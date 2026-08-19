using System.Net;

using Carina.Api.Authentication;
using Carina.Api.Tests.Unit;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class RoutedSurfaceTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Fact]
    public void EveryRoutedSurfaceSaysWhetherItDestroys()
    {
        Assert.Empty(EndpointRules.SurfacesWithoutADeclaredEffect(Inventory()));
    }

    [Fact]
    public void NoRoutedSurfaceChangesStateOnAMethodThatOnlyReads()
    {
        Assert.Empty(EndpointRules.SurfacesChangingStateWithoutAskingForIt(Inventory()));
    }

    [Fact]
    public void TheDestructiveSurfacesAreTheOnesThatDiscardWhatCannotBeCollectedAgain()
    {
        Assert.Equal(
            [
                "DELETE /api/services/{networkId:int}-{serviceId:int}/candidate-channels/{candidateChannelId:guid}",
                "POST /api/epg/archive/forget-service",
                "POST /api/epg/rebuild",
                "POST /api/tuners/scan/{scanId:guid}/apply",
            ],
            Inventory()
                .Where(surface => surface.Effect is EndpointEffect.Destructive)
                .Select(surface => surface.ToString())
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void TheInventoryHoldsTheSurfacesTheDocumentCannotDescribe()
    {
        string[] patterns = [.. Inventory().Select(surface => surface.Pattern)];

        Assert.Contains("/api/events", patterns, StringComparer.Ordinal);
        Assert.Contains("/api/programs/bulk", patterns, StringComparer.Ordinal);
        Assert.Contains("/api/health", patterns, StringComparer.Ordinal);
    }

    [Fact]
    public async Task EveryRoutedSurfaceOutsideTheEnumeratedListRefusesAClientCarryingNoCredentials()
    {
        WebApplicationFactory<Program> guarded = factory.WithTestScheme();
        using HttpClient client = guarded.CreateClient();
        var admitted = new List<string>();

        foreach (RoutedSurface surface in RouteInventory.Of(guarded))
        {
            string path = RouteInventory.SamplePath(surface.Pattern);

            if (AnonymousSurfaces.WhileDeveloping.Admit(surface.Method, path))
            {
                continue;
            }

            using var asking = new HttpRequestMessage(
                new HttpMethod(surface.Method),
                new Uri(path, UriKind.Relative));
            using HttpResponseMessage response = await client.SendAsync(
                asking,
                HttpCompletionOption.ResponseHeadersRead);

            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                admitted.Add($"{surface.Method} {path} answered {(int)response.StatusCode}");
            }
        }

        Assert.Empty(admitted);
    }

    [Fact]
    public async Task TheEnumeratedSurfacesAreReachedWithoutCredentialsWhileASchemeIsRegistered()
    {
        using HttpClient client = factory.WithTestScheme().CreateClient();

        using HttpResponseMessage health = await client.GetAsync(new Uri("/api/health", UriKind.Relative));
        using HttpResponseMessage document = await client.GetAsync(new Uri(ServedOpenApi.Route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
    }

    [Theory]
    [InlineData("/openapi/sample.json")]
    [InlineData("/api/health/detail")]
    [InlineData("/_next/staticbait/main.js")]
    public async Task APathThatMerelyLooksLikeAnEnumeratedSurfaceIsRefused(string path)
    {
        using HttpClient client = factory.WithTestScheme().CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private IReadOnlyList<RoutedSurface> Inventory() => RouteInventory.Of(factory);
}
