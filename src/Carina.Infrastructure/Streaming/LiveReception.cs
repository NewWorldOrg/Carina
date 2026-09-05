using System.Buffers;

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
        Action<LiveReception> forget)
    {
        this.network = network;
        this.service = service;
        this.supply = supply;
        this.settings = settings;
        this.forget = forget;
    }

    internal NetworkId Network => network;

    internal ServiceId Service => service;

    internal Task Life { get; private set; } = Task.CompletedTask;

    internal LiveSupplyEnding? Ending => stream?.Ending;

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

    internal LiveSeat Take(
        Stream into,
        Action locked,
        Action<LiveSupplyEnding> ended)
    {
        LiveSeat seat = new(into, locked, ended, settings.LongestWaitToBeFed);

        lock (gate)
        {
            seats.Add(seat);
        }

        return seat;
    }

    internal void Drop(LiveSeat seat)
    {
        lock (gate)
        {
            seats.Remove(seat);
        }
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

        Life = CarryAsync(bytes);

        return opened;
    }

    private async Task CarryAsync(ILiveTransportStream from)
    {
        byte[] mouthful = ArrayPool<byte>.Shared.Rent(LiveFeed.Mouthful);

        try
        {
            int read;

            while ((read = await from.Bytes.ReadAsync(mouthful, stopping.Token)) > 0)
            {
                await FeedAsync(mouthful.AsMemory(0, read));
            }

            EndEverySeat(from.Ending ?? LiveSupplyEnding.Of(
                LiveSupplyEnd.DriverLost,
                "the transport stream ended and the supply did not say why."));
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or OperationCanceledException)
        {
            EndEverySeat(from.Ending);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(mouthful);
            await from.DisposeAsync();
        }
    }

    private async Task FeedAsync(ReadOnlyMemory<byte> mouthful)
    {
        foreach (LiveSeat seat in Seated())
        {
            if (!await seat.OfferAsync(mouthful, stopping.Token))
            {
                // One transcoder that will not take bytes is not a reason to stop the others.
                Drop(seat);
                seat.NoMore();
            }
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
/// One transcoder's place at a reading of the channel.
/// </summary>
internal sealed class LiveSeat(
    Stream into,
    Action locked,
    Action<LiveSupplyEnding> ended,
    TimeSpan patience)
{
    private bool fed;

    internal async Task<bool> OfferAsync(ReadOnlyMemory<byte> mouthful, CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource deadline = new(patience);
            using CancellationTokenSource leash =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

            await into.WriteAsync(mouthful, leash.Token);
            await into.FlushAsync(leash.Token);

            if (!fed)
            {
                fed = true;
                locked();
            }

            return true;
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return false;
        }
    }

    internal void Ended(LiveSupplyEnding why) => ended(why);

    internal void NoMore()
    {
        try
        {
            into.Close();
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException)
        {
            return;
        }
    }
}
