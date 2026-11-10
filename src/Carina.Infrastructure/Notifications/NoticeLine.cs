using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Net;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// What sending one notice came to: no failure where the other end took it, otherwise why not, whether the address
/// is one the other end no longer knows, and how long it asked to be left alone.
/// </summary>
public sealed record NoticeAnswer(NoticeFailure? Failure, bool AddressGone, TimeSpan? Asked)
{
    public static NoticeAnswer Taken { get; } = new(null, false, null);

    public bool IsTaken => Failure is null;

    public static NoticeAnswer Failed(NoticeFailure failure) => new(failure, false, null);
}

/// <summary>
/// The connection notices go out to webhooks on. Only a public address is connected to, judged on what the name
/// resolves to as the connection is opened. No redirect is followed, no cookie is kept, no proxy is used, and what
/// the other end writes back is never read.
/// </summary>
public sealed class NoticeLine : IDisposable
{
    public static readonly TimeSpan UsualDeadline = TimeSpan.FromSeconds(10);

    private const string ResetAfter = "X-RateLimit-Reset-After";

    private static readonly MediaTypeHeaderValue Json = new("application/json");

    private readonly INoticeNetwork network;

    private readonly string userAgent;

    private readonly TimeProvider clock;

    private readonly TimeSpan deadline;

    private readonly HttpMessageInvoker client;

    public NoticeLine(INoticeNetwork network, string userAgent, TimeProvider clock, TimeSpan deadline)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentException.ThrowIfNullOrEmpty(userAgent);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(deadline, TimeSpan.Zero);

        this.network = network;
        this.userAgent = userAgent;
        this.clock = clock;
        this.deadline = deadline;
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
                    RemoteCertificateValidationCallback = (_, certificate, _, errors) => network.Trusts(certificate, errors),
                },
            },
            disposeHandler: true);
    }

    public async Task<NoticeAnswer> SendAsync(WebhookUrl webhook, byte[] payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webhook);
        ArgumentNullException.ThrowIfNull(payload);

        using CancellationTokenSource running = new(deadline, clock);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, running.Token);

        try
        {
            using HttpRequestMessage request = Request(new Uri(webhook.InTheClear), payload);
            using HttpResponseMessage response = await client.SendAsync(request, linked.Token);

            return Answered(response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return NoticeAnswer.Failed(NoticeFailure.TimedOut);
        }
        catch (Exception failure) when (Named(failure) is { } named)
        {
            return NoticeAnswer.Failed(named);
        }
    }

    public void Dispose() => client.Dispose();

    private static NoticeFailure? Named(Exception failure)
    {
        for (Exception? at = failure; at is not null; at = at.InnerException)
        {
            if (at is AuthenticationException)
            {
                return NoticeFailure.TlsFailed;
            }
        }

        return failure is HttpRequestException or IOException or SocketException ? NoticeFailure.Unreachable : null;
    }

    private NoticeAnswer Answered(HttpResponseMessage response)
        => (int)response.StatusCode switch
        {
            >= 200 and < 300 => NoticeAnswer.Taken,
            429 => new NoticeAnswer(NoticeFailure.RateLimited, false, Asked(response)),
            401 or 404 => new NoticeAnswer(NoticeFailure.Rejected, true, null),
            >= 500 => NoticeAnswer.Failed(NoticeFailure.ServerFailed),
            _ => NoticeAnswer.Failed(NoticeFailure.Rejected),
        };

    private TimeSpan? Asked(HttpResponseMessage response)
    {
        TimeSpan? asked = response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - clock.GetUtcNow(),
            _ => ResetAfterOf(response),
        };

        if (asked is not { } wait || wait < TimeSpan.Zero)
        {
            return null;
        }

        return wait > NoticeRetry.LongestWait ? NoticeRetry.LongestWait : wait;
    }

    private static TimeSpan? ResetAfterOf(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(ResetAfter, out IEnumerable<string>? written)
            || !double.TryParse(written.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            || !double.IsFinite(seconds)
            || seconds < 0)
        {
            return null;
        }

        return TimeSpan.FromSeconds(Math.Min(seconds, NoticeRetry.LongestWait.TotalSeconds));
    }

    private HttpRequestMessage Request(Uri at, byte[] payload)
    {
        HttpRequestMessage request = new(HttpMethod.Post, at)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new ByteArrayContent(payload),
        };

        request.Headers.UserAgent.ParseAdd(userAgent);
        request.Content.Headers.ContentType = Json;

        return request;
    }

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        DnsEndPoint endpoint = context.DnsEndPoint;
        IReadOnlyList<IPAddress> addresses = await AddressesOfAsync(endpoint.Host, cancellationToken);

        if (addresses.Count is 0 || !addresses.All(address => PublicAddresses.IsPublic(PublicAddresses.Unmapped(address))))
        {
            throw new SocketException((int)SocketError.NetworkUnreachable);
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

        try
        {
            return await network.ResolveAsync(host, cancellationToken);
        }
        catch (ArgumentException failure)
        {
            throw new SocketException((int)SocketError.HostNotFound, failure.Message);
        }
    }
}
