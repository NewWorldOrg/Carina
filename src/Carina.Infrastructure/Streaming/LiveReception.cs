using System.Buffers;
using System.Threading.Channels;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// One reading of one channel off the tuner, shared by every profile being made from it.
/// </summary>
/// <remarks>
/// A viewer changing quality is a new session on a new key, and the key carries the profile, so
/// for a moment two sessions want the same channel. Asked for the same channel twice, the driver
/// gives the second one a seat on the first one's stream, and cuts it when the first one ends —
/// which is exactly what changing quality does to the session left behind. The profile decides
/// how the picture is encoded and nothing about how it is received, so the reception is held per
/// channel and the transcoders hang off it.
/// </remarks>
internal sealed class LiveReception
{
    private readonly Lock gate = new();

    private readonly NetworkId network;

    private readonly ServiceId service;

    private readonly ILiveSupply supply;

    private readonly LiveSessionSettings settings;

    private readonly TimeProvider clock;

    private readonly Action<LiveReception> forget;

    private readonly CancellationTokenSource stopping = new();

    private readonly List<LiveSeat> seats = [];

    private Task<LiveSupplyStart>? opening;

    private ILiveTransportStream? stream;

    private int attached;

    private bool closed;

    private bool captionsMissing;

    internal LiveReception(
        NetworkId network,
        ServiceId service,
        ILiveSupply supply,
        LiveSessionSettings settings,
        TimeProvider clock,
        Action<LiveReception> forget)
    {
        this.network = network;
        this.service = service;
        this.supply = supply;
        this.settings = settings;
        this.clock = clock;
        this.forget = forget;
    }

    internal NetworkId Network => network;

    internal ServiceId Service => service;

    internal Task Life { get; private set; } = Task.CompletedTask;

    internal Task Holding { get; private set; } = Task.CompletedTask;

    internal LiveSupplyEnding? Ending => stream?.Ending;

    internal SessionId? Supply
    {
        get
        {
            lock (gate)
            {
                return stream?.Supply;
            }
        }
    }

    /// <summary>
    /// Whether a transcoder of this channel has found that the service carries no caption stream, in
    /// which case the ones raised after it are not asked to draw captions.
    /// </summary>
    internal bool CaptionsMissing
    {
        get
        {
            lock (gate)
            {
                return captionsMissing;
            }
        }
    }

    internal void MissCaptions()
    {
        lock (gate)
        {
            captionsMissing = true;
        }
    }

    internal bool Attach()
    {
        lock (gate)
        {
            if (closed)
            {
                return false;
            }

            attached++;

            return true;
        }
    }

    internal void Detach()
    {
        lock (gate)
        {
            attached--;

            if (attached > 0 || closed)
            {
                return;
            }
        }

        Close();
    }

    /// <summary>
    /// Opens the supply once however many sessions ask, and hands every asker the same answer.
    /// </summary>
    internal Task<LiveSupplyStart> OpenAsync(CancellationToken cancellationToken)
    {
        Task<LiveSupplyStart> answering;

        lock (gate)
        {
            answering = opening ??= RaiseAsync();
        }

        return answering.WaitAsync(cancellationToken);
    }

    internal LiveSeat Take(Stream into, TimeSpan patience)
        => Take(into, static () => { }, static _ => { }, patience, settings.MostBytesWaitingToBeFed);

    internal LiveSeat Take(
        Stream into,
        Action locked,
        Action<LiveSupplyEnding> ended)
        => Take(into, locked, ended, settings.LongestWaitToBeFed, settings.MostBytesWaitingToBeFed);

    /// <summary>
    /// Takes the seat out of the reading and calls off what is being written into it, and hands back
    /// the task that ends once nothing more is being written.
    /// </summary>
    internal Task Drop(LiveSeat seat)
    {
        lock (gate)
        {
            seats.Remove(seat);
        }

        seat.LetGo();

        return seat.Pumping;
    }

    internal void Close()
    {
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            closed = true;
        }

