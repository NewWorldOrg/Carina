using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Segments;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.Feature.Segments;

internal sealed class SegmentStatusFeature : IAsyncDisposable
{
    public const string Path = "/api/segments/status";

    public static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public SegmentStatusFeature()
    {
        Built = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<ILearningDataAmountReader>(LearningData);
            services.AddSingleton<ICatalogCountsReader>(Catalog);
            services.AddSingleton<ISegmentModelVersionRepository>(Versions);
            services.AddSingleton<ISegmentSettingsRepository>(Settings);
            services.AddSingleton<ISkipEventRepository>(Skips);
            services.AddSingleton<ICatalogEvidenceReader>(Evidence);
            services.AddSingleton<ISegmentLearningRoundRepository>(Rounds);
            services.AddSingleton<TimeProvider>(new MovingClock(Now));
        }));

        Client = Built.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "anything");
    }

    public WebApplicationFactory<Program> Built { get; }

    public HttpClient Client { get; }

    public HeldLearningDataAmount LearningData { get; } = new();

    public HeldCatalogCounts Catalog { get; } = new();

    public HeldSegmentModelVersions Versions { get; } = new();

    public HeldSegmentSettings Settings { get; } = new();

    public HeldSkipEvents Skips { get; } = new();

    public HeldCatalogEvidence Evidence { get; } = new();

    public HeldSegmentLearningRounds Rounds { get; } = new();

    public HttpClient Anonymous() => Built.WithTestScheme().CreateClient();

    public async Task<(HttpStatusCode Status, JsonElement LearningData)> GetAsync()
    {
        (HttpStatusCode status, JsonElement data) = await DataAsync();

        return (status, data.GetProperty("learningData"));
    }

    public async Task<(HttpStatusCode Status, JsonElement Data)> DataAsync()
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri(Path, UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.GetProperty("data").Clone());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }
}
