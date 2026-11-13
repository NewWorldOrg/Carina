using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.Feature.Segments;

internal sealed class SegmentVersionFeature : IAsyncDisposable
{
    public const string Path = "/api/segments/version";

    public static readonly DateTime Trained = new(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public SegmentVersionFeature()
    {
        Versions.Others.Add(SegmentModelVersion.Rehydrate(2, SegmentModelVersionState.Candidate, false, HandMadeModels.First, Trained, null, null, null, null));

        Built = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<TimeProvider>(new MovingClock(Now));
            services.AddSingleton<ISegmentModelVersionRepository>(Versions);
            services.AddSingleton<IOccupancyReader>(new Unoccupied());
            services.AddSingleton<IDetectionWorklist>(Worklist);
        }));

        Client = Built.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "anything");
    }

    public WebApplicationFactory<Program> Built { get; }

    public HttpClient Client { get; }

    public HeldSegmentModelVersions Versions { get; } = new();

    public AskedWorklist Worklist { get; } = new();

    public HttpClient Anonymous() => Built.WithTestScheme().CreateClient();

    public async Task<(HttpStatusCode Status, JsonElement Body)> PatchAsync(string json)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Client.PatchAsync(new Uri(Path, UriKind.Relative), content);
        string body = await response.Content.ReadAsStringAsync();

        if (!body.StartsWith('{'))
        {
            return (response.StatusCode, default);
        }

        using var document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.Clone());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }

    internal sealed class Unoccupied : IOccupancyReader
    {
        public Task<Occupancy> ReadAsync(DateTime now, CancellationToken cancellationToken)
            => Task.FromResult(new Occupancy(false, false, null));
    }

    /// <summary>
    /// Keeps which version each look of the detection asked a waiting recording for, and has none waiting.
    /// </summary>
    internal sealed class AskedWorklist : IDetectionWorklist
    {
        public List<int> Asked { get; } = [];

        public Task<LearningExtraction?> NextAsync(int modelVersion, IReadOnlyCollection<RecordingId> passedOver, CancellationToken cancellationToken)
        {
            Asked.Add(modelVersion);

            return Task.FromResult<LearningExtraction?>(null);
        }

        public Task<IReadOnlyList<LearningExtraction>> EarlierOnTheStationAsync(SeriesMember judged, int most, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LearningExtraction>>([]);

        public Task<IReadOnlyList<LearningExtraction>> RecordsAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LearningExtraction>>([]);

        public Task<IReadOnlyList<LearningDataPart>> PartsAsync(RecordingId recording, IReadOnlyCollection<LearningDataKind> kinds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LearningDataPart>>([]);
    }
}
