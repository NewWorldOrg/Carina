using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Tests.DataBroadcast;

namespace Carina.Infrastructure.Tests.Notifications;

/// <summary>
/// The network as the line of notices sees it in a test: names resolve to the addresses written here, an unknown name
/// does not resolve, every connection reaches the stand-in on the loopback interface, and the one certificate written
/// as trusted is the only one taken.
/// </summary>
internal sealed class PointedNoticeNetwork(int port) : INoticeNetwork
{
    public Dictionary<string, IPAddress[]> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentQueue<IPEndPoint> Connected { get; } = new();

    public bool Refuses { get; set; }

    public X509Certificate2? Trusted { get; set; }

    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        => Names.TryGetValue(host, out IPAddress[]? addresses)
            ? Task.FromResult<IReadOnlyList<IPAddress>>(addresses)
            : Task.FromException<IReadOnlyList<IPAddress>>(new SocketException((int)SocketError.HostNotFound));

    public async Task<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        Connected.Enqueue(endpoint);

        if (Refuses)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), cancellationToken);

        return new NetworkStream(socket, ownsSocket: true);
    }

    public bool Trusts(X509Certificate? certificate, SslPolicyErrors errors)
        => certificate is not null && Trusted is not null && certificate.GetCertHashString() == Trusted.GetCertHashString();
}

/// <summary>
/// A webhook on the loopback interface that answers as it is told to, the network pointed at it, and a line that
/// sends over that network.
/// </summary>
internal sealed class WebhookRig : IAsyncDisposable
{
    public const string SlackHost = "hooks.slack.com";

    public const string DiscordHost = "discord.com";

    public static readonly IPAddress Public = IPAddress.Parse("203.0.113.40");

    private readonly X509Certificate2 certificate;

    public WebhookRig(Func<HeardRequest, CannedAnswer> answer, TimeSpan? deadline = null, TimeProvider? clock = null)
        : this(request => Task.FromResult(answer(request)), deadline, clock)
    {
    }

    public WebhookRig(Func<HeardRequest, Task<CannedAnswer>> answer, TimeSpan? deadline = null, TimeProvider? clock = null)
    {
        certificate = BroadcasterServerStandIn.SelfSigned(SlackHost, DiscordHost);
        Server = new BroadcasterServerStandIn(answer, certificate);
        Network = new PointedNoticeNetwork(Server.Port) { Trusted = certificate };
        Network.Names[SlackHost] = [Public];
        Network.Names[DiscordHost] = [Public];
        Line = new NoticeLine(Network, "Carina/0.0.0-test", clock ?? TimeProvider.System, deadline ?? NoticeLine.UsualDeadline);
    }

    public static WebhookUrl Slack { get; } = WebhookUrl.Read($"https://{SlackHost}/services/T0000/B0000/made-up-token-wxyz")!;

    public static WebhookUrl Discord { get; } = WebhookUrl.Read($"https://{DiscordHost}/api/webhooks/1234/made-up-token-abcd")!;

    public BroadcasterServerStandIn Server { get; }

    public PointedNoticeNetwork Network { get; }

    public NoticeLine Line { get; }

    public async ValueTask DisposeAsync()
    {
        Line.Dispose();
        await Server.DisposeAsync();
        certificate.Dispose();
    }
}
