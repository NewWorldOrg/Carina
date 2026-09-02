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

    private ILiveTransportStream? stream;

    private ILiveTranscoder? transcoder;

    private Task? pumping;

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
        fanout = new LiveFanout(fanouts);
        this.settings = settings;
        this.supply = supply;
        this.transcoders = transcoders;
        this.clock = clock;
        this.forget = forget;
    }

    public LiveSessionKey Key { get; }

    public Task Life { get; private set; } = Task.CompletedTask;

    public int Viewers => fanout.Viewers;

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

    private static async Task FeedAsync(Stream from, Stream into, CancellationToken cancellationToken)
    {
        byte[] mouthful = ArrayPool<byte>.Shared.Rent(LiveFeed.Mouthful);

        try
        {
            int read;

            while ((read = await from.ReadAsync(mouthful, cancellationToken)) > 0)
            {
                await into.WriteAsync(mouthful.AsMemory(0, read), cancellationToken);
                await into.FlushAsync(cancellationToken);
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

        LiveTranscoderStart started = await transcoders.StartAsync(
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

        Task<LiveFragmentFault?> carrying = LiveFeed.CarryAsync(running.Output, fanout, cancellationToken);
        Task feeding = FeedAsync(bytes.Bytes, running.Input, cancellationToken);

        lock (gate)
        {
            pumping = Task.WhenAll(Quietly(carrying), Quietly(feeding));
        }

        TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenRegistration whenStopped = cancellationToken.UnsafeRegister(_ => stopped.TrySetResult(), null);

        await Task.WhenAny(carrying, stopped.Task);
    }

    private async Task TearDownAsync()
    {
        ILiveTranscoder? running;
        ILiveTransportStream? bytes;
        Task? pumped;

        lock (gate)
        {
            running = transcoder;
            bytes = stream;
            pumped = pumping;
            transcoder = null;
            stream = null;
        }

        if (running is not null)
        {
            Task drained = DrainAsync(running.Output);

            await running.DisposeAsync();
            await drained;
        }

        if (bytes is not null)
        {
            await bytes.DisposeAsync();
        }

        if (pumped is not null)
        {
            await pumped;
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
        catch (Exception gone) when (gone is IOException or ObjectDisposedException)
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
