using System.Buffers;
using System.Threading.Channels;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

internal sealed class LiveSession
{
    private readonly Lock gate = new();

    private readonly LiveFanout fanout;

    private readonly LiveSessionSettings settings;

    private readonly ILiveSupply supply;

    private readonly ILiveTranscoderFactory transcoders;

    private readonly TimeProvider clock;

    private readonly Action<LiveSession> forget;

    private readonly CancellationTokenSource stopping = new();

    private readonly TaskCompletionSource<LiveJoin?> raised = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly LiveStartupRecord startup;

    private readonly LiveEndingRecord ending = new();

    private ILiveTransportStream? stream;

    private ILiveTranscoder? transcoder;

    private Task<LiveFragmentFault?>? carrying;

    private Task? feeding;

    private ITimer? linger;

    private int expected;

    private bool closed;

    internal LiveSession(
        LiveSessionKey key,
        LiveFanoutSettings fanouts,
        LiveSessionSettings settings,
        ILiveSupply supply,
        ILiveTranscoderFactory transcoders,
        TimeProvider clock,
        Action<LiveSession> forget)
    {
        Key = key;
        startup = new LiveStartupRecord(clock);
        fanout = new LiveFanout(fanouts, startup, ending);
        this.settings = settings;
        this.supply = supply;
        this.transcoders = transcoders;
        this.clock = clock;
        this.forget = forget;
    }

    public LiveSessionKey Key { get; }

    public Task Life { get; private set; } = Task.CompletedTask;

    public int Viewers => fanout.Viewers;

    public long Dropped => fanout.Dropped;

    public ILiveStartup Startup => startup;

    public ILiveEnding Ending => ending;

    internal void Start() => Life = LiveAsync();

    internal bool Expect()
    {
        lock (gate)
        {
            if (closed)
            {
                return false;
            }

            expected++;
            linger?.Dispose();
            linger = null;

            return true;
        }
    }