        forget(this);
        stopping.Cancel();
    }

    private LiveSeat Take(
        Stream into,
        Action locked,
        Action<LiveSupplyEnding> ended,
        TimeSpan patience,
        long mostHeld)
    {
        LiveSeat seat = new(into, locked, ended, patience, mostHeld, clock);

        lock (gate)
        {
            seats.Add(seat);
        }

        return seat;
    }

    private async Task<LiveSupplyStart> RaiseAsync()
    {
        LiveSupplyStart opened = await supply.OpenAsync(network, service, stopping.Token);

        if (opened.Stream is not { } bytes)
        {
            Close();

            return opened;
        }

        lock (gate)
        {
            stream = bytes;
        }

        Holding = HoldOpenAsync(bytes);
        Life = CarryAsync(bytes);

        return opened;
    }

    /// <summary>
    /// Says, for as long as this reading is attached to, that what it is reading is still being read.
    /// </summary>
    /// <remarks>
    /// The reading is let go of within one linger of the last viewer leaving, so a supply that is
    /// still being asked for is one somebody is still behind, and one that stops being asked for is
    /// let go of by the driver a window later even if this app never says so.
    /// </remarks>
    private async Task HoldOpenAsync(ILiveTransportStream held)
    {
        try
        {
            while (true)
            {
                await Task.Delay(settings.BetweenHolds, clock, stopping.Token);
                await held.HoldOpenUntilAsync(clock.GetUtcNow() + settings.HeldAhead, stopping.Token);
            }
        }
        catch (Exception gone) when (gone is OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private async Task CarryAsync(ILiveTransportStream from)
    {
        byte[] mouthful = ArrayPool<byte>.Shared.Rent(LiveFeed.Mouthful);

        try
        {
            int read;

            while ((read = await from.Bytes.ReadAsync(mouthful, stopping.Token)) > 0)
            {
                Feed(mouthful.AsSpan(0, read).ToArray());
            }

            EndEverySeat(from.Ending ?? LiveSupplyEnding.Of(
                LiveSupplyEnd.DriverLost,
                "the transport stream ended and the supply did not say why."));
        }
        catch (Exception gone)
            when (gone is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            EndEverySeat(from.Ending);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(mouthful);
            Close();
            await from.DisposeAsync();
        }
    }

    private void Feed(ReadOnlyMemory<byte> mouthful)
    {
        foreach (LiveSeat seat in Seated())
        {
            if (seat.Offer(mouthful))
            {
                continue;
            }

            Drop(seat);

            if (seat.FellBehind)
            {
                seat.Ended(LiveSupplyEnding.Of(
                    LiveSupplyEnd.TranscoderFellBehind,
                    $"the transcoder left more than {seat.MostHeld} bytes, or bytes older than {seat.Patience}, untaken."));
            }

            seat.NoMore();
        }
    }

    private void EndEverySeat(LiveSupplyEnding? why)
    {
        foreach (LiveSeat seat in Seated())
        {
            if (why is { } ending)
            {
                seat.Ended(ending);
            }

            seat.NoMore();
        }
    }

    private IReadOnlyList<LiveSeat> Seated()
    {
        lock (gate)
        {
            return [.. seats];
        }
    }
}

/// <summary>
/// One transcoder's place at a reading of the channel, with the mouthfuls it has not taken yet
/// queued in front of it.
/// </summary>
/// <remarks>
/// The seat is refused further mouthfuls once the oldest one it has not taken has waited longer
/// than its patience, once the bytes it has not taken would come to more than it is held, or once
/// writing into it has failed.
/// </remarks>
internal sealed class LiveSeat
{
    private const long NothingWaiting = long.MinValue;

    private readonly Stream into;

    private readonly Action locked;

    private readonly Action<LiveSupplyEnding> ended;

    private readonly TimeSpan patience;

    private readonly long mostHeld;

    private readonly TimeProvider clock;

    private readonly Channel<Offered> backlog = Channel.CreateUnbounded<Offered>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly CancellationTokenSource letGo = new();

    private long waitingSince = NothingWaiting;

    private long held;

    private int refused;

    private int behind;

    private int noMore;

    private int emptied;

    private int closed;

    internal LiveSeat(
        Stream into,
        Action locked,
        Action<LiveSupplyEnding> ended,
        TimeSpan patience,
        long mostHeld,
        TimeProvider clock)
    {
        this.into = into;
        this.locked = locked;
        this.ended = ended;
        this.patience = patience;
        this.mostHeld = mostHeld;
        this.clock = clock;
        Pumping = PumpAsync();
    }

    internal Task Pumping { get; }

    internal TimeSpan Patience => patience;

    internal long MostHeld => mostHeld;

    internal bool FellBehind => Volatile.Read(ref behind) is not 0;

    private bool WaitedTooLong
    {
        get
        {
            long since = Volatile.Read(ref waitingSince);

            return since is not NothingWaiting && clock.GetUtcNow().UtcTicks - since > patience.Ticks;
        }
    }

    internal bool Offer(ReadOnlyMemory<byte> mouthful)
    {
        if (letGo.IsCancellationRequested || Volatile.Read(ref refused) is not 0 || FellBehind)
        {
            return false;
        }

        if (WaitedTooLong || Volatile.Read(ref held) + mouthful.Length > mostHeld)
        {
            Interlocked.Exchange(ref behind, 1);

            return false;
        }

        Interlocked.Add(ref held, mouthful.Length);

        return backlog.Writer.TryWrite(new Offered(mouthful, clock.GetUtcNow().UtcTicks));
    }

    internal void LetGo()
    {
        letGo.Cancel();
        backlog.Writer.TryComplete();
    }

    internal void Ended(LiveSupplyEnding why) => ended(why);

    /// <summary>
    /// Closes what the transcoder reads from once the mouthfuls already queued have been written,
    /// or at once if nothing more is being written into it.
    /// </summary>
    internal void NoMore()
    {
        Interlocked.Exchange(ref noMore, 1);
        backlog.Writer.TryComplete();

        if (Volatile.Read(ref emptied) is not 0)
        {
            Close();
        }
    }

    private async Task PumpAsync()
    {
        bool fed = false;

        try
        {
            ChannelReader<Offered> queued = backlog.Reader;

            while (await queued.WaitToReadAsync(letGo.Token))
            {
                while (queued.TryRead(out Offered next))
                {
                    Volatile.Write(ref waitingSince, next.At);

                    await into.WriteAsync(next.Bytes, letGo.Token);
                    await into.FlushAsync(letGo.Token);

                    Volatile.Write(ref waitingSince, NothingWaiting);
                    Interlocked.Add(ref held, -next.Bytes.Length);

                    if (!fed)
                    {
                        fed = true;
                        locked();
                    }
                }
            }
        }
        catch (Exception gone)
            when (gone is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            Interlocked.Exchange(ref refused, 1);
        }
        finally
        {
            Interlocked.Exchange(ref emptied, 1);

            if (Volatile.Read(ref noMore) is not 0)
            {
                Close();
            }
        }
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref closed, 1) is not 0)
        {
            return;
        }

        try
        {
            into.Close();
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException)
        {
            return;
        }
    }

    private readonly record struct Offered(ReadOnlyMemory<byte> Bytes, long At);
}
