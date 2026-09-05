using System.Buffers;
using System.Threading.Channels;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

internal sealed class LiveSession
{
    private readonly Lock gate = new();

    private readonly LiveFanout fanout;

    private readonly LiveSessionSettings settings;

    private readonly LiveReception reception;

    private readonly ILiveTranscoderFactory transcoders;

    private readonly TimeProvider clock;

    private readonly Action<LiveSession> forget;

    private readonly CancellationTokenSource stopping = new();

    private readonly TaskCompletionSource<LiveJoin?> raised = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly LiveStartupRecord startup;

    private readonly LiveEndingRecord ending = new();

    private LiveSeat? seat;

    private ILiveTranscoder? transcoder;

    private CaptionOutlet captions;

    private Task<LiveFragmentFault?>? carrying;

    private Task? captioning;

    private ITimer? linger;

    private int expected;

    private bool closed;

    internal LiveSession(
        LiveSessionKey key,
        LiveFanoutSettings fanouts,
        LiveSessionSettings settings,
        LiveReception reception,
        ILiveTranscoderFactory transcoders,
        TimeProvider clock,
        Action<LiveSession> forget)
    {
        Key = key;
        startup = new LiveStartupRecord(clock);
        fanout = new LiveFanout(fanouts, startup, ending);
        this.settings = settings;
        this.reception = reception;
        this.transcoders = transcoders;
        this.clock = clock;
        this.forget = forget;
    }

    public LiveSessionKey Key { get; }

    internal LiveReception Reception => reception;

    public Task Life { get; private set; } = Task.CompletedTask;

    public int Viewers => fanout.Viewers;

    public long Dropped => fanout.Dropped;

    public int Queued => fanout.Queued;

    public ILiveStartup Startup => startup;

    public ILiveEnding Ending => ending;

    internal bool NobodyIsWatching
    {
        get
        {
            lock (gate)
            {
                return expected is 0;
            }
        }
    }

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
            if (await RaisedAsync(cancellationToken) is { } refused)
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

