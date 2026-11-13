using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.Feature.Segments;

/// <summary>
/// The status of detection beside the day's round of learning, the machine idle; learning comes to what a test
/// sets.
/// </summary>
internal sealed class SegmentTrainingFeature : IAsyncDisposable
{
    public static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public SegmentTrainingFeature()
    {
        Built = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<TimeProvider>(new MovingClock(Now));
            services.AddSingleton<ILearningDataAmountReader>(new HeldLearningDataAmount());
            services.AddSingleton<ICatalogCountsReader>(new HeldCatalogCounts());
            services.AddSingleton<ISegmentModelVersionRepository>(Versions);
            services.AddSingleton<ISegmentSettingsRepository>(Settings);
            services.AddSingleton<ISkipEventRepository>(new HeldSkipEvents());
            services.AddSingleton<ICatalogEvidenceReader>(new HeldCatalogEvidence());
            services.AddSingleton<ISegmentLearningRoundRepository>(Rounds);
            services.AddSingleton<ILearningMaterialChanges>(new HeldLearningMaterialChanges());
            services.AddSingleton<IOccupancyReader>(new SegmentVersionFeature.Unoccupied());
            services.AddSingleton<ISegmentTrainer>(Trainer);
        }));

        Client = Built.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "anything");
    }

    public WebApplicationFactory<Program> Built { get; }

    public HttpClient Client { get; }

    public HeldSegmentModelVersions Versions { get; } = new();

    public HeldSegmentSettings Settings { get; } = new();

    public HeldSegmentLearningRounds Rounds { get; } = new();

    public CannedTrainer Trainer { get; } = new();

    public async Task<SegmentTrainingLook> RoundAsync()
        => await Built.Services.GetRequiredService<SegmentTrainingJob>().LookAsync(CancellationToken.None);

    public async Task<(HttpStatusCode Status, JsonElement Data)> StatusAsync()
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri(SegmentStatusFeature.Path, UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.GetProperty("data").Clone());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }

    /// <summary>
    /// Learning that comes to the figures a test sets for the candidate and for the version in use, judged as
    /// learning judges them.
    /// </summary>
    internal sealed class CannedTrainer : ISegmentTrainer
    {
        public SegmentEvaluation Candidate { get; set; } = Evaluation(0.85, 0.9, 12);

        public SegmentEvaluation InUse { get; set; } = Evaluation(0.85, 0.9, 12);

        public static SegmentEvaluation Evaluation(double recall, double seconds, int recordings)
            => new(
                new SegmentMetrics(recall, 0.8, [new KindMetrics(SegmentKind.Break, seconds)], null, 0),
                [new StationMetrics(new Carina.Domain.Channels.NetworkId(1), new Carina.Domain.Channels.ServiceId(1), recordings, new SegmentMetrics(recall, 0.8, [], null, 0))],
                new EvaluationEvidence(recordings, 148, Now.AddDays(-1)));

        public Task<Trained> TrainAsync(
            SegmentModelVersion active,
            DateTime now,
            Func<CancellationToken, Task<bool>> mayGoOn,
            CancellationToken cancellationToken)
            => Task.FromResult(new Trained(new SegmentLearned(HandMadeModels.First, Candidate, InUse, SegmentAdoption.Judge(active, InUse, Candidate)), false));
    }
}
