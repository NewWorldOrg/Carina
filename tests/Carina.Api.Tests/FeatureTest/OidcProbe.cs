using System.Net.Http.Json;

using Carina.Api.Authentication;
using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class OidcProbe : IAsyncDisposable
{
    public const string Secret = "the-client-secret";

    private static readonly DateTime Founded = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    private readonly HttpClient outward;

    private readonly WebApplicationFactory<Program> wired;

    private OidcProbe(bool secure)
    {
        outward = new HttpClient(Idp);
        Clock = new WoundClock(Founded);
        Idp.Clock = Clock;

        wired = factory.WithWebHostBuilder(
            builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAuthSessionRepository>(Sessions);
                services.AddSingleton<ILocalAccountRepository>(Accounts);
                services.AddSingleton<IOidcSettingsRepository>(Settings);
                services.AddSingleton<IPasswordHasher>(Hasher);
                services.AddSingleton<IOidcGateway>(new OidcGateway(outward));
                services.AddSingleton<TimeProvider>(Clock);
            }));

        Client = Browsing(wired, secure);
        Signed = Browsing(wired.WithTestScheme(), secure);
        Signed.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test", "anything");
    }

    public MockIdentityProvider Idp { get; } = new();

    public WoundClock Clock { get; }

    public HeldAuthSessions Sessions { get; } = new();

    public HeldLocalAccount Accounts { get; } = new();

    public HeldOidcSettings Settings { get; } = new();

    public QuickPasswordHasher Hasher { get; } = new();

    public HttpClient Client { get; }

    public HttpClient Signed { get; }

    public static OidcProbe OverHttp() => new(secure: false);

    public static OidcProbe OverHttps() => new(secure: true);

    public OidcProbe Configured(
        IEnumerable<string>? allowedGroups = null,
        IEnumerable<string>? allowedHostedDomains = null)
    {
        Settings.Settings = OidcSettings.Rehydrate(
            OidcSettings.TheOnlyRow,
            MockIdentityProvider.DiscoveryUrl,
            Idp.ClientId,
            new ClientSecret(Secret),
            Founded,
            allowedGroups,
            allowedHostedDomains);

        return this;
    }

    public OidcProbe WithALocalAccount()
    {
        Accounts.Account = LocalAccount.Bootstrap(
            FirstCredentials.Username,
            Hasher.Hash(AuthProbe.Password, PasswordHashPolicy.Default),
            Founded);

        return this;
    }

    public Task<HttpResponseMessage> LogInAsync()
        => Client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username = FirstCredentials.Username, password = AuthProbe.Password });

    public HttpClient Relaying(string carried)
    {
        HttpClient client = wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });

        client.DefaultRequestHeaders.Add(HeaderNames.Cookie, carried);

        return client;
    }

    public Task<HttpResponseMessage> StartAsync(string? next = null)
        => Client.GetAsync(new Uri(
            next is null
                ? OidcHandshake.StartPath
                : $"{OidcHandshake.StartPath}?{LoginRedirect.ReturnKey}={Uri.EscapeDataString(next)}",
            UriKind.Relative));

    public Task<HttpResponseMessage> CallbackAsync(string? state, string? code, HttpClient? through = null)
        => (through ?? Client).GetAsync(new Uri(
            $"{OidcHandshake.CallbackPath}?{OidcHandshake.StateKey}={Uri.EscapeDataString(state ?? string.Empty)}"
            + $"&{OidcHandshake.CodeKey}={Uri.EscapeDataString(code ?? string.Empty)}",
            UriKind.Relative));

    public async Task<HttpResponseMessage> SignInAsync(MockIdentityUser user, string? next = null)
    {
        using HttpResponseMessage started = await StartAsync(next);
        var authorize = new Uri(started.Headers.Location!.ToString());

        return await CallbackAsync(MockIdentityProvider.StateOf(authorize), Idp.Authorize(authorize, user));
    }

    public async Task<Uri> AuthorizeUriAsync(string? next = null)
    {
        using HttpResponseMessage started = await StartAsync(next);

        return new Uri(started.Headers.Location!.ToString());
    }

    public Task<HttpResponseMessage> SignInOptionsAsync()
        => Client.GetAsync(new Uri(SignInOptions.Path, UriKind.Relative));

    public Task<HttpResponseMessage> ReadConfigAsync()
        => Signed.GetAsync(new Uri($"/{OidcHandshake.ConfigRoute}", UriKind.Relative));

    public Task<HttpResponseMessage> SaveConfigAsync(object body)
        => Signed.PutAsJsonAsync(new Uri($"/{OidcHandshake.ConfigRoute}", UriKind.Relative), body);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Signed.Dispose();
        outward.Dispose();
        Idp.Dispose();
        await factory.DisposeAsync();
    }

    private static HttpClient Browsing(WebApplicationFactory<Program> wired, bool secure)
    {
        HttpClient client = wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(secure ? "https://localhost" : "http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        client.DefaultRequestHeaders.Add(
            HeaderNames.Origin,
            client.BaseAddress!.GetLeftPart(UriPartial.Authority));

        return client;
    }
}