    private async Task<LiveJoin?> RaisedAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await raised.Task.WaitAsync(settings.LongestRaise, clock, cancellationToken);
        }
        catch (TimeoutException)
        {
            Close();

            return GaveUp($"nothing was ready to watch within {settings.LongestRaise}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return GaveUp("this session was closed before it was ever raised.");
        }
    }

    private LiveJoin GaveUp(string note)
        => startup.Current is { } where && where.Reached(LiveStartupSegment.TunerSecured)
            ? LiveJoin.Refused(LiveRefusal.TranscoderWouldNotStart, $"the tuner was secured and {note}")
            : LiveJoin.Refused(LiveRefusal.DriverUnavailable, $"no transport stream was opened and {note}");

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
        LiveSupplyStart opened = await reception.OpenAsync(cancellationToken);

        if (opened.Stream is null)
        {
            return LiveJoin.Refused(opened.Refusal!.Value, opened.Note, opened.Detail);
        }

        startup.Reach(LiveStartupSegment.TunerSecured);

        return await StartTranscodingAsync(
            reception.CaptionsMissing ? CaptionOutlet.None : CaptionOutlet.Drawn,
            cancellationToken);
    }

    private async Task<LiveJoin?> StartTranscodingAsync(CaptionOutlet asked, CancellationToken cancellationToken)
    {
        LiveTranscoderStart started = await transcoders.StartAsync(
            Key.Service,
            Key.Profile,
            StreamAttributes.SafeSide,
            asked,
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
            captions = asked;
            seat = reception.Take(running.Input, () => startup.Reach(LiveStartupSegment.ChannelLocked), ending.Note);
        }

        startup.Reach(LiveStartupSegment.TranscoderStarted);

        return null;
    }

    /// <summary>
    /// A service without a caption stream makes ffmpeg refuse the whole command, picture included,
    /// so a transcoder that ends before writing anything for that reason is started again without
    /// captions, and the reading remembers so that the next profile of this channel does not try.
    /// </summary>
    private async Task<Stream?> OutputOrRestartAsync(CancellationToken cancellationToken)
    {
        ILiveTranscoder running;
        CaptionOutlet asked;

        lock (gate)
        {
            running = transcoder!;
            asked = captions;
        }

        ReadOnlyMemory<byte> first = await FirstBytesAsync(running, cancellationToken);

        if (!first.IsEmpty || asked is CaptionOutlet.None)
        {
            return new FirstBytesThenTheRest(first, running.Output);
        }

        TranscoderExit exit = await running.Completion;

        if (!FfmpegComplaints.RefusedForWantOfACaptionStream(exit.Note))
        {
            return new FirstBytesThenTheRest(first, running.Output);
        }

        reception.MissCaptions();
        await LetGoOfTranscoderAsync();

        if (await StartTranscodingAsync(CaptionOutlet.None, cancellationToken) is not null)
        {
            return null;
        }

        lock (gate)
        {
            running = transcoder!;
        }

        return new FirstBytesThenTheRest(await FirstBytesAsync(running, cancellationToken), running.Output);
    }

    private static async Task<ReadOnlyMemory<byte>> FirstBytesAsync(ILiveTranscoder running, CancellationToken cancellationToken)
    {
        byte[] mouthful = new byte[LiveFeed.Mouthful];

        try
        {
            int read = await running.Output.ReadAsync(mouthful, cancellationToken);

            return mouthful.AsMemory(0, read);
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    private async Task LetGoOfTranscoderAsync()
    {
        ILiveTranscoder? running;
        LiveSeat? given;

        lock (gate)
        {
            running = transcoder;
            given = seat;
            transcoder = null;
            seat = null;
        }

        if (given is not null)
        {
            reception.Drop(given);
        }

        if (running is not null)
        {
            await running.DisposeAsync();
        }
    }

    private static async Task CaptionAsync(ChannelReader<LiveFrame> drawn, LiveFanout into, CancellationToken cancellationToken)
    {
        try
        {
            while (await drawn.WaitToReadAsync(cancellationToken))
            {
                while (drawn.TryRead(out LiveFrame? frame))
                {
                    into.Publish(frame);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CarryAsync(CancellationToken cancellationToken)
    {
        if (await OutputOrRestartAsync(cancellationToken) is not { } output)
        {
            return;
        }

        ILiveTranscoder running;
        CaptionOutlet drawn;

        lock (gate)
        {
            running = transcoder!;
            drawn = captions;
        }

        if (drawn is CaptionOutlet.Drawn)
        {
            fanout.Publish(LiveCaptions.Canvas(StreamAttributes.SafeSide.Size));
        }

        Task<LiveFragmentFault?> carried = LiveFeed.CarryAsync(output, fanout, cancellationToken, Published);
        Task captioned = CaptionAsync(running.Captions, fanout, cancellationToken);

        lock (gate)
        {
            carrying = carried;
            captioning = captioned;
        }

        TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenRegistration whenStopped = cancellationToken.UnsafeRegister(_ => stopped.TrySetResult(), null);

        await Task.WhenAny(carried, stopped.Task);
    }

    private async Task TearDownAsync()
    {
        ILiveTranscoder? running;
        LiveSeat? given;
        Task<LiveFragmentFault?>? carried;
        Task? captioned;

        lock (gate)
        {
            running = transcoder;
            given = seat;
            carried = carrying;
            captioned = captioning;
            transcoder = null;
            seat = null;
        }

        // Out of the reading first, so nothing is written into a transcoder being taken down.
        if (given is not null)
        {
            reception.Drop(given);
        }

        try
        {
            await StopTranscodingAsync(running, carried, captioned);
        }
        finally
        {
            fanout.End();
            reception.Detach();
        }
    }

    private static async Task StopTranscodingAsync(ILiveTranscoder? running, Task<LiveFragmentFault?>? carried, Task? captioned)
    {
        if (running is null)
        {
            return;
        }

        Stream output = running.Output;
        Task disposing = running.DisposeAsync().AsTask();

        if (carried is not null)
        {
            await Quietly(carried);
        }

        if (captioned is not null)
        {
            await Quietly(captioned);
        }

        await DrainAsync(output);
        await disposing;
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
