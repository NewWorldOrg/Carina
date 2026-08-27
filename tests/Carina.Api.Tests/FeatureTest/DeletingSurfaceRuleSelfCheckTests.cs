using Carina.Api.Tests.Unit;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class DeletingSurfaceRuleSelfCheckTests
{
    public const string LongWayRound = "DELETE /api/recordings/{id}/fixture-only";

    [Fact]
    public async Task TheRuleReadsADeleteWrittenTheLongWayRoundOnASubPath()
    {
        await using var factory = new TestingWebApplicationFactory();
        WebApplicationFactory<Program> carrying = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services
                .AddControllers()
                .ConfigureApplicationPartManager(parts => parts.ApplicationParts.Add(
                    new AssemblyPart(typeof(DeletesARecordingFileFixtureAction).Assembly)))));

        IReadOnlyList<RoutedSurface> inventory = RouteInventory.Of(carrying);

        Assert.Contains(
            LongWayRound,
            inventory.Select(surface => surface.ToString()),
            StringComparer.Ordinal);
        Assert.Equal([LongWayRound], EndpointRules.SurfacesThatDeleteUnder(inventory, "/api/recordings"));
        Assert.Contains(LongWayRound, EndpointRules.SurfacesThatDelete(inventory), StringComparer.Ordinal);
    }

    [Fact]
    public async Task TheFixtureIsNowhereNearTheSurfaceTheApplicationServes()
    {
        await using var factory = new TestingWebApplicationFactory();

        Assert.DoesNotContain(
            LongWayRound,
            RouteInventory.Of(factory).Select(surface => surface.ToString()),
            StringComparer.Ordinal);
    }

    [Fact]
    public void TheRuleReadsTheMethodRatherThanHowSomebodySpelledTheAttribute()
    {
        RoutedSurface[] inventory =
        [
            new("GET", "/api/recordings", Carina.Api.Authentication.EndpointEffect.Reading),
            new("DELETE", "/api/recordings/{id}/file", Carina.Api.Authentication.EndpointEffect.Changing),
            new("DELETE", "/api/library/recordings/{id}", Carina.Api.Authentication.EndpointEffect.Destructive),
        ];

        Assert.Equal(
            ["DELETE /api/recordings/{id}/file"],
            EndpointRules.SurfacesThatDeleteUnder(inventory, "/api/recordings"));
        Assert.Equal(
            ["DELETE /api/library/recordings/{id}", "DELETE /api/recordings/{id}/file"],
            EndpointRules.SurfacesThatDelete(inventory));
    }

    [Fact]
    public void APathThatMerelyStartsWithTheSameLettersIsNotUnderIt()
    {
        RoutedSurface[] inventory =
        [
            new("DELETE", "/api/recordings-archive/{id}", Carina.Api.Authentication.EndpointEffect.Destructive),
        ];

        Assert.Empty(EndpointRules.SurfacesThatDeleteUnder(inventory, "/api/recordings"));
    }
}
