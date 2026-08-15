using System.Net;
using System.Text.Json;

using Carina.Api.Events;
using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.TestSupport;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class SeamProbe : IAsyncDisposable
{
    private readonly TestingWebApplicationFactory factory = new();

    private SeamProbe(bool schemeRegistered)
    {
        var wired = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IBroadcastServiceRepository>(Services);
            services.AddSingleton<ICandidateChannelRepository>(Candidates);
            services.AddSingleton<IScanRunRepository>(Runs);
            services.AddSingleton<ISatelliteTransportStreamRepository>(SatelliteStreams);
        }));

        Client = (schemeRegistered ? wired.WithTestScheme() : wired).CreateClient();
    }

    public HttpClient Client { get; }

    public HeldServices Services { get; } = new();

    public HeldCandidates Candidates { get; } = new();

    public HeldScanRuns Runs { get; } = new();

    public HeldSatelliteStreams SatelliteStreams { get; } = new();

    public static SeamProbe WithNoSchemeRegistered() => new(schemeRegistered: false);

    public static SeamProbe WithASchemeRegistered() => new(schemeRegistered: true);

    public Task<HttpResponseMessage> GetAsync(string path)
        => Client.GetAsync(new Uri(path, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }
}

[Collection(FeatureTestCollection.Name)]
public sealed class DefaultDenyTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    public static TheoryData<string> EveryBusinessSurface =>
    [
        "/api/driver/status",
        "/api/tuners",
        "/api/tuners/detected",
        "/api/tuners/scan-runs",
        "/api/services",
        AppEventStream.Path,
    ];

    [Theory]
    [MemberData(nameof(EveryBusinessSurface))]
    public async Task ASurfaceAnswersAClientCarryingNoCredentialsWhileTheAppHoldsNoAuthenticationScheme(
        string path)
    {
        await using var probe = SeamProbe.WithNoSchemeRegistered();

        using var response = await probe.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(EveryBusinessSurface))]
    public async Task ASurfaceRefusesAClientCarryingNoCredentialsOnceAnAuthenticationSchemeIsRegistered(
        string path)
    {
        await using var probe = SeamProbe.WithASchemeRegistered();

        using var response = await probe.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheCatalogArrivesWithoutCredentialsRatherThanADenialWithNoWayToSatisfyIt()
    {
        await using var probe = SeamProbe.WithNoSchemeRegistered();
        probe.Services.Services.Add(BroadcastService.Discover(
            new NetworkId(1),
            new ServiceId(101),
            "Reachable",
            ServiceCategory.Television,
            TunerHoldingDriverClient.At));

        using var response = await probe.GetAsync("/api/services");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Reachable",
            body.RootElement.GetProperty("data")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AnUnknownPathReachesRoutingWhileTheAppHoldsNoAuthenticationScheme()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AWrongMethodOnAKnownPathReachesRoutingWhileTheAppHoldsNoAuthenticationScheme()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            new Uri("/api/driver/status", UriKind.Relative),
            content: null
        );

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownPathIsRefusedBeforeRoutingHasAnEndpointToAnswer404AboutOnceASchemeIsRegistered()
    {
        using var client = factory.WithTestScheme().CreateClient();

        using var response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AWrongMethodOnAKnownPathIsRefusedBeforeRoutingHasAnEndpointToAnswer405AboutOnceASchemeIsRegistered()
    {
        using var client = factory.WithTestScheme().CreateClient();

        using var response = await client.PostAsync(
            new Uri("/api/driver/status", UriKind.Relative),
            content: null
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheDocumentIsHandedOutWithoutCredentialsInDevelopmentBecauseTheClientIsGeneratedFromIt()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(ServedOpenApi.Route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheDocumentIsNotServedAtAllOutsideDevelopmentRatherThanLeftToASeamThatMayAdmit()
    {
        using var deployed = new TestingWebApplicationFactory();
        using var client = deployed
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production))
            .CreateClient();

        using var response = await client.GetAsync(new Uri(ServedOpenApi.Route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
