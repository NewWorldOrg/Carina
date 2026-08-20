using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Api.Authentication;
using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class PublicOriginProbe : IAsyncDisposable
{
    public const string PublicAddress = "https://carina.example";

    public const string RelayedAddress = "http://carina-app.relayed:8080";

    public const string InsideAddress = "http://carina-app.inside:8080";

    public const string Secret = "the-client-secret";

    private readonly TestingWebApplicationFactory factory = new();

    private readonly HttpClient outward;

    private readonly WebApplicationFactory<Program> wired;

    private PublicOriginProbe(string? origin)
    {
        outward = new HttpClient(Idp);

        wired = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(PublicOrigin.Key, origin ?? string.Empty);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAuthSessionRepository>(Sessions);
                services.AddSingleton<ILocalAccountRepository>(Accounts);
                services.AddSingleton<IOidcSettingsRepository>(Settings);
                services.AddSingleton<IPasswordHasher>(Hasher);
                services.AddSingleton<IOidcGateway>(new OidcGateway(outward));
            });
        });

        Relayed = Reaching(wired, RelayedAddress);
        Inside = Reaching(wired.WithTestScheme(), InsideAddress);
        Inside.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", "anything");
    }

    public MockIdentityProvider Idp { get; } = new();

    public HeldAuthSessions Sessions { get; } = new();

    public HeldLocalAccount Accounts { get; } = new();

    public HeldOidcSettings Settings { get; } = new();

    public QuickPasswordHasher Hasher { get; } = new();

    public HttpClient Relayed { get; }

    public HttpClient Inside { get; }

    public static PublicOriginProbe Named(string origin) => new(origin);

    public static PublicOriginProbe NamingNothing() => new(null);

    public static string RedirectUriIn(Uri authorize)
    {
        ArgumentNullException.ThrowIfNull(authorize);

        return QueryHelpers.ParseQuery(authorize.Query)["redirect_uri"].ToString();
    }

    public PublicOriginProbe Configured()
    {
        Settings.Settings = OidcSettings.Rehydrate(
            OidcSettings.TheOnlyRow,
            MockIdentityProvider.DiscoveryUrl,
            Idp.ClientId,
            new ClientSecret(Secret),
            DateTime.UtcNow,
            null,
            null);

        return this;
    }

    public async Task<JsonElement> ReadConfigAsync()
    {
        using HttpResponseMessage read = await Inside.GetAsync(
            new Uri($"/{OidcHandshake.ConfigRoute}", UriKind.Relative));

        return JsonDocument.Parse(await read.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").Clone();
    }

    public Task<HttpResponseMessage> StartAsync()
        => Relayed.GetAsync(new Uri(OidcHandshake.StartPath, UriKind.Relative));

    public async Task<Uri> AuthorizeUriAsync()
    {
        using HttpResponseMessage started = await StartAsync();

        return new Uri(started.Headers.Location!.ToString());
    }

    public async Task<HttpResponseMessage> CallbackFromInsideAsync(string state, string code, string carried)
    {
        using HttpClient client = wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(InsideAddress),
            AllowAutoRedirect = false,
        });

        client.DefaultRequestHeaders.Add(HeaderNames.Cookie, carried);

        return await client.GetAsync(new Uri(
            $"{OidcHandshake.CallbackPath}?{OidcHandshake.StateKey}={Uri.EscapeDataString(state)}"
            + $"&{OidcHandshake.CodeKey}={Uri.EscapeDataString(code)}",
            UriKind.Relative));
    }

    public async ValueTask DisposeAsync()
    {
        Relayed.Dispose();
        Inside.Dispose();
        outward.Dispose();
        Idp.Dispose();
        await factory.DisposeAsync();
    }

    private static HttpClient Reaching(WebApplicationFactory<Program> wired, string address)
    {
        HttpClient client = wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(address),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        client.DefaultRequestHeaders.Add(HeaderNames.Origin, address);

        return client;
    }
}

public sealed class PublicRedirectUriTests
{
    [Fact]
    public async Task TheScreenIsToldThePublicRedirectUriEvenWhenItAskedFromInside()
    {
        await using PublicOriginProbe probe = PublicOriginProbe.Named(PublicOriginProbe.PublicAddress);

        JsonElement config = await probe.ReadConfigAsync();

        Assert.Equal(
            $"{PublicOriginProbe.PublicAddress}{OidcHandshake.CallbackPath}",
            config.GetProperty("redirectUri").GetString());
        Assert.False(config.GetProperty("redirectUriGuessed").GetBoolean());
    }

    [Fact]
    public async Task TheProviderIsSentTheRedirectUriTheScreenAsksForItToBeRegisteredWith()
    {
        await using PublicOriginProbe probe =
            PublicOriginProbe.Named(PublicOriginProbe.PublicAddress).Configured();

        Uri authorize = await probe.AuthorizeUriAsync();
        JsonElement config = await probe.ReadConfigAsync();

        Assert.Equal(
            config.GetProperty("redirectUri").GetString(),
            PublicOriginProbe.RedirectUriIn(authorize));
    }

    [Fact]
    public async Task TheProviderIsSentOneRedirectUriHoweverEachHalfOfTheHandshakeArrived()
    {
        await using PublicOriginProbe probe =
            PublicOriginProbe.Named(PublicOriginProbe.PublicAddress).Configured();

        using HttpResponseMessage started = await probe.StartAsync();
        var authorize = new Uri(started.Headers.Location!.ToString());
        string code = probe.Idp.Authorize(authorize, new MockIdentityUser("someone"));

        using HttpResponseMessage back = await probe.CallbackFromInsideAsync(
            MockIdentityProvider.StateOf(authorize),
            code,
            started.Headers.GetValues("Set-Cookie").First().Split(';')[0]);

        Assert.Single(probe.Sessions.Sessions);
        Assert.Equal(AuthMethod.Oidc, probe.Sessions.Sessions[0].Method);
    }

    [Fact]
    public async Task AnInstallationNamingNoPublicOriginFallsBackToTheRequestAndSaysTheUriIsAGuess()
    {
        await using PublicOriginProbe probe = PublicOriginProbe.NamingNothing();

        JsonElement config = await probe.ReadConfigAsync();

        Assert.Equal(
            $"{PublicOriginProbe.InsideAddress}{OidcHandshake.CallbackPath}",
            config.GetProperty("redirectUri").GetString());
        Assert.True(config.GetProperty("redirectUriGuessed").GetBoolean());
    }
}
