using Carina.Api.Authentication;

namespace Carina.Api.Tests.Unit;

public sealed class EndpointRuleSelfCheckTests
{
    private static readonly RoutedSurface Declared =
        new("GET", "/api/tuners", EndpointEffect.Reading);

    [Fact]
    public void DetectsASurfaceThatNeverSaidWhetherItDestroys()
    {
        var undeclared = new RoutedSurface("POST", "/api/tuners/scan", Effect: null);

        Assert.Equal(
            ["POST /api/tuners/scan"],
            EndpointRules.SurfacesWithoutADeclaredEffect([Declared, undeclared]));
    }

    [Fact]
    public void DetectsAReadingMethodThatChangesState()
    {
        var changing = new RoutedSurface("GET", "/api/epg/collect-now", EndpointEffect.Changing);

        Assert.Equal(
            ["GET /api/epg/collect-now"],
            EndpointRules.SurfacesChangingStateWithoutAskingForIt([Declared, changing]));
    }

    [Fact]
    public void DetectsAReadingMethodThatDestroys()
    {
        var destructive = new RoutedSurface("HEAD", "/api/epg/rebuild", EndpointEffect.Destructive);

        Assert.Equal(
            ["HEAD /api/epg/rebuild"],
            EndpointRules.SurfacesChangingStateWithoutAskingForIt([Declared, destructive]));
    }

    [Fact]
    public void LeavesAPostThatChangesStateAlone()
    {
        var changing = new RoutedSurface("POST", "/api/epg/collect-now", EndpointEffect.Changing);

        Assert.Empty(EndpointRules.SurfacesChangingStateWithoutAskingForIt([Declared, changing]));
        Assert.Empty(EndpointRules.SurfacesWithoutADeclaredEffect([Declared, changing]));
    }
}
