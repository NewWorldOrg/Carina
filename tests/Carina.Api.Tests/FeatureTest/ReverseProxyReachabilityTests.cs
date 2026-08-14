using System.Net;
using System.Text.Json;

using Carina.Api.Authentication;
using Carina.Api.Events;
using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class ReachedApi : IAsyncDisposable
{
    private readonly TestingWebApplicationFactory factory = new();
    private readonly WebApplicationFactory<Program> reached;

    private ReachedApi(string address)
    {
        reached = factory
            .ArrivingFrom(address)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IBroadcastServiceRepository>(Services);
                services.AddSingleton<ICandidateChannelRepository>(Candidates);
                services.AddSingleton<IScanRunRepository>(Runs);
                services.AddSingleton<ISatelliteTransportStreamRepository>(SatelliteStreams);
            }));

        Client = reached.CreateClient();
    }

    public HttpClient Client { get; }

    public HeldServices Services { get; } = new();

    public HeldCandidates Candidates { get; } = new();

    public HeldScanRuns Runs { get; } = new();

    public HeldSatelliteStreams SatelliteStreams { get; } = new();

    public static ReachedApi ThroughTheProxy() => new(RequestOrigin.ProxyAddress);

    public static ReachedApi FromTheOpenInternet() => new(RequestOrigin.PublicAddress);

    public Task<HttpResponseMessage> GetAsync(string path)
        => Client.GetAsync(new Uri(path, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await reached.DisposeAsync();
        await factory.DisposeAsync();
    }
}

[Collection(FeatureTestCollection.Name)]
public sealed class ReverseProxyReachabilityTests
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
    public async Task ASurfaceAnswersWhenTheRequestComesFromTheProxyTheDeploymentPutsInFrontOfTheApp(
        string path)
    {
        await using var api = ReachedApi.ThroughTheProxy();

        using var response = await api.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(EveryBusinessSurface))]
    public async Task ASurfaceRefusesWhenTheRequestDidNotComeThroughTheProxy(string path)
    {
        await using var api = ReachedApi.FromTheOpenInternet();

        using var response = await api.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheCatalogArrivesThroughTheProxyRatherThanADenialWithNoWayToSatisfyIt()
    {
        await using var api = ReachedApi.ThroughTheProxy();
        api.Services.Services.Add(BroadcastService.Discover(
            new NetworkId(1),
            new ServiceId(101),
            "Reachable",
            ServiceCategory.Television,
            TunerHoldingDriverClient.At));

        using var response = await api.GetAsync("/api/services");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Reachable",
            body.RootElement.GetProperty("data")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task TheProbeAnswersFromAnywhereBecauseItIsTheOneSurfaceThatIsNotBehindTheProxy()
    {
        await using var api = ReachedApi.FromTheOpenInternet();

        using var response = await api.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AForwardedHeaderClaimingTheProxyAddressDoesNotStandInForArrivingFromIt()
    {
        await using var api = ReachedApi.FromTheOpenInternet();
        api.Client.DefaultRequestHeaders.Add("X-Forwarded-For", RequestOrigin.ProxyAddress);

        using var response = await api.GetAsync("/api/driver/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TheProxyAllowanceEndsAsSoonAsTheAuthenticationDomainRegistersAScheme()
    {
        using var factory = new TestingWebApplicationFactory();
        using var behindTheProxy = factory.BehindTheProxy().WithTestScheme();
        using var client = behindTheProxy.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/driver/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ARequestWithCredentialsIsStillAnsweredOnceASchemeExists()
    {
        using var factory = new TestingWebApplicationFactory();
        using var client = factory.BehindTheProxy().CreateAuthenticatedClient();

        using var response = await client.GetAsync(new Uri("/api/driver/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
