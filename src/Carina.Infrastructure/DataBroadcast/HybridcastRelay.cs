using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;

using Carina.Broadcast.Hybridcast;
using Carina.Domain.DataBroadcast;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// Talks to a broadcaster's server for a Hybridcast application started from a live channel, over the
/// <see cref="RelayLine"/> that connects to public addresses only and never to this installation. It reads the AIT a
/// document names, and carries the application's documents, resources, posts and WebSockets only inside the
/// application's boundary; a redirect is followed only where it stays inside too. A post carries no more than a data
/// broadcast document sends, with the media type the application named and nothing else of its request. Each exchange is written to the log by its channel, host, path, method, result,
/// status, sizes and time, never by its query, its bodies or a cookie, and nothing is retried.
/// </summary>
public sealed class HybridcastRelay : IHybridcastRelay, IDisposable
{
    public const string Exchanged =
        "Hybridcast relay on channel {Channel}: {Method} {Host}{Path} ended {Result} with status {Status}, "
        + "{Sent} bytes sent and {Received} received in {Elapsed} ms.";

    private const string Socket = "WEBSOCKET";

    private static readonly TimeSpan Closing = TimeSpan.FromSeconds(5);

    private readonly HybridcastRelaySettings settings;

    private readonly OwnHost own;

    private readonly TimeProvider clock;

    private readonly ILogger<HybridcastRelay> logger;

    private readonly RelayLine line;

    private readonly RelayLimits aits;

    private readonly RelayLimits resources;

    private readonly ConcurrentDictionary<string, int> sockets = new(StringComparer.Ordinal);

