using System.Net.WebSockets;
using System.Threading.Channels;

using Carina.Domain.Streaming;

namespace Carina.Api.Live;

public sealed class LiveWireSocket(WebSocket socket, LiveWireSettings settings, ILiveStartup? startup = null)
{
    private static readonly TimeSpan GoodbyePatience = TimeSpan.FromSeconds(2);

    public async Task<LiveDeparture> CarryAsync(
        ChannelReader<LiveFrame> frames,
        CancellationToken stopping,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frames);

        using CancellationTokenSource leash =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping);

        Task<LiveDeparture> listening = ListenAsync(leash.Token);
        Task<LiveDeparture> carrying = CarryOnAsync(frames, leash.Token);

        Task<LiveDeparture> first = await Task.WhenAny(listening, carrying);

        await leash.CancelAsync();

        LiveDeparture departure = Told(stopping, cancellationToken) ?? await first;

        await Settled(listening, carrying);
        await GoodbyeAsync(departure);

        return departure;
    }

    private static LiveDeparture? Told(CancellationToken stopping, CancellationToken cancellationToken)
    {
        if (stopping.IsCancellationRequested)
        {
            return LiveDeparture.ServerStopping;
        }

        return cancellationToken.IsCancellationRequested ? LiveDeparture.ViewerLeft : null;
    }

    private static async Task Settled(params Task[] running)
    {
        try
        {
            await Task.WhenAll(running);
        }
        catch (Exception ending) when (ending is OperationCanceledException or WebSocketException or IOException)
        {
        }
    }

    private async Task<LiveDeparture> ListenAsync(CancellationToken cancellationToken)
    {
        byte[] heard = new byte[LiveFrame.HeaderLength + settings.LargestFrameFromAViewer + 1];

        try
        {
            while (true)
            {
                WebSocketReceiveResult said = await socket.ReceiveAsync(new ArraySegment<byte>(heard), cancellationToken);

                if (Understood(said, heard) is { } departure)
                {
                    return departure;
                }
            }
        }
        catch (Exception ending) when (ending is OperationCanceledException or WebSocketException or IOException)
        {
            return LiveDeparture.ViewerLeft;
        }
    }

    private LiveDeparture? Understood(WebSocketReceiveResult said, ReadOnlySpan<byte> heard)
    {
        if (said.MessageType is WebSocketMessageType.Close)
        {
            return LiveDeparture.ViewerLeft;
        }

        if (!said.EndOfMessage || said.Count > LiveFrame.HeaderLength + settings.LargestFrameFromAViewer)
        {
            return LiveDeparture.SaidMoreThanTheWireTakes;
        }

        if (said.MessageType is not WebSocketMessageType.Binary)
        {
            return LiveDeparture.SaidSomethingUnknown;
        }

        if (LiveFrame.Read(heard[..said.Count]).Frame is not { Channel: LiveChannel.Control } frame)
        {
            return LiveDeparture.SaidSomethingUnknown;
        }

        return LiveControls.SaidByAViewer(frame.Payload.Span) switch
        {
            LiveControl.Leaving => LiveDeparture.ViewerLeft,
            LiveControl.Pong => null,
            _ => LiveDeparture.SaidSomethingUnknown,
        };
    }

    private async Task<LiveDeparture> CarryOnAsync(
        ChannelReader<LiveFrame> frames,
        CancellationToken cancellationToken)
    {
        try
        {
            await SayWhereWeAre(cancellationToken);

            Task<bool> waiting = frames.WaitToReadAsync(cancellationToken).AsTask();

            while (true)
            {
                if (!await ReadableWithin(waiting, settings.BetweenPings, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!await SayWhereWeAre(cancellationToken))
                    {
                        await SendAsync(LiveControls.Frame(LiveControl.Ping), cancellationToken);
                    }

                    continue;
                }

                if (!await waiting)
                {
                    return LiveDeparture.SourceEnded;
                }

                while (frames.TryRead(out LiveFrame? frame))
                {
                    await SendAsync(frame, cancellationToken);
                }

                waiting = frames.WaitToReadAsync(cancellationToken).AsTask();
            }
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? LiveDeparture.ViewerLeft
                : LiveDeparture.ViewerStoppedReading;
        }
        catch (Exception ending) when (ending is WebSocketException or IOException)
        {
            return LiveDeparture.ViewerStoppedReading;
        }
        catch (Exception)
        {
            return LiveDeparture.SourceBroke;
        }
    }

    private async Task<bool> SayWhereWeAre(CancellationToken cancellationToken)
    {
        if (startup?.Current is not { InProgress: true } where)
        {
            return false;
        }

        await SendAsync(
            new LiveFrame(LiveChannel.Control, LivePts.Start, where.ToProgressPayload()),
            cancellationToken);

        return true;
    }

    private static async Task<bool> ReadableWithin(
        Task<bool> waiting,
        TimeSpan quiet,
        CancellationToken cancellationToken)
    {
        using var tick = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task quiets = Task.Delay(quiet, tick.Token);
        bool readable = await Task.WhenAny(waiting, quiets) != quiets;

        await tick.CancelAsync();

        return readable;
    }

    private async Task SendAsync(LiveFrame frame, CancellationToken cancellationToken)
    {
        using CancellationTokenSource patience =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        patience.CancelAfter(settings.WritePatience);

        cancellationToken.ThrowIfCancellationRequested();

        await socket.SendAsync(frame.ToArray(), WebSocketMessageType.Binary, true, patience.Token);
    }

    private async Task GoodbyeAsync(LiveDeparture departure)
    {
        if (departure is LiveDeparture.ViewerStoppedReading)
        {
            socket.Abort();

            return;
        }

        using var patience = new CancellationTokenSource(GoodbyePatience);

        try
        {
            await socket.CloseOutputAsync(
                LiveDepartures.Status(departure),
                LiveDepartures.Because(departure),
                patience.Token);
        }
        catch (Exception gone)
            when (gone is OperationCanceledException or WebSocketException or IOException
                      or ObjectDisposedException or InvalidOperationException)
        {
            socket.Abort();
        }
    }
}
