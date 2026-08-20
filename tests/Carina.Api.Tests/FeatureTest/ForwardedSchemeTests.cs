using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Authentication;
using Carina.Domain.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class ForwardedProxyProbe : IAsyncDisposable
{
    public const string Password = "a password long enough";

    public const string ProxyAddress = "10.42.0.9";

    private readonly TestingWebApplicationFactory factory = new();

    private ForwardedProxyProbe(string? knownProxies, string arrivingFrom)
    {
        WebApplicationFactory<Program> wired = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(TrustedProxies.ProxiesKey, knownProxies ?? string.Empty);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IStartupFilter>(new ArrivingFrom(IPAddress.Parse(arrivingFrom)));
                services.AddSingleton<IAuthSessionRepository>(Sessions);
                services.AddSingleton<ILocalAccountRepository>(Accounts);
                services.AddSingleton<IOidcSettingsRepository>(Settings);
                services.AddSingleton<IPasswordHasher>(Hasher);
            });
        });

        Client = Behind(wired);
        Signed = Behind(wired.WithTestScheme());
        Signed.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Test", "anything");

        Accounts.Account = LocalAccount.Bootstrap(
            FirstCredentials.Username,
            Hasher.Hash(Password, PasswordHashPolicy.Default),
            DateTime.UtcNow);
    }

    public HeldAuthSessions Sessions { get; } = new();

    public HeldLocalAccount Accounts { get; } = new();

    public HeldOidcSettings Settings { get; } = new();

    public QuickPasswordHasher Hasher { get; } = new();

    public HttpClient Client { get; }

    public HttpClient Signed { get; }

    public static ForwardedProxyProbe TrustingItsProxy() => new(ProxyAddress, ProxyAddress);

    public static ForwardedProxyProbe TrustingNothing() => new(null, ProxyAddress);

    public static ForwardedProxyProbe TrustingSomeoneElse() => new("10.42.0.8", ProxyAddress);

    public Task<HttpResponseMessage> LogInAsync()
        => Client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username = FirstCredentials.Username, password = Password });

    public Task<HttpResponseMessage> ReadConfigAsync()
        => Signed.GetAsync(new Uri($"/{OidcHandshake.ConfigRoute}", UriKind.Relative));

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Signed.Dispose();
        await factory.DisposeAsync();
    }

    private static HttpClient Behind(WebApplicationFactory<Program> wired)
    {
        HttpClient client = wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });

        client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        client.DefaultRequestHeaders.Add(HeaderNames.Origin, "https://localhost");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        return client;
    }

    private sealed class ArrivingFrom(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return builder =>
            {
                builder.Use((context, following) =>
                {
                    context.Connection.RemoteIpAddress = address;

                    return following(context);
                });

                next(builder);
            };
        }
    }
}

public sealed class ForwardedSchemeTests
{
    [Fact]
    public async Task ANamedProxySayingHttpsMakesTheSessionCookieSecureUnderTheOneName()
    {
        await using ForwardedProxyProbe probe = ForwardedProxyProbe.TrustingItsProxy();

        using HttpResponseMessage response = await probe.LogInAsync();
        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith($"{SessionCookie.Name}=", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ANamedProxySayingHttpsMakesTheRedirectUriTheProviderWasRegisteredWith()
    {
        await using ForwardedProxyProbe probe = ForwardedProxyProbe.TrustingItsProxy();

        using HttpResponseMessage response = await probe.ReadConfigAsync();
        JsonElement config = await DataAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $"https://localhost{OidcHandshake.CallbackPath}",
            config.GetProperty("redirectUri").GetString());
    }

    [Fact]
    public async Task AnInstallationTrustingNothingReadsTheSameRequestAsPlainHttp()
    {
        await using ForwardedProxyProbe probe = ForwardedProxyProbe.TrustingNothing();

        using HttpResponseMessage response = await probe.ReadConfigAsync();
        JsonElement config = await DataAsync(response);

        Assert.Equal(
            $"http://localhost{OidcHandshake.CallbackPath}",
            config.GetProperty("redirectUri").GetString());
    }

    [Fact]
    public async Task AProxyThatWasNotNamedIsIgnoredHoweverItLabelledTheRequest()
    {
        await using ForwardedProxyProbe probe = ForwardedProxyProbe.TrustingSomeoneElse();

        using HttpResponseMessage response = await probe.ReadConfigAsync();
        JsonElement config = await DataAsync(response);

        Assert.Equal(
            $"http://localhost{OidcHandshake.CallbackPath}",
            config.GetProperty("redirectUri").GetString());
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("data").Clone();
    }
}
