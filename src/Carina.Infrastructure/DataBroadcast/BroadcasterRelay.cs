using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

using Carina.Broadcast.DsmCc;
using Carina.Domain.DataBroadcast;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// Talks to a broadcaster's server for a document shown on a live channel. Only a public address on port 80 or 443
/// is connected to, judged on the addresses a name resolves to as the connection is opened, so a name that is
/// pointed inwards between two asks is still refused. Redirects are followed on the same host only and judged
/// again. Each exchange is written to the log by its channel, host, path, method, result, status, sizes and time,
/// never by its query, its bodies or a cookie, and nothing is retried.
/// </summary>
public sealed class BroadcasterRelay : IBroadcasterRelay, IDisposable
{
    public const string Exchanged =
        "Data broadcast relay on channel {Channel}: {Method} {Host}{Path} ended {Result} with status {Status}, "
        + "{Sent} bytes sent and {Received} received in {Elapsed} ms.";

    private const string FormMediaType = "application/x-www-form-urlencoded";

    private static readonly FrozenSet<string> EucJpNames =
        new[] { "euc-jp", "x-euc-jp" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> Utf8Names =
        new[] { "utf-8", "utf8" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly IRelayNetwork network;

    private readonly BroadcasterRelaySettings settings;

    private readonly TimeProvider clock;

    private readonly ILogger<BroadcasterRelay> logger;

    private readonly HttpMessageInvoker client;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> lanes = new(StringComparer.Ordinal);

    public BroadcasterRelay(
        IRelayNetwork network,
        BroadcasterRelaySettings settings,
        TimeProvider clock,
        ILogger<BroadcasterRelay> logger)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.network = network;
        this.settings = settings;
        this.clock = clock;
        this.logger = logger;
        client = new HttpMessageInvoker(
            new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                AutomaticDecompression = DecompressionMethods.None,
                PooledConnectionLifetime = TimeSpan.FromMinutes(1),
                ConnectCallback = ConnectAsync,
                SslOptions = new SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                },
            },
            disposeHandler: true);
    }

    public async Task<RelayedText> TransmitAsync(
        string channel,
        string uri,
        string text,
        string charset,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);
        ArgumentNullException.ThrowIfNull(text);

        long started = clock.GetTimestamp();
        TextCharset? asked = CharsetNamed(charset);
        byte[]? body = asked is not { } named || text.Length == 0 ? null : Encoded(text, named);
        HttpMethod method = body is null ? HttpMethod.Get : HttpMethod.Post;
        Uri? destination = asked is null || body?.Length > settings.LargestSent ? null : RelayDestination.Admit(uri);

        RelayExchange exchange = await RelayAsync(channel, destination, method, body, charset, cancellationToken);
        Write(channel, uri, method, exchange, body?.Length ?? 0, started);

        return exchange.Body is { } received && asked is { } decodedAs
            ? new RelayedText(exchange.Result, exchange.Status, Decoded(received, exchange.Type?.CharSet, decodedAs))
            : new RelayedText(exchange.Result, exchange.Status, null);
    }

    public async Task<RelayedResource> FetchAsync(string channel, string uri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);

        long started = clock.GetTimestamp();
        RelayExchange exchange = await RelayAsync(
            channel,
            RelayDestination.Admit(uri),
            HttpMethod.Get,
            null,
            null,
            cancellationToken);
        Write(channel, uri, HttpMethod.Get, exchange, 0, started);

        return exchange is { Body: { } received, Status: >= 200 and < 300 }
            ? new RelayedResource(exchange.Result, exchange.Status, FetchedResources.Of(exchange.At!, exchange.Type, received))
            : new RelayedResource(exchange.Result, exchange.Status, null);
    }

    public void Dispose()
    {
        client.Dispose();

        foreach (SemaphoreSlim lane in lanes.Values)
        {
            lane.Dispose();
        }
    }

    private static TextCharset? CharsetNamed(string? name)
    {
        if (name is null)
        {
            return null;
        }

        if (EucJpNames.Contains(name))
        {
            return TextCharset.EucJp;
        }

        return Utf8Names.Contains(name) ? TextCharset.Utf8 : null;
    }

    private static byte[] Encoded(string text, TextCharset charset)
        => charset is TextCharset.EucJp ? EucJpText.Encode(text) : Encoding.UTF8.GetBytes(text);

    private static string Decoded(byte[] received, string? said, TextCharset asked)
    {
        TextCharset charset = CharsetNamed(said?.Trim('"')) ?? asked;

        return charset is TextCharset.EucJp ? EucJpText.Decode(received) : Encoding.UTF8.GetString(received);
    }

    private static RelayResult? Classified(Exception failure)
    {
        for (Exception? at = failure; at is not null; at = at.InnerException)
        {
            RelayResult? named = at switch
            {
                RelayFailureException relayed => relayed.Result,
                AuthenticationException => RelayResult.SecureConnectionFailed,
                _ => null,
            };

            if (named is not null)
            {
                return named;
            }
        }

        return failure is HttpRequestException or IOException or SocketException ? RelayResult.Disconnected : null;
    }

    private static Uri? Redirected(Uri at, HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            || response.Headers.Location is not { } location)
        {
            return null;
        }

        Uri next = location.IsAbsoluteUri ? location : new Uri(at, location);

        return RelayDestination.Admits(next) && string.Equals(next.IdnHost, at.IdnHost, StringComparison.OrdinalIgnoreCase)
            ? next
            : null;
    }

    private static bool KeepsTheMethod(HttpStatusCode status)
        => status is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private async Task<RelayExchange> RelayAsync(
        string channel,
        Uri? destination,
        HttpMethod method,
        byte[]? body,
        string? charset,
        CancellationToken cancellationToken)
    {
        if (destination is null)
        {
            return RelayExchange.Failed(RelayResult.RefusedOrTooLarge);
        }

        SemaphoreSlim lane = lanes.GetOrAdd(channel, _ => new SemaphoreSlim(settings.AtOnce, settings.AtOnce));
        using CancellationTokenSource deadline = new(settings.Time, clock);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            await lane.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RelayExchange.Failed(RelayResult.TimedOut);
        }

        try
        {
            return await ExchangeAsync(destination, method, body, charset, linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RelayExchange.Failed(RelayResult.TimedOut);
        }
        catch (Exception failure) when (Classified(failure) is { } result)
        {
            return RelayExchange.Failed(result);
        }
        finally
        {
            lane.Release();
        }
    }

    private async Task<RelayExchange> ExchangeAsync(
        Uri destination,
        HttpMethod method,
        byte[]? body,
        string? charset,
        CancellationToken cancellationToken)
    {
        Uri at = destination;

        for (int hop = 0; ; hop++)
        {
            using HttpRequestMessage request = Request(at, method, body, charset);
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

            if (hop < settings.Redirects && Redirected(at, response) is { } next)
            {
                at = next;
                method = KeepsTheMethod(response.StatusCode) ? method : HttpMethod.Get;
                body = method == HttpMethod.Get ? null : body;

                continue;
            }

            byte[] received = await ReadAsync(response.Content, cancellationToken);

            return new RelayExchange(RelayResult.Answered, (int)response.StatusCode, received, response.Content.Headers.ContentType, at);
        }
    }

    private HttpRequestMessage Request(Uri at, HttpMethod method, byte[]? body, string? charset)
    {
        HttpRequestMessage request = new(method, at)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        request.Headers.UserAgent.ParseAdd(settings.UserAgent);

        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(FormMediaType) { CharSet = charset };
        }

        return request;
    }

    private async Task<byte[]> ReadAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > settings.LargestReceived)
        {
            throw new RelayFailureException(RelayResult.RefusedOrTooLarge, "The server answered more than the relay takes back.");
        }

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream kept = new();
        byte[] buffer = new byte[16 * 1024];
        int read;

        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (kept.Length + read > settings.LargestReceived)
            {
                throw new RelayFailureException(RelayResult.RefusedOrTooLarge, "The server answered more than the relay takes back.");
            }

            kept.Write(buffer, 0, read);
        }

        return kept.ToArray();
    }

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        DnsEndPoint endpoint = context.DnsEndPoint;
        IReadOnlyList<IPAddress> addresses = await AddressesOfAsync(endpoint.Host, cancellationToken);

        if (!addresses.All(RelayAddresses.IsPublic))
        {
            throw new RelayFailureException(RelayResult.RefusedOrTooLarge, "The name resolves to an address the relay does not connect to.");
        }

        SocketException? last = null;

        foreach (IPAddress address in addresses)
        {
            try
            {
                return await network.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
            }
            catch (SocketException failure)
            {
                last = failure;
            }
        }

        throw last ?? new SocketException((int)SocketError.HostUnreachable);
    }

    private async Task<IReadOnlyList<IPAddress>> AddressesOfAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? written))
        {
            return [written];
        }

        IReadOnlyList<IPAddress> resolved;

        try
        {
            resolved = await network.ResolveAsync(host, cancellationToken);
        }
        catch (Exception failure) when (failure is SocketException or ArgumentException)
        {
            throw new RelayFailureException(RelayResult.NameNotResolved, "The name of the server could not be resolved.", failure);
        }

        return resolved.Count > 0
            ? resolved
            : throw new RelayFailureException(RelayResult.NameNotResolved, "The name of the server resolves to no address.");
    }

    private void Write(string channel, string uri, HttpMethod method, RelayExchange exchange, int sent, long started)
    {
        Uri? asked = Uri.TryCreate(uri, UriKind.Absolute, out Uri? read) ? read : null;

        logger.LogInformation(
            Exchanged,
            channel,
            method.Method,
            asked?.IdnHost ?? string.Empty,
            asked?.AbsolutePath ?? string.Empty,
            (int)exchange.Result,
            exchange.Status,
            sent,
            exchange.Body?.Length ?? 0,
            (long)clock.GetElapsedTime(started).TotalMilliseconds);
    }

    private sealed record RelayExchange(RelayResult Result, int? Status, byte[]? Body, MediaTypeHeaderValue? Type, Uri? At)
    {
        public static RelayExchange Failed(RelayResult result) => new(result, null, null, null, null);
    }

    private enum TextCharset
    {
        EucJp,

        Utf8,
    }
}

public sealed class RelayFailureException : Exception
{
    public RelayFailureException()
    {
    }

    public RelayFailureException(string message)
        : base(message)
    {
    }

    public RelayFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public RelayFailureException(RelayResult result, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Result = result;
    }

    public RelayResult Result { get; } = RelayResult.Disconnected;
}
