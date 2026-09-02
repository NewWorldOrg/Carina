using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;

using Carina.Api.Authentication;
using Carina.Api.Live;
using Carina.Domain.Auth;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class ProxiedKestrelProbe : IAsyncDisposable
{
    public const string Password = "a password long enough";

    private const string ForwardedProto = "X-Forwarded-Proto";

    private readonly TestingWebApplicationFactory factory = new();

    private readonly WebApplicationFactory<Program> wired;

    private readonly TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 4 });

    private ProxiedKestrelProbe()
    {
        Transcoders = new HeldTranscoders(budget);
        wired = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(TrustedProxies.ProxiesKey, "127.0.0.1 ::1");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAuthSessionRepository>(Sessions);
                services.AddSingleton<ILocalAccountRepository>(Accounts);
                services.AddSingleton<IPasswordHasher>(Hasher);
                services.AddSingleton<ILiveSupply>(Supply);
                services.AddSingleton<ITranscodeBudget>(budget);
                services.AddSingleton<ILiveTranscoderFactory>(Transcoders);
            });
        });
        wired.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        wired.StartServer();
        Address = wired.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses
            .Select(address => new Uri(address))
            .First(address => address.Host == IPAddress.Loopback.ToString());
        Accounts.Account = LocalAccount.Bootstrap(
            FirstCredentials.Username,
            Hasher.Hash(Password, PasswordHashPolicy.Default),
            DateTime.UtcNow);
    }

    public Uri Address { get; }

    public HeldAuthSessions Sessions { get; } = new();

    public HeldLocalAccount Accounts { get; } = new();

    public QuickPasswordHasher Hasher { get; } = new();

    public PipedSupply Supply { get; } = new();

    public HeldTranscoders Transcoders { get; }

    public static ProxiedKestrelProbe ListeningOnKestrel() => new();

    public string PageAt(string scheme) => $"{scheme}://{Address.Authority}";

    public Uri Wire => new($"ws://{Address.Authority}{LiveWire.Path}?network=32736&service=1024&profile=720p30");

    public async Task<string> SignedInCookieAsync()
    {
        using HttpClient client = new() { BaseAddress = Address };

        client.DefaultRequestHeaders.Add(HeaderNames.Origin, PageAt(Uri.UriSchemeHttp));

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username = FirstCredentials.Username, password = Password });

        response.EnsureSuccessStatusCode();

        string handed = response.Headers.GetValues(HeaderNames.SetCookie).Single();

        return handed[..handed.IndexOf(';', StringComparison.Ordinal)];
    }

    public async Task<HttpResponseMessage> PostThroughTheProxyAsync(string forwardedProto, string origin, string cookie)
    {
        using HttpClient client = new() { BaseAddress = Address };

        client.DefaultRequestHeaders.Add(HeaderNames.Origin, origin);
        client.DefaultRequestHeaders.Add(HeaderNames.Cookie, cookie);
        client.DefaultRequestHeaders.Add(ForwardedProto, forwardedProto);

        return await client.PostAsJsonAsync(new Uri("/api/live/ticket", UriKind.Relative), new { network = 32736, service = 1024 });
    }

    public ClientWebSocket Opening(string cookie, string? forwardedProto, string? origin)
    {
        ClientWebSocket client = new();

        client.Options.CollectHttpResponseDetails = true;
        client.Options.SetRequestHeader(HeaderNames.Cookie, cookie);

        if (forwardedProto is not null)
        {
            client.Options.SetRequestHeader(ForwardedProto, forwardedProto);
        }

        if (origin is not null)
        {
            client.Options.SetRequestHeader(HeaderNames.Origin, origin);
        }

        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await wired.DisposeAsync();
        await factory.DisposeAsync();
    }
}

public sealed class LiveWireBehindAProxyTests
{
    [Theory]
    [InlineData("wss", "https")]
    [InlineData("ws", "http")]
    public async Task AWireTheProxyLabelsAsAWebSocketIsOpenedForThePageThatWasServedOverTheMatchingScheme(
        string forwardedProto,
        string page)
    {
        await using ProxiedKestrelProbe probe = ProxiedKestrelProbe.ListeningOnKestrel();
        string cookie = await probe.SignedInCookieAsync();

        using ClientWebSocket client = probe.Opening(cookie, forwardedProto, probe.PageAt(page));

        await client.ConnectAsync(probe.Wire, Patiently());

        Assert.Equal(WebSocketState.Open, client.State);
        Assert.Equal(HttpStatusCode.SwitchingProtocols, client.HttpStatusCode);

        LiveFrame first = await Take(client);

        Assert.Equal(LiveChannel.Control, first.Channel);
        Assert.Null(LiveStartup.ReadProgress(first.Payload.Span).Fault);
        Assert.Equal(1, probe.Transcoders.Started);
    }

    [Theory]
    [InlineData("wss", "https://elsewhere.example")]
    [InlineData("wss", "http")]
    [InlineData("ws", "https")]
    [InlineData("ftp", "http")]
    public async Task AWireFromAPageThisAppDidNotServeIsStillRefusedWhateverTheProxyLabelsIt(
        string forwardedProto,
        string origin)
    {
        await using ProxiedKestrelProbe probe = ProxiedKestrelProbe.ListeningOnKestrel();
        string cookie = await probe.SignedInCookieAsync();
        string page = origin.Contains("://", StringComparison.Ordinal) ? origin : probe.PageAt(origin);

        using ClientWebSocket client = probe.Opening(cookie, forwardedProto, page);

        await Assert.ThrowsAsync<WebSocketException>(() => client.ConnectAsync(probe.Wire, Patiently()));

        Assert.Equal(HttpStatusCode.Forbidden, client.HttpStatusCode);
        Assert.Equal(0, probe.Transcoders.Started);
    }

    [Fact]
    public async Task ARequestThatChangesStateBehindTheSameProxyIsStillAnsweredForThePageItNames()
    {
        await using ProxiedKestrelProbe probe = ProxiedKestrelProbe.ListeningOnKestrel();
        string cookie = await probe.SignedInCookieAsync();

        using HttpResponseMessage fromThisPage = await probe.PostThroughTheProxyAsync(
            "https",
            probe.PageAt(Uri.UriSchemeHttps),
            cookie);
        using HttpResponseMessage fromElsewhere = await probe.PostThroughTheProxyAsync(
            "https",
            "https://elsewhere.example",
            cookie);

        Assert.NotEqual(HttpStatusCode.Forbidden, fromThisPage.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, fromElsewhere.StatusCode);
    }

    private static CancellationToken Patiently() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

    private static async Task<LiveFrame> Take(WebSocket socket)
    {
        byte[] heard = new byte[64 * 1024];

        WebSocketReceiveResult said = await socket.ReceiveAsync(new ArraySegment<byte>(heard), Patiently());

        Assert.Equal(WebSocketMessageType.Binary, said.MessageType);

        LiveFraming framing = LiveFrame.Read(heard.AsSpan(0, said.Count));

        Assert.NotNull(framing.Frame);

        return framing.Frame;
    }
}
