using Carina.Api.Authentication;
using Carina.Api.Tests.Unit;

using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class MigrationSurfaceTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const string Root = "/api/migration";

    [Fact]
    public void TheMigrationSurfaceIsTheOneTheRecordIsReadThrough()
        => Assert.Equal(
            ["GET /api/migration/record"],
            Migration().Select(surface => surface.ToString()).Order(StringComparer.Ordinal).ToArray());

    [Fact]
    public void NothingAnywhereElseOffersAMigrationOfItsOwn()
        => Assert.Equal(
            ["/api/migration/record"],
            Inventory()
                .Select(surface => surface.Pattern)
                .Where(pattern => pattern.Contains("migration", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());

    [Fact]
    public void NothingUnderTheMigrationSurfaceIsAskedForOnAMethodThatWrites()
        => Assert.All(Migration(), surface => Assert.True(
            HttpMethods.IsGet(surface.Method),
            $"{surface} reaches the record on a method that is not a read"));

    [Fact]
    public void NothingUnderTheMigrationSurfaceChangesAnythingOrSaysItDestroys()
        => Assert.All(Migration(), surface => Assert.Equal(EndpointEffect.Reading, surface.Effect));

    [Fact]
    public void NothingUnderTheMigrationSurfaceDeletes()
        => Assert.Empty(EndpointRules.SurfacesThatDeleteUnder(Inventory(), Root));

    [Fact]
    public void TheRootIsReadAsAWholeSegmentAndNotAsLeadingLetters()
    {
        RoutedSurface[] inventory =
        [
            new("POST", "/api/migrations-of-a-kind/run", EndpointEffect.Changing),
            new("POST", "/api/migration/run", EndpointEffect.Changing),
        ];

        Assert.Equal(
            ["POST /api/migration/run"],
            inventory.Where(UnderTheRoot).Select(surface => surface.ToString()).ToArray());
    }

    private static bool UnderTheRoot(RoutedSurface surface)
        => string.Equals(surface.Pattern, Root, StringComparison.Ordinal)
           || surface.Pattern.StartsWith(Root + "/", StringComparison.Ordinal);

    private IEnumerable<RoutedSurface> Migration() => Inventory().Where(UnderTheRoot);

    private IReadOnlyList<RoutedSurface> Inventory() => RouteInventory.Of(factory);
}
