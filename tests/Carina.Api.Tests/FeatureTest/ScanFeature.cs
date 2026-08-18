using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.Infrastructure.Scanning;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class ScanFeature : IAsyncDisposable
{
    private readonly TestingWebApplicationFactory factory = new();
    private readonly WebApplicationFactory<Program> configured;

    public ScanFeature()
    {
        Orchestrator = new ScriptedScanOrchestrator(Runs);
        configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAtomicWrite, UnguardedWrites>();
                services.AddSingleton<IChannelScanOrchestrator>(Orchestrator);
                services.AddSingleton<IScanRunRepository>(Runs);
                services.AddSingleton<IBroadcastServiceRepository>(Services);
                services.AddSingleton<ICandidateChannelRepository>(
                    new RefusingCandidates(Candidates, () => WhenACandidateArrives()));
                services.AddSingleton<ISatelliteTransportStreamRepository>(SatelliteStreams);
            }));
        Client = configured.CreateAuthenticatedClient();
    }

    public HttpClient Client { get; }

    public Func<bool> WhenACandidateArrives { get; set; } = () => false;

    public ScriptedScanOrchestrator Orchestrator { get; }

    public HeldScanRuns Runs { get; } = new();

    public HeldServices Services { get; } = new();

    public HeldCandidates Candidates { get; } = new();

    public HeldSatelliteStreams SatelliteStreams { get; } = new();

    public async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path, object? body = null)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            new Uri(path, UriKind.Relative),
            body ?? new { });

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri(path, UriKind.Relative));

        return await ReadAsync(response);
    }

    public async Task<Guid> StartAsync(object? body = null)
    {
        (HttpStatusCode status, JsonElement payload) = await PostAsync("/api/tuners/scan", body);

        Assert.Equal(HttpStatusCode.Accepted, status);

        return payload.GetProperty("data").GetProperty("scanId").GetGuid();
    }

    public Task UntilSettled(Guid scanId)
        => Eventually.Happens(
            () => Runs.Runs.Any(run => run.Id.Value == scanId && !run.IsRunning),
            "the scan leaves Running");

    public async ValueTask DisposeAsync()
    {
        Orchestrator.Release();
        Client.Dispose();
        await factory.DisposeAsync();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(
        HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        if (!body.StartsWith('{') && !body.StartsWith('['))
        {
            return (response.StatusCode, default);
        }

        using var document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.Clone());
    }
}
