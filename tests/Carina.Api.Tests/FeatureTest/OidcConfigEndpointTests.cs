using System.Net;
using System.Text.Json;

using Carina.Api.Services;
using Carina.Domain.Auth;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

public sealed class OidcConfigEndpointTests
{
    [Fact]
    public async Task AnInstallationWithNoProviderSaysWhichRedirectUriToRegisterFirst()
    {
        await using OidcProbe probe = OidcProbe.OverHttp();

        JsonElement config = await ReadAsync(probe);

        Assert.False(config.GetProperty("configured").GetBoolean());
        Assert.Equal("http://localhost/api/auth/oidc/callback", config.GetProperty("redirectUri").GetString());
        Assert.True(config.GetProperty("admitsEveryone").GetBoolean());
        Assert.False(config.GetProperty("secretHeld").GetBoolean());
    }

    [Fact]
    public async Task SavingAProviderChecksTheDiscoveryDocumentAnswersBeforeAnythingIsKept()
    {
        await using OidcProbe probe = OidcProbe.OverHttp();
        probe.Idp.Reachable = false;

        using HttpResponseMessage saved = await probe.SaveConfigAsync(new
        {
            discoveryUrl = MockIdentityProvider.DiscoveryUrl,
            clientId = "carina",
            clientSecret = OidcProbe.Secret,
        });

        Assert.Equal(HttpStatusCode.BadRequest, saved.StatusCode);
        Assert.Null(probe.Settings.Settings);
        Assert.Equal(0, probe.Settings.Saves);
    }

    [Fact]
    public async Task AProviderThatAnswersIsKeptAlongWithWhoItLetsThrough()
    {
        await using OidcProbe probe = OidcProbe.OverHttp();

        JsonElement config = await SaveAsync(probe, new
        {
            discoveryUrl = MockIdentityProvider.DiscoveryUrl,
            clientId = "carina",
            clientSecret = OidcProbe.Secret,
            allowedGroups = new[] { "operators" },
            allowedHostedDomains = new[] { "example.test" },
        });

        Assert.True(config.GetProperty("configured").GetBoolean());
        Assert.False(config.GetProperty("admitsEveryone").GetBoolean());
        Assert.Equal(["operators"], Strings(config, "allowedGroups"));
        Assert.Equal(["example.test"], Strings(config, "allowedHostedDomains"));
        Assert.Equal(OnTheWire(OidcReach.Reachable), config.GetProperty("reach").GetString());
    }

    [Fact]
    public async Task TheSecretGoesInAndNeverComesBackOut()
    {
        await using OidcProbe probe = OidcProbe.OverHttp();

        await SaveAsync(probe, new
        {
            discoveryUrl = MockIdentityProvider.DiscoveryUrl,
            clientId = "carina",
            clientSecret = OidcProbe.Secret,
        });

        using HttpResponseMessage read = await probe.ReadConfigAsync();
        string body = await read.Content.ReadAsStringAsync();

        Assert.DoesNotContain(OidcProbe.Secret, body, StringComparison.Ordinal);
        Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("secretHeld").GetBoolean());
    }

    [Fact]
    public async Task SavingTheFormAgainWithoutTheSecretKeepsTheSecretAlreadyHeld()
    {
        await using OidcProbe probe = OidcProbe.OverHttp();

        await SaveAsync(probe, new
        {
            discoveryUrl = MockIdentityProvider.DiscoveryUrl,
            clientId = "carina",
            clientSecret = OidcProbe.Secret,
        });

        JsonElement again = await SaveAsync(probe, new
        {
            discoveryUrl = MockIdentityProvider.DiscoveryUrl,
            clientId = "carina-renamed",
        });

        Assert.True(again.GetProperty("secretHeld").GetBoolean());
        Assert.Equal(new ClientSecret(OidcProbe.Secret), probe.Settings.Settings!.ClientSecret);
        Assert.Equal("carina-renamed", probe.Settings.Settings.ClientId);
    }

    [Fact]
    public async Task AFirstSaveWithoutASecretIsRefusedRatherThanHalfKept()
    {
        await using OidcProbe probe = OidcProbe.OverHttp();

        using HttpResponseMessage saved = await probe.SaveConfigAsync(new
        {
            discoveryUrl = MockIdentityProvider.DiscoveryUrl,
            clientId = "carina",
        });

        Assert.Equal(HttpStatusCode.BadRequest, saved.StatusCode);
        Assert.Contains(
            OidcConfigService.AFirstSaveCarriesItsSecret,
            await saved.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADiscoveryUrlThatIsNotAnHttpsUrlIsRefusedBeforeAnythingIsFetched()
    {
        await using OidcProbe probe = OidcProbe.OverHttp();

        using HttpResponseMessage saved = await probe.SaveConfigAsync(new
        {
            discoveryUrl = "http://login.example.test/.well-known/openid-configuration",
            clientId = "carina",
            clientSecret = OidcProbe.Secret,
        });

        Assert.Equal(HttpStatusCode.BadRequest, saved.StatusCode);
        Assert.Empty(probe.Idp.Visits);
    }

    [Fact]
    public async Task ClearingTheFormLeavesTheInstallationOnItsLocalAccount()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedGroups: ["operators"]);

        JsonElement cleared = await SaveAsync(probe, new { discoveryUrl = string.Empty, clientId = string.Empty });

        Assert.False(cleared.GetProperty("configured").GetBoolean());
        Assert.False(cleared.GetProperty("secretHeld").GetBoolean());
        Assert.Equal(OnTheWire(OidcReach.NotConfigured), cleared.GetProperty("reach").GetString());
    }

    [Fact]
    public async Task ASavedProviderThatStopsAnsweringIsSurfacedAsDegradedWithoutTakingTheAppDown()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();
        probe.Idp.Reachable = false;

        using HttpResponseMessage started = await probe.StartAsync();
        using HttpResponseMessage health = await probe.Client.GetAsync(new Uri("/api/health", UriKind.Relative));
        JsonElement told = JsonDocument.Parse(await health.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HealthView.Alive, told.GetProperty("status").GetString());
        Assert.Equal([HealthView.TheIdentityProvider], Strings(told, "degraded"));
    }

    [Fact]
    public async Task AProviderThatAnswersLeavesHealthWithNothingDegradedToReport()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage started = await probe.StartAsync();
        using HttpResponseMessage health = await probe.Client.GetAsync(new Uri("/api/health", UriKind.Relative));
        JsonElement told = JsonDocument.Parse(await health.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HealthView.Alive, told.GetProperty("status").GetString());
        Assert.Empty(Strings(told, "degraded"));
    }

    [Fact]
    public async Task ReadingTheConfigurationNeedsTheCallerToHaveSignedIn()
    {
        await using OidcProbe probe = OidcProbe.OverHttp();

        using HttpResponseMessage read = await probe.Client.GetAsync(
            new Uri("/api/auth/oidc-config", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
    }

    private static string OnTheWire(OidcReach reach)
        => JsonNamingPolicy.CamelCase.ConvertName(reach.ToString());

    private static string[] Strings(JsonElement element, string name)
        => [.. element.GetProperty(name).EnumerateArray().Select(entry => entry.GetString()!)];

    private static async Task<JsonElement> ReadAsync(OidcProbe probe)
    {
        using HttpResponseMessage read = await probe.ReadConfigAsync();

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        return JsonDocument.Parse(await read.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Clone();
    }

    private static async Task<JsonElement> SaveAsync(OidcProbe probe, object body)
    {
        using HttpResponseMessage saved = await probe.SaveConfigAsync(body);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        return JsonDocument.Parse(await saved.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Clone();
    }
}
