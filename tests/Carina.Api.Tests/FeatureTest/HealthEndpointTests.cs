using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Responder.Health;
using Carina.Api.Services;
using Carina.Domain.Auth;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

public sealed class HealthEndpointTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Fact]
    public async Task HealthIsServedWithoutAuthentication()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        HealthResponder? payload = await response.Content.ReadFromJsonAsync<HealthResponder>();
        Assert.Equal("ok", payload?.Status);
        Assert.Empty(payload!.Degraded);
    }

    [Fact]
    public async Task TheProbeAnswersWithTheWordsItHasAlwaysAnsweredWithAndNoOperationalDetail()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(
            ["status", "degraded"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
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

    private static string[] Strings(JsonElement element, string name)
        => [.. element.GetProperty(name).EnumerateArray().Select(entry => entry.GetString()!)];
}
