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

    private readonly ILiveCaptionerFactory captioners;

    private readonly TimeProvider clock;

    private readonly Action<LiveSession> forget;

    private readonly CancellationTokenSource stopping = new();

    private readonly TaskCompletionSource<LiveJoin?> raised = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly LiveStartupRecord startup;

    private readonly LiveEndingRecord ending = new();

    private LiveSeat? seat;

    private ILiveTranscoder? transcoder;

    private ILiveCaptioner? captioner;

    private CaptionSupply? captionSupply;

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
        ILiveCaptionerFactory captioners,
        TimeProvider clock,
        Action<LiveSession> forget)
    {
        Key = key;
        startup = new LiveStartupRecord(clock);
        fanout = new LiveFanout(fanouts, startup, ending);
        this.settings = settings;
        this.reception = reception;
        this.transcoders = transcoders;
        this.captioners = captioners;
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

        StreamAttributes attributes = StreamAttributes.SafeSide;

        LiveTranscoderStart started = await transcoders.StartAsync(
            Key.Service,
            Key.Profile,
            attributes,
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

        LiveCaptionerStart drawn = await captioners.StartAsync(Key.Service, attributes, cancellationToken);

        if (drawn.Captioner is { } drawing)
        {
            lock (gate)
            {
                captioner = drawing;
                captionSupply = new CaptionSupply(drawing.Input);
            }

            fanout.Publish(LiveCaptions.Canvas(attributes.Size));
        }

        lock (gate)
        {
            seat = reception.Take(
                running.Input,
                captionSupply,
                () => startup.Reach(LiveStartupSegment.ChannelLocked),
                ending.Note);
        }

        return null;
    }

    private static async Task CaptionAsync(ILiveCaptioner drawing, LiveFanout into, CancellationToken cancellationToken)
    {
        try
        {
            while (await drawing.Frames.WaitToReadAsync(cancellationToken))
            {
                while (drawing.Frames.TryRead(out LiveFrame? frame))
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
        ILiveTranscoder running;
        ILiveCaptioner? drawing;

        lock (gate)
        {
            running = transcoder!;
            drawing = captioner;
        }

        Task<LiveFragmentFault?> carried = LiveFeed.CarryAsync(running.Output, fanout, cancellationToken, Published);
        Task? captioned = drawing is null ? null : CaptionAsync(drawing, fanout, cancellationToken);

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
        ILiveCaptioner? drawing;
        CaptionSupply? drawingFrom;
        LiveSeat? given;
        Task<LiveFragmentFault?>? carried;
        Task? captioned;

        lock (gate)
        {
            running = transcoder;
            drawing = captioner;
            drawingFrom = captionSupply;
            given = seat;
            carried = carrying;
            captioned = captioning;
            transcoder = null;
            captioner = null;
            captionSupply = null;
            seat = null;
        }

        // Out of the reading first, so nothing is written into a transcoder being taken down.
        if (given is not null)
        {
            reception.Drop(given);
        }

        try
        {
            await StopDrawingAsync(drawingFrom, drawing, captioned);
        }
        finally
        {
            try
            {
                await StopTranscodingAsync(running, carried);
            }
            finally
            {
                fanout.End();
                reception.Detach();
            }
        }
    }

    private static async Task StopDrawingAsync(CaptionSupply? drawingFrom, ILiveCaptioner? drawing, Task? captioned)
    {
        if (drawingFrom is not null)
        {
            await drawingFrom.CompleteAsync();
        }

        if (drawing is not null)
        {
            await drawing.DisposeAsync();
        }

        if (captioned is not null)
        {
            await Quietly(captioned);
        }
    }

    private static async Task StopTranscodingAsync(ILiveTranscoder? running, Task<LiveFragmentFault?>? carried)
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
