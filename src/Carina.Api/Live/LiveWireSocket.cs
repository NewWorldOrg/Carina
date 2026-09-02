using System.Net.WebSockets;
using System.Threading.Channels;

using Carina.Domain.Streaming;

namespace Carina.Api.Live;

public sealed class LiveWireSocket(
    WebSocket socket,
    LiveWireSettings settings,
    ILiveStartup? startup = null,
    ILiveEnding? ending = null)
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

        Task<LiveDeparture> firstDone = await Task.WhenAny(listening, carrying);

        LiveDeparture departure = stopping.IsCancellationRequested
            ? LiveDeparture.ServerStopping
            : await firstDone;

        if (firstDone == carrying)
        {
            await GoodbyeAsync(departure, listening);
            await leash.CancelAsync();
            await Swallow(listening);
        }
        else
        {
            await leash.CancelAsync();
            await Swallow(carrying);
            await GoodbyeAsync(departure, null);
        }

        return departure;
    }

    public async Task RefuseAsync(LiveJoin refused, CancellationToken cancellationToken)
    {
        LiveRefusalReport report = LiveRefusalReport.Of(refused);

        using CancellationTokenSource patience = new(GoodbyePatience);
        using CancellationTokenSource leash =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, patience.Token);

        try
        {
            await socket.SendAsync(
                new LiveFrame(LiveChannel.Control, LivePts.Start, report.ToPayload()).ToArray(),
                WebSocketMessageType.Binary,
                true,
                leash.Token);

            await socket.CloseOutputAsync(
                LiveRefusalClosures.Status(report.Refusal),
                LiveRefusalClosures.Because(report.Refusal),
                leash.Token);

            byte[] heard = new byte[LiveFrame.HeaderLength + settings.LargestFrameFromAViewer + 1];

            while ((await socket.ReceiveAsync(new ArraySegment<byte>(heard), leash.Token)).MessageType
                   is not WebSocketMessageType.Close)
            {
            }
        }
        catch (Exception gone)
            when (gone is OperationCanceledException or WebSocketException or IOException
                      or ObjectDisposedException or InvalidOperationException)
        {
            socket.Abort();
        }
    }

    private static async Task Swallow(Task running)
    {
        try
        {
            await running;
        }
        catch (Exception gone) when (gone is OperationCanceledException or WebSocketException or IOException)
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
        catch (Exception gone) when (gone is OperationCanceledException or WebSocketException or IOException)
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
                    await SayWhyItEnded(cancellationToken);

                    return LiveDeparture.SourceEnded;
                }

                while (frames.TryRead(out LiveFrame? frame))
                {
                    await SendAsync(frame, cancellationToken);
                }

                waiting = frames.WaitToReadAsync(cancellationToken).AsTask();
            }
        }
        catch (ViewerTooSlow)
        {
            return LiveDeparture.ViewerStoppedReading;
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? LiveDeparture.ViewerLeft
                : LiveDeparture.ViewerStoppedReading;
        }
        catch (Exception gone) when (gone is WebSocketException or IOException)
        {
            return LiveDeparture.ViewerStoppedReading;
        }
        catch (Exception)
        {
            await SayWhyItEndedIfItCan(cancellationToken);

            return LiveDeparture.SourceBroke;
        }
    }

    private async Task SayWhyItEndedIfItCan(CancellationToken cancellationToken)
    {
        try
        {
            await SayWhyItEnded(cancellationToken);
        }
        catch (Exception gone)
            when (gone is OperationCanceledException or WebSocketException or IOException or ViewerTooSlow)
        {
        }
    }

    private async Task SayWhyItEnded(CancellationToken cancellationToken)
    {
        if (ending?.Current is not { } why)
        {
            return;
        }

        await SendAsync(
            new LiveFrame(LiveChannel.Control, LivePts.Start, LiveEndingReport.Of(why).ToPayload()),
            cancellationToken);
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
        cancellationToken.ThrowIfCancellationRequested();

        Task sending = socket.SendAsync(frame.ToArray(), WebSocketMessageType.Binary, true, cancellationToken);

        using CancellationTokenSource ticking = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task waited = Task.Delay(settings.WritePatience, ticking.Token);

        if (await Task.WhenAny(sending, waited) == waited)
        {
            Forget(sending);

            cancellationToken.ThrowIfCancellationRequested();

            throw new ViewerTooSlow();
        }

        await ticking.CancelAsync();
        await sending;
    }

    private static void Forget(Task sending)
        => _ = sending.ContinueWith(
            static settled => settled.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private async Task GoodbyeAsync(LiveDeparture departure, Task<LiveDeparture>? drain)
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

            if (drain is not null)
            {
                await drain.WaitAsync(patience.Token);
            }
        }
        catch (Exception gone)
            when (gone is OperationCanceledException or WebSocketException or IOException
                      or ObjectDisposedException or InvalidOperationException)
        {
            socket.Abort();
        }
    }

    private sealed class ViewerTooSlow : Exception;
}
