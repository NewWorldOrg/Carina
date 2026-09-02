using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class DriverTransportStream : ILiveTransportStream
{
    public const string LetGoBecause = "the last viewer has left";

    private static readonly LiveSupplyEnding Draining = LiveSupplyEnding.Of(
        LiveSupplyEnd.DriverDraining,
        "the driver is shutting down and does not wait for live viewing.");

    private readonly SessionId session;

    private readonly Stream inner;

    private readonly IDriverClient driver;

    private readonly IDriverStatusReader status;

    private LiveSupplyEnding? ending;

    private int letGo;

    public DriverTransportStream(SessionId session, Stream inner, IDriverClient driver, IDriverStatusReader status)
    {
        this.session = session;
        this.inner = inner;
        this.driver = driver;
        this.status = status;
        Bytes = new Reading(this);
    }

    public SessionId Session => session;

    public Stream Bytes { get; }

    public LiveSupplyEnding? Ending => Volatile.Read(ref ending);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref letGo, 1) is 1)
        {
            return;
        }

        bool stillHeld = Interlocked.CompareExchange(
            ref ending,
            LiveSupplyEnding.Of(LiveSupplyEnd.LetGo, LetGoBecause),
            null) is null;

        await inner.DisposeAsync();

        if (stillHeld)
        {
            await driver.StopSessionAsync(session, LetGoBecause, CancellationToken.None);
        }
    }

    private async ValueTask<int> ReadAsync(Memory<byte> into, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref letGo) is 1 || Ending is not null)
        {
            return 0;
        }

        int read;

        try
        {
            read = await inner.ReadAsync(into, cancellationToken);
        }
        catch (Exception gone) when (gone is IOException or HttpRequestException or ObjectDisposedException)
        {
            read = 0;
        }

        if (read > 0)
        {
            return read;
        }

        if (Volatile.Read(ref letGo) is 0)
        {
            Interlocked.CompareExchange(ref ending, await WhyItEndedAsync(cancellationToken), null);
        }

        return 0;
    }

    private async Task<LiveSupplyEnding> WhyItEndedAsync(CancellationToken cancellationToken)
    {
        DriverCall<SessionSnapshot> asked = await driver.GetSessionAsync(session, cancellationToken);

        if (asked.Outcome is DriverCallOutcome.Unreachable)
        {
            return await DrainingAsync(cancellationToken)
                ? Draining
                : LiveSupplyEnding.Of(LiveSupplyEnd.DriverLost, asked.Failure ?? "the driver could not be reached.");
        }

        if (!asked.TryGetValue(out SessionSnapshot? snapshot))
        {
            return LiveSupplyEnding.Of(
                LiveSupplyEnd.DriverLost,
                $"the driver no longer speaks of this session ({asked.Problem?.Title}).");
        }

        switch (snapshot.StopReason)
        {
            case SessionStopReason.Preempted:
                return LiveSupplyEnding.Of(
                    LiveSupplyEnd.TakenForARecording,
                    snapshot.FailureCause ?? "a recording outranked this viewing and took the tuner.");
            case SessionStopReason.DrainCapReached:
                return Draining;
            case SessionStopReason.EndTimeReached:
                return LiveSupplyEnding.Of(
                    LiveSupplyEnd.WindowClosed,
                    $"the driver holds a live session open until {snapshot.EndsAt:O} and no longer.");
            case SessionStopReason.DeviceFailed:
            case SessionStopReason.RecordingFailed:
                return LiveSupplyEnding.Of(LiveSupplyEnd.TunerFailed, snapshot.FailureCause ?? "the tuner failed.");
            default:
                break;
        }

        if (snapshot.State is SessionState.Failed)
        {
            return LiveSupplyEnding.Of(
                LiveSupplyEnd.TunerFailed,
                snapshot.FailureCause ?? snapshot.FirstFault ?? "the tuner stopped delivering.");
        }

        if (await DrainingAsync(cancellationToken))
        {
            return Draining;
        }

        return snapshot.StopReason is SessionStopReason.Requested
            ? LiveSupplyEnding.Of(LiveSupplyEnd.StoppedByAnother, "something other than this viewing asked the driver to stop the session.")
            : LiveSupplyEnding.Of(LiveSupplyEnd.DriverLost, "the stream ended while the driver still holds the session.");
    }

    private async Task<bool> DrainingAsync(CancellationToken cancellationToken)
    {
        DriverObservation seen = await status.ReadAsync(cancellationToken);

        if (seen.Connection is DriverConnection.Draining)
        {
            return true;
        }

        DriverCall<DriverHello> health = await driver.GetHealthAsync(cancellationToken);

        return health.TryGetValue(out DriverHello? hello) && hello.Draining;
    }

    private sealed class Reading(DriverTransportStream owner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => owner.ReadAsync(buffer, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