    internal async Task<LiveJoin?> JoinAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await raised.Task.WaitAsync(cancellationToken) is { } refused)
            {
                Left();

                return refused;
            }

            if (await fanout.JoinAsync(cancellationToken) is not { } viewing)
            {
                Left();

                return null;
            }

            return LiveJoin.Joined(new Seat(viewing, Left));
        }
        catch (Exception)
        {
            Left();

            throw;
        }
    }

    internal void Close()
    {
        lock (gate)
        {
            closed = true;
            linger?.Dispose();
            linger = null;
        }

        forget(this);
        stopping.Cancel();
    }

    private async Task FeedAsync(ILiveTransportStream from, Stream into, CancellationToken cancellationToken)
    {
        byte[] mouthful = ArrayPool<byte>.Shared.Rent(LiveFeed.Mouthful);

        try
        {
            int read;

            while ((read = await from.Bytes.ReadAsync(mouthful, cancellationToken)) > 0)
            {
                startup.Reach(LiveStartupSegment.ChannelLocked);

                await into.WriteAsync(mouthful.AsMemory(0, read), cancellationToken);
                await into.FlushAsync(cancellationToken);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                ending.Note(from.Ending ?? LiveSupplyEnding.Of(
                    LiveSupplyEnd.DriverLost,
                    "the transport stream ended and the supply did not say why."));
            }

            into.Close();
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(mouthful);
        }
    }

    private void Published(LiveFrame frame)
    {
        switch (frame.Channel)
        {
            case LiveChannel.PictureHeader:
                startup.Reach(LiveStartupSegment.InitReached);
                break;
            case LiveChannel.Picture:
                startup.Reach(LiveStartupSegment.FirstPicture);
                break;
            default:
                break;
        }
    }

    private static async Task Quietly(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return;
        }
    }

    private void Left()
    {
        lock (gate)
        {
            expected--;

            if (expected > 0 || closed)
            {
                return;
            }

            linger?.Dispose();
            linger = clock.CreateTimer(_ => LingerOver(), null, settings.Linger, Timeout.InfiniteTimeSpan);
        }
    }

    private void LingerOver()
    {
        lock (gate)
        {
            if (expected > 0 || closed)
            {
                return;
            }
        }

        Close();
    }

    private async Task LiveAsync()
    {
        try
        {
            if (await RaiseAsync(stopping.Token) is { } refusal)
            {
                Close();
                raised.SetResult(refusal);

                return;
            }

            raised.SetResult(null);

            await CarryAsync(stopping.Token);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            raised.TrySetCanceled(stopping.Token);
        }
        catch (Exception failure)
        {
            raised.TrySetException(failure);
        }
        finally
        {
            Close();
            await TearDownAsync();
        }
    }

    private async Task<LiveJoin?> RaiseAsync(CancellationToken cancellationToken)
    {
        LiveSupplyStart opened = await supply.OpenAsync(Key.Network, Key.Service, cancellationToken);

        if (opened.Stream is not { } bytes)
        {
            return LiveJoin.Refused(opened.Refusal!.Value, opened.Note);
        }

        lock (gate)
        {
            stream = bytes;
        }

        startup.Reach(LiveStartupSegment.TunerSecured);

        LiveTranscoderStart started = await transcoders.StartAsync(
            Key.Service,
            Key.Profile,
            StreamAttributes.SafeSide,
            cancellationToken);

        if (started.Transcoder is not { } running)
        {
            return started.Ceiling is { } full
                ? LiveJoin.Refused(full)
                : LiveJoin.Refused(LiveRefusal.TranscoderWouldNotStart, started.Note);
        }

        lock (gate)
        {
            transcoder = running;
        }

        startup.Reach(LiveStartupSegment.TranscoderStarted);

        return null;
    }

    private async Task CarryAsync(CancellationToken cancellationToken)
    {
        ILiveTransportStream bytes;
        ILiveTranscoder running;

        lock (gate)
        {
            bytes = stream!;
            running = transcoder!;
        }

        Task<LiveFragmentFault?> carried = LiveFeed.CarryAsync(running.Output, fanout, cancellationToken, Published);
        Task fed = FeedAsync(bytes, running.Input, cancellationToken);

        lock (gate)
        {
            carrying = carried;
            feeding = fed;
        }

        TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenRegistration whenStopped = cancellationToken.UnsafeRegister(_ => stopped.TrySetResult(), null);

        await Task.WhenAny(carried, stopped.Task);
    }

    private async Task TearDownAsync()
    {
        ILiveTranscoder? running;
        ILiveTransportStream? bytes;
        Task<LiveFragmentFault?>? carried;
        Task? fed;

        lock (gate)
        {
            running = transcoder;
            bytes = stream;
            carried = carrying;
            fed = feeding;
            transcoder = null;
            stream = null;
        }

        if (running is not null)
        {
            Task disposing = running.DisposeAsync().AsTask();

            if (carried is not null)
            {
                await Quietly(carried);
            }

            await DrainAsync(running.Output);
            await disposing;
        }

        if (bytes is not null)
        {
            await bytes.DisposeAsync();
        }

        if (fed is not null)
        {
            await Quietly(fed);
        }

        fanout.End();
    }

    private static async Task DrainAsync(Stream from)
    {
        byte[] mouthful = ArrayPool<byte>.Shared.Rent(LiveFeed.Mouthful);

        try
        {
            while (await from.ReadAsync(mouthful, CancellationToken.None) > 0)
            {
            }
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(mouthful);
        }
    }

    private sealed class Seat(ILiveViewing viewing, Action left) : ILiveViewing
    {
        private bool letGo;

        public ChannelReader<LiveFrame> Frames => viewing.Frames;

        public LiveBacklog Backlog => viewing.Backlog;

        public ILiveStartup? Startup => viewing.Startup;

        public ILiveEnding? Ending => viewing.Ending;

        public async ValueTask DisposeAsync()
        {
            if (letGo)
            {
                return;
            }

            letGo = true;

            await viewing.DisposeAsync();

            left();
        }
    }
}
