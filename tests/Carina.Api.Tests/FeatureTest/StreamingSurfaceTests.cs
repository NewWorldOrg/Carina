using Carina.Api.Authentication;
using Carina.Api.Tests.Unit;

using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class StreamingSurfaceTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    public static readonly IReadOnlyList<string> Roots = ["/api/live", "/api/videos"];

    private static readonly string[] SurfacesTheFeatureHadWhenTheseRulesWereWritten =
    [
        "GET /api/live/channels",
        "GET /api/live/profiles",
        "GET /api/live/sessions",
        "GET /api/live/ws",
        "POST /api/live/ticket",
        "GET /api/videos/{id}",
        "HEAD /api/videos/{id}",
        "GET /api/videos/{id}/play",
        "GET /api/videos/{id}/scrub",
        "GET /api/videos/{id}/thumbnail",
        "POST /api/videos/{id}/ticket",
    ];

    [Fact]
    public void TheStreamingSurfacesAreInTheInventoryTheEffectRuleReads()
    {
        string[] streaming = [.. Streaming().Select(surface => surface.ToString())];

        Assert.All(
            SurfacesTheFeatureHadWhenTheseRulesWereWritten,
            surface => Assert.Contains(surface, streaming, StringComparer.Ordinal));
    }

    [Fact]
    public void NothingUnderTheStreamingSurfaceDeletes()
    {
        Assert.All(Roots, root => Assert.Empty(EndpointRules.SurfacesThatDeleteUnder(Inventory(), root)));
    }

    [Fact]
    public void NothingUnderTheStreamingSurfaceSaysItDestroys()
    {
        Assert.Empty(
            Streaming()
                .Where(surface => surface.Effect is EndpointEffect.Destructive)
                .Select(surface => surface.ToString()));
    }

    [Fact]
    public void TheOnlyThingThatChangesStateUnderTheStreamingSurfaceIsATicketBeingIssued()
    {
        RoutedSurface[] changing =
        [
            .. Streaming().Where(surface => surface.Effect is EndpointEffect.Changing),
        ];

        Assert.NotEmpty(changing);
        Assert.All(
            changing,
            surface =>
            {
                Assert.True(HttpMethods.IsPost(surface.Method), $"{surface} changes state on {surface.Method}");
                Assert.EndsWith("/ticket", surface.Pattern, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void TheRootsAreReadAsWholeSegmentsAndNotAsLeadingLetters()
    {
        RoutedSurface[] inventory =
        [
            new("DELETE", "/api/videos-archive/{id}", EndpointEffect.Destructive),
            new("DELETE", "/api/livestock/{id}", EndpointEffect.Destructive),
            new("DELETE", "/api/live/sessions/{id}", EndpointEffect.Destructive),
        ];

        Assert.Equal(
            ["DELETE /api/live/sessions/{id}"],
            Roots.SelectMany(root => EndpointRules.SurfacesThatDeleteUnder(inventory, root)).ToArray());
        Assert.Equal(
            ["DELETE /api/live/sessions/{id}"],
            inventory.Where(UnderARoot).Select(surface => surface.ToString()).ToArray());
    }

    private static bool UnderARoot(RoutedSurface surface)
        => Roots.Any(root => string.Equals(surface.Pattern, root, StringComparison.Ordinal)
                             || surface.Pattern.StartsWith(root + "/", StringComparison.Ordinal));

    private IEnumerable<RoutedSurface> Streaming() => Inventory().Where(UnderARoot);

    private IReadOnlyList<RoutedSurface> Inventory() => RouteInventory.Of(factory);
}
