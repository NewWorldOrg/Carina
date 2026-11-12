using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Events;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Scans;
using Carina.Domain.Segments;
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
            services.AddSingleton<IStationLogoRepository>(Logos);
            services.AddSingleton<ICandidateChannelRepository>(Candidates);
            services.AddSingleton<IScanRunRepository>(Runs);
            services.AddSingleton<IRecordingDirectory>(Recordings);
            services.AddSingleton<ISatelliteTransportStreamRepository>(SatelliteStreams);
            services.AddSingleton<IQualityIncidentRepository>(Incidents);
            services.AddSingleton<IQualityLedgerReader>(Ledger);
            services.AddSingleton<IQualityThresholdRepository>(Thresholds);
            services.AddSingleton<IQualityThresholdChangeRepository>(Changes);
            services.AddSingleton<ILearningDataAmountReader>(LearningData);
            services.AddSingleton<ICatalogCountsReader>(Catalog);
            services.AddSingleton<ISegmentModelVersionRepository>(Versions);
        }));

        Client = credentialled ? wired.CreateAuthenticatedClient() : wired.WithTestScheme().CreateClient();
    }

    public HttpClient Client { get; }

    public HeldServices Services { get; } = new();

    public HeldLogos Logos { get; } = new();

    public HeldCandidates Candidates { get; } = new();

    public HeldScanRuns Runs { get; } = new();

    public HeldRecordings Recordings { get; } = new();

    public HeldSatelliteStreams SatelliteStreams { get; } = new();

    public HeldQualityIncidents Incidents { get; } = new();

    public HeldQualityLedger Ledger { get; } = new();

    public HeldQualityThresholds Thresholds { get; } = new();

    public HeldQualityThresholdChanges Changes { get; } = new();

    public HeldLearningDataAmount LearningData { get; } = new();

    public HeldCatalogCounts Catalog { get; } = new();

    public HeldSegmentModelVersions Versions { get; } = new();

    public static SeamProbe CarryingNoCredentials() => new(credentialled: false);

    public static SeamProbe CarryingCredentials() => new(credentialled: true);

    public Task<HttpResponseMessage> GetAsync(string path)
        => Client.GetAsync(new Uri(path, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead);

    public Task<HttpResponseMessage> PostAsync(string path)
        => Client.PostAsJsonAsync(new Uri(path, UriKind.Relative), new { });

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }
}
