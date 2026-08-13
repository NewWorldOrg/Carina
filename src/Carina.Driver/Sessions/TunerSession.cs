using Carina.Contracts;
using Carina.Driver.Recording;
using Carina.Driver.Transport;
using Carina.Driver.Tuning;

namespace Carina.Driver.Sessions;

public sealed class TunerSession : IDisposable
{
    public const int DefaultChunkSize = TsPacketReader.PacketLength * 100;

    private readonly ITunerDevice device;
    private readonly RecordingWriter? recordingWriter;
    private readonly TimeProvider timeProvider;
    private readonly TsPacketReader packetReader = new();
    private readonly int chunkSize;
    private readonly Lock gate = new();

    private Thread? loop;
    private volatile bool stopRequested;
    private long endsAtTicks;

    public TunerSession(
        SessionId sessionId,
        SessionPurpose purpose,
        string deviceId,
        ITunerDevice device,
        DateTimeOffset startedAt,
        DateTimeOffset endsAt,
        TimeProvider timeProvider,
        RecordingWriter? recordingWriter = null,
        int chunkSize = DefaultChunkSize
    )
    {
        if (endsAt <= startedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endsAt),
                $"A session ends after it starts; got {endsAt:O} against {startedAt:O}."
            );
        }

        SessionId = sessionId;
        Purpose = purpose;
        DeviceId = deviceId;
        StartedAt = startedAt;
        endsAtTicks = endsAt.UtcTicks;
        this.device = device;
        this.recordingWriter = recordingWriter;
        this.timeProvider = timeProvider;
        this.chunkSize = chunkSize;
        State = SessionState.Requested;
    }

    public SessionId SessionId { get; }

    public SessionPurpose Purpose { get; }

    public string DeviceId { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset EndsAt =>
        new(Interlocked.Read(ref endsAtTicks), TimeSpan.Zero);

    public SessionState State { get; private set; }

    public SessionBroadcaster Broadcaster { get; } = new();

    public ContinuityCounterTracker Counters { get; } = new();

    public long BytesRecorded => recordingWriter?.BytesWritten ?? 0;

    public Exception? FailureCause { get; private set; }

    public event Action<TunerSession>? Ended;

    public void Start()
    {
        lock (gate)
        {
            if (State is not SessionState.Requested)
            {
                throw new InvalidOperationException(
                    $"A session starts once; '{SessionId}' is already {State}."
                );
            }

            State = SessionState.Active;
        }

        loop = new Thread(Run) { IsBackground = true, Name = $"session-{SessionId}" };
        loop.Start();
    }

    public bool Extend(DateTimeOffset newEndsAt)
    {
        while (true)
        {
            var current = Interlocked.Read(ref endsAtTicks);
            if (newEndsAt.UtcTicks <= current)
            {
                return false;
            }

            if (
                Interlocked.CompareExchange(ref endsAtTicks, newEndsAt.UtcTicks, current)
                == current
            )
            {
                return true;
            }
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            if (State is SessionState.Active)
            {
                State = SessionState.Stopping;
            }
        }

        stopRequested = true;
    }

    public void WaitForEnd(TimeSpan timeout) => loop?.Join(timeout);

    public void Dispose()
    {
        Stop();
        loop?.Join(TimeSpan.FromSeconds(5));
    }

    private void Run()
    {
        try
        {
            while (!stopRequested && timeProvider.GetUtcNow() < EndsAt)
            {
                var chunk = device.Read(chunkSize);

                recordingWriter?.Write(chunk);

                foreach (var packet in packetReader.Read(chunk))
                {
                    Counters.Observe(packet);
                }

                Broadcaster.Publish(chunk);
            }

            Finish(SessionState.Stopped, null);
        }
        catch (Exception error)
        {
            Finish(SessionState.Failed, error);
        }
    }

    private void Finish(SessionState state, Exception? cause)
    {
        lock (gate)
        {
            State = state;
            FailureCause = cause;
        }

        Broadcaster.Dispose();
        recordingWriter?.Dispose();
        device.Dispose();

        Ended?.Invoke(this);
    }
}
