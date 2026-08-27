using System.Net;
using System.Text.Json;

using Carina.Api.Events;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
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

    private SeamProbe(bool credentialled)
    {
        WebApplicationFactory<Program> wired = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IBroadcastServiceRepository>(Services);
            services.AddSingleton<ICandidateChannelRepository>(Candidates);
            services.AddSingleton<IScanRunRepository>(Runs);
            services.AddSingleton<IRecordingDirectory>(Recordings);
            services.AddSingleton<ISatelliteTransportStreamRepository>(SatelliteStreams);
        }));

        Client = credentialled ? wired.CreateAuthenticatedClient() : wired.WithTestScheme().CreateClient();
    }

    public HttpClient Client { get; }

    public HeldServices Services { get; } = new();

    public HeldCandidates Candidates { get; } = new();

    public HeldScanRuns Runs { get; } = new();

    public HeldRecordings Recordings { get; } = new();

    public HeldSatelliteStreams SatelliteStreams { get; } = new();

    public static SeamProbe CarryingNoCredentials() => new(credentialled: false);

    public static SeamProbe CarryingCredentials() => new(credentialled: true);

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
        "/api/tuners/health",
        "/api/tuners/scan-runs",
        "/api/services",
        "/api/recordings",
        AppEventStream.Path,
    ];

    [Theory]
    [MemberData(nameof(EveryBusinessSurface))]
    public async Task ASurfaceRefusesAClientCarryingNoCredentials(string path)
    {
        await using var probe = SeamProbe.CarryingNoCredentials();

        using HttpResponseMessage response = await probe.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [MemberData(nameof(EveryBusinessSurface))]
    public async Task ASurfaceAnswersAClientCarryingCredentials(string path)
    {
        await using var probe = SeamProbe.CarryingCredentials();

        using HttpResponseMessage response = await probe.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TheCatalogArrivesForACallerThatHasSignedIn()
    {
        await using var probe = SeamProbe.CarryingCredentials();
        probe.Services.Services.Add(BroadcastService.Discover(
            new NetworkId(1),
            new ServiceId(101),
            "Reachable",
            ServiceCategory.Television,
            TunerHoldingDriverClient.At));

        using HttpResponseMessage response = await probe.GetAsync("/api/services");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Reachable",
            body.RootElement.GetProperty("data")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AnUnknownPathReachesRoutingOnceTheCallerHasSignedIn()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AWrongMethodOnAKnownPathReachesRoutingOnceTheCallerHasSignedIn()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.PostAsync(
            new Uri("/api/driver/status", UriKind.Relative),
            content: null
        );

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownPathIsRefusedBeforeRoutingHasAnEndpointToAnswer404About()
    {
        using HttpClient client = factory.WithTestScheme().CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AWrongMethodOnAKnownPathIsRefusedBeforeRoutingHasAnEndpointToAnswer405About()
    {
        using HttpClient client = factory.WithTestScheme().CreateClient();

        using HttpResponseMessage response = await client.PostAsync(
            new Uri("/api/driver/status", UriKind.Relative),
            content: null
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheDocumentIsHandedOutWithoutCredentialsInDevelopmentBecauseTheClientIsGeneratedFromIt()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(ServedOpenApi.Route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheDocumentIsNotServedAtAllOutsideDevelopmentRatherThanLeftToASeamThatMayAdmit()
    {
        using var deployed = new TestingWebApplicationFactory();
        using HttpClient client = deployed
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production))
            .CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(ServedOpenApi.Route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheDocumentIsRefusedOutsideDevelopmentToACallerCarryingNoCredentials()
    {
        using var deployed = new TestingWebApplicationFactory();
        using HttpClient client = deployed
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production))
            .CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(ServedOpenApi.Route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