    public HybridcastRelay(
        IRelayNetwork network,
        OwnHost own,
        HybridcastRelaySettings settings,
        TimeProvider clock,
        ILogger<HybridcastRelay> logger)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.settings = settings;
        this.own = own;
        this.clock = clock;
        this.logger = logger;
        line = new RelayLine(network, own, settings.UserAgent, clock);
        aits = new RelayLimits(settings.AtOnce, settings.Time, settings.LargestAit, settings.Redirects);
        resources = new RelayLimits(settings.AtOnce, settings.Time, settings.LargestReceived, settings.Redirects);
    }

    public async Task<RelayedAit> ReadAitAsync(string channel, string uri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);

        long started = clock.GetTimestamp();
        Uri? destination = RelayDestination.Admit(uri);
        RelayExchange exchange = await line.RelayAsync(
            new RelayAsk(Lane(channel), aits, destination, HttpMethod.Get, null, null, OnTheSameHost),
            cancellationToken);
        Write(channel, destination, HttpMethod.Get.Method, exchange.Result, exchange.Status, 0, exchange.Body?.Length ?? 0, started);

        return exchange is { Body: { } received, Status: >= 200 and < 300 }
            ? new RelayedAit(exchange.Result, exchange.Status, XmlAit.Read(received)?.Select(Application).ToList())
            : new RelayedAit(exchange.Result, exchange.Status, null);
    }

    public Task<RelayedBody> FetchAsync(string channel, Uri address, HybridcastBoundary boundary, CancellationToken cancellationToken)
        => ExchangeAsync(channel, address, boundary, HttpMethod.Get, null, true, cancellationToken);

    public Task<RelayedBody> PostAsync(string channel, Uri address, HybridcastBoundary boundary, PostedBody posted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(posted);

        bool sendable = posted.Bytes.Length <= settings.LargestSent
                        && (posted.ContentType is null || MediaTypeHeaderValue.TryParse(posted.ContentType, out _));

        return ExchangeAsync(channel, address, boundary, HttpMethod.Post, posted, sendable, cancellationToken);
    }

    public async Task<RelayedSocket> OpenSocketAsync(
        string channel,
        Uri address,
        HybridcastBoundary boundary,
        IReadOnlyList<string> subProtocols,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(subProtocols);

        long started = clock.GetTimestamp();

        if (!RelayDestination.AdmitsSocket(address) || own.IsNamedBy(address) || !boundary.Admits(address) || !TookASocket(channel))
        {
            Write(channel, address, Socket, RelayResult.RefusedOrTooLarge, null, 0, 0, started);

            return RelayedSocket.Failed(RelayResult.RefusedOrTooLarge);
        }

        RelayedSocket? opened = null;

        try
        {
            opened = await ConnectAsync(channel, address, subProtocols, started, cancellationToken);
        }
        finally
        {
            if (opened?.Socket is null)
            {
                GaveASocketBack(channel);
            }
        }

        if (opened.Socket is null)
        {
            Write(channel, address, Socket, opened.Result, opened.HttpStatus, 0, 0, started);
        }

        return opened;
    }

    public void Dispose() => line.Dispose();

    private static string Lane(string channel) => $"{channel} hybridcast";

    private async Task<RelayedBody> ExchangeAsync(
        string channel,
        Uri address,
        HybridcastBoundary boundary,
        HttpMethod method,
        PostedBody? posted,
        bool sendable,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(boundary);

        long started = clock.GetTimestamp();
        Uri? destination = sendable && RelayDestination.Admits(address) && boundary.Admits(address) ? address : null;
        byte[]? body = posted?.Bytes.ToArray();
        RelayExchange exchange = await line.RelayAsync(
            new RelayAsk(Lane(channel), resources, destination, method, body, posted?.ContentType, (_, next) => boundary.Admits(next)),
            cancellationToken);
        Write(channel, address, method.Method, exchange.Result, exchange.Status, body?.Length ?? 0, exchange.Body?.Length ?? 0, started);

        return exchange.Body is { } received
            ? new RelayedBody(exchange.Result, exchange.Status, exchange.Type?.ToString(), received, exchange.At)
            : RelayedBody.Failed(exchange.Result);
    }

    private static bool OnTheSameHost(Uri at, Uri next) => string.Equals(next.IdnHost, at.IdnHost, StringComparison.OrdinalIgnoreCase);

    private static HybridcastApplication Application(AitApplication read)
        => new(
            read.OrgId,
            read.AppId,
            (HybridcastControlCode)(int)read.ControlCode,
            new Uri(read.EntryUrl),
            read.Profile,
            [.. read.Boundary.Select(pair => new HybridcastPermission(pair.Bitmap, pair.ManagedUrls))]);

    private async Task<RelayedSocket> ConnectAsync(
        string channel,
        Uri address,
        IReadOnlyList<string> subProtocols,
        long started,
        CancellationToken cancellationToken)
    {
        ClientWebSocket upstream = new();
        bool handed = false;
        using CancellationTokenSource deadline = new(settings.Time, clock);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            Prepare(upstream.Options, subProtocols);
            await upstream.ConnectAsync(address, line.Client, linked.Token);
            handed = true;

            return new RelayedSocket(RelayResult.Answered, null, new CarriedSocket(this, channel, address, upstream, started));
        }
        catch (Exception failure) when (Refused(failure, upstream, cancellationToken) is { } refused)
        {
            return refused;
        }
        finally
        {
            if (!handed)
            {
                upstream.Dispose();
            }
        }
    }

    private void Prepare(ClientWebSocketOptions options, IReadOnlyList<string> subProtocols)
    {
        options.HttpVersion = HttpVersion.Version11;
        options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        options.CollectHttpResponseDetails = true;
        options.SetRequestHeader("User-Agent", line.UserAgent);

        foreach (string subProtocol in subProtocols)
        {
            options.AddSubProtocol(subProtocol);
        }
    }

    private static RelayedSocket? Refused(Exception failure, ClientWebSocket upstream, CancellationToken cancellationToken)
    {
        if (failure is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return RelayedSocket.Failed(RelayResult.TimedOut);
        }

        if (upstream.HttpStatusCode is not 0 and not HttpStatusCode.SwitchingProtocols)
        {
            return new RelayedSocket(RelayResult.Answered, (int)upstream.HttpStatusCode, null);
        }

        return RelayLine.Classified(failure) is { } result ? RelayedSocket.Failed(result) : null;
    }

    private bool TookASocket(string channel)
    {
        bool took = false;

        sockets.AddOrUpdate(
            channel,
            _ =>
            {
                took = settings.SocketsAtOnce > 0;

                return took ? 1 : 0;
            },
            (_, open) =>
            {
                took = open < settings.SocketsAtOnce;

                return took ? open + 1 : open;
            });

        return took;
    }

    private void GaveASocketBack(string channel) => sockets.AddOrUpdate(channel, 0, (_, open) => Math.Max(0, open - 1));

    private void Write(string channel, Uri? address, string method, RelayResult result, int? status, long sent, long received, long started)
        => logger.LogInformation(
            Exchanged,
            channel,
            method,
            address?.IdnHost ?? string.Empty,
            address?.AbsolutePath ?? string.Empty,
            (int)result,
            status,
            sent,
            received,
            (long)clock.GetElapsedTime(started).TotalMilliseconds);

    private sealed class CarriedSocket(HybridcastRelay relay, string channel, Uri address, ClientWebSocket upstream, long started)
        : IHybridcastSocket
    {
        private int given;

        public string? SubProtocol => upstream.SubProtocol;

        public async Task CarryAsync(WebSocket viewer, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(viewer);

            Task<long> sent = PumpAsync(viewer, upstream, cancellationToken);
            Task<long> received = PumpAsync(upstream, viewer, cancellationToken);
            RelayResult result = await CarriedAsync(viewer, sent, received, cancellationToken);

            relay.Write(channel, address, Socket, result, null, Settled(sent), Settled(received), started);
        }

        public ValueTask DisposeAsync()
        {
            upstream.Dispose();

            if (Interlocked.Exchange(ref given, 1) == 0)
            {
                relay.GaveASocketBack(channel);
            }

            return ValueTask.CompletedTask;
        }

        private static long Settled(Task<long> pump) => pump.IsCompletedSuccessfully ? pump.Result : 0;

        private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string? description, CancellationToken cancellationToken)
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseOutputAsync(status, status is WebSocketCloseStatus.Empty ? null : description, cancellationToken);
            }
        }

        private async Task<RelayResult> CarriedAsync(WebSocket viewer, Task<long> sent, Task<long> received, CancellationToken cancellationToken)
        {
            Task<long>[] pumps = [sent, received];

            try
            {
                await await Task.WhenAny(pumps);
                await Task.WhenAll(pumps).WaitAsync(Closing, relay.clock, cancellationToken);

                return RelayResult.Answered;
            }
            catch (Exception failure) when (failure is WebSocketException or OperationCanceledException or TimeoutException or IOException)
            {
                viewer.Abort();
                upstream.Abort();
                await Task.WhenAll(pumps.Select(pump => pump.ContinueWith(_ => { }, TaskScheduler.Default)));

                return RelayResult.Disconnected;
            }
        }

        private async Task<long> PumpAsync(WebSocket from, WebSocket to, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[16 * 1024];
            long carried = 0;
            long message = 0;

            while (true)
            {
                WebSocketReceiveResult got = await from.ReceiveAsync(buffer, cancellationToken);

                if (got.MessageType is WebSocketMessageType.Close)
                {
                    await CloseAsync(to, from.CloseStatus ?? WebSocketCloseStatus.Empty, from.CloseStatusDescription, cancellationToken);

                    return carried;
                }

                message += got.Count;

                if (message > relay.settings.LargestMessage)
                {
                    await CloseAsync(from, WebSocketCloseStatus.MessageTooBig, null, cancellationToken);
                    await CloseAsync(to, WebSocketCloseStatus.MessageTooBig, null, cancellationToken);

                    return carried;
                }

                await to.SendAsync(buffer.AsMemory(0, got.Count), got.MessageType, got.EndOfMessage, cancellationToken);
                carried += got.Count;
                message = got.EndOfMessage ? 0 : message;
            }
        }
    }
}
