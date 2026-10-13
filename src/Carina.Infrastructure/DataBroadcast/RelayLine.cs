using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

using Carina.Domain.DataBroadcast;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// How far one kind of relayed exchange goes: how many a channel has under way at once, how long one may take
/// including the wait for its turn, the most it takes back, and how many redirects it follows.
/// </summary>
public sealed record RelayLimits(int AtOnce, TimeSpan Time, int LargestReceived, int Redirects);

/// <summary>
/// One exchange to make: the lane it waits its turn in, its limits, where it goes (nothing where the address was
/// already refused), how, with what body in which character set, and which redirect it follows from where.
/// </summary>
public sealed record RelayAsk(
    string Lane,
    RelayLimits Limits,
    Uri? Destination,
    HttpMethod Method,
    byte[]? Body,
    string? Charset,
    Func<Uri, Uri, bool> Follows);

/// <summary>
/// What one exchange came to: the result, and where the server answered, its status, the body, the type it named
/// and the address it was finally asked at.
/// </summary>
public sealed record RelayExchange(RelayResult Result, int? Status, byte[]? Body, MediaTypeHeaderValue? Type, Uri? At)
{
    public static RelayExchange Failed(RelayResult result) => new(result, null, null, null, null);
}

/// <summary>
/// The connection both relays to broadcasters' servers go out on. Only a public address on port 80 or 443 is
/// connected to, and never this installation: not a host it is reached by, whatever the address writes after it,
/// nor an address this machine answers at. Addresses are judged on what a name resolves to as the connection is
/// opened, so a name that is pointed inwards between two asks is still refused. Redirects are followed only where
/// the asker says so and judged again. Nothing is retried, no cookie is kept, and the relay introduces itself by the
/// one name it is given.
/// </summary>
public sealed class RelayLine : IDisposable
{
    private const string FormMediaType = "application/x-www-form-urlencoded";

    private readonly IRelayNetwork network;

    private readonly OwnHost own;

    private readonly TimeProvider clock;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> lanes = new(StringComparer.Ordinal);

    public RelayLine(IRelayNetwork network, OwnHost own, string userAgent, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(own);
        ArgumentException.ThrowIfNullOrEmpty(userAgent);
        ArgumentNullException.ThrowIfNull(clock);

        this.network = network;
        this.own = own;
        this.clock = clock;
        UserAgent = userAgent;
        Client = new HttpMessageInvoker(
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

    public HttpMessageInvoker Client { get; }

    public string UserAgent { get; }

    public static RelayResult? Classified(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

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

        return failure is HttpRequestException or IOException or SocketException or System.Net.WebSockets.WebSocketException
            ? RelayResult.Disconnected
            : null;
    }

    public async Task<RelayExchange> RelayAsync(RelayAsk ask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ask);

        if (ask.Destination is null || own.IsNamedBy(ask.Destination))
        {
            return RelayExchange.Failed(RelayResult.RefusedOrTooLarge);
        }

        SemaphoreSlim lane = lanes.GetOrAdd(ask.Lane, _ => new SemaphoreSlim(ask.Limits.AtOnce, ask.Limits.AtOnce));
        using CancellationTokenSource deadline = new(ask.Limits.Time, clock);
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
            return await ExchangeAsync(ask, ask.Destination, linked.Token);
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

    public void Dispose()
    {
        Client.Dispose();

        foreach (SemaphoreSlim lane in lanes.Values)
        {
            lane.Dispose();
        }
    }

    private Uri? Redirected(Uri at, HttpResponseMessage response, Func<Uri, Uri, bool> follows)
    {
        if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            || response.Headers.Location is not { } location)
        {
            return null;
        }

        Uri next = location.IsAbsoluteUri ? location : new Uri(at, location);

        return RelayDestination.Admits(next) && !own.IsNamedBy(next) && follows(at, next) ? next : null;
    }

    private static bool KeepsTheMethod(HttpStatusCode status)
        => status is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private async Task<RelayExchange> ExchangeAsync(RelayAsk ask, Uri destination, CancellationToken cancellationToken)
    {
        Uri at = destination;
        HttpMethod method = ask.Method;
        byte[]? body = ask.Body;

        for (int hop = 0; ; hop++)
        {
            using HttpRequestMessage request = Request(at, method, body, ask.Charset);
            using HttpResponseMessage response = await Client.SendAsync(request, cancellationToken);

            if (hop < ask.Limits.Redirects && Redirected(at, response, ask.Follows) is { } next)
            {
                at = next;
                method = KeepsTheMethod(response.StatusCode) ? method : HttpMethod.Get;
                body = method == HttpMethod.Get ? null : body;

                continue;
            }

            byte[] received = await ReadAsync(response.Content, ask.Limits.LargestReceived, cancellationToken);

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

        request.Headers.UserAgent.ParseAdd(UserAgent);

        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(FormMediaType) { CharSet = charset };
        }

        return request;
    }

    private static async Task<byte[]> ReadAsync(HttpContent content, int largest, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > largest)
        {
            throw new RelayFailureException(RelayResult.RefusedOrTooLarge, "The server answered more than the relay takes back.");
        }

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream kept = new();
        byte[] buffer = new byte[16 * 1024];
        int read;

        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (kept.Length + read > largest)
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

        if (own.IsNamedBy(endpoint.Host))
        {
            throw new RelayFailureException(RelayResult.RefusedOrTooLarge, "The name is one this installation is reached by.");
        }

        IReadOnlyList<IPAddress> addresses = await AddressesOfAsync(endpoint.Host, cancellationToken);
        IPAddress[] machine = [.. network.OwnAddresses().Select(RelayAddresses.Unmapped)];

        if (!addresses.All(address => ConnectsTo(RelayAddresses.Unmapped(address), machine)))
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

    private bool ConnectsTo(IPAddress address, IPAddress[] machine)
        => RelayAddresses.IsPublic(address) && !own.IsAt(address) && !machine.Contains(address);

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
