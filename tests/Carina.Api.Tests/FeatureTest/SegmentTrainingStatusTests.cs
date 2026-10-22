using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

/// <summary>
/// The status of detection beside the day's round of learning, learning on and the machine idle; learning comes
/// to what a test sets.
/// </summary>
internal sealed class SegmentTrainingFeature : IAsyncDisposable
{
    public static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public SegmentTrainingFeature()
    {
        Settings.SaveAsync(SegmentSettings.LearningSwitched(true, Now.AddDays(-30)), CancellationToken.None).GetAwaiter().GetResult();

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
            => Task.FromResult(new Trained(new SegmentLearned(SegmentModel.First, Candidate, InUse, SegmentAdoption.Judge(active, InUse, Candidate)), false));
    }
}

public sealed class SegmentTrainingStatusTests
{
    [Fact(DisplayName = "BR-CD-008: a candidate the day's round puts in use is answered as the version in use, first among the versions, with its figures and the evidence it stands on")]
    public async Task ACandidatePutInUseIsAnsweredWithItsFigures()
    {
        await using var feature = new SegmentTrainingFeature();
        feature.Trainer.Candidate = SegmentTrainingFeature.CannedTrainer.Evaluation(0.93, 0.9, 12);

        SegmentTrainingLook look = await feature.RoundAsync();
        (HttpStatusCode status, JsonElement data) = await feature.StatusAsync();

        Assert.Equal(SegmentTrainingOutcome.Adopted, look.Round?.Outcome);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((2, "active"), (data.GetProperty("version").GetProperty("number").GetInt32(), data.GetProperty("version").GetProperty("state").GetString()));
        JsonElement[] versions = [.. data.GetProperty("versions").EnumerateArray()];
        Assert.Equal([(2, "active"), (1, "retired")], versions.Select(version => (version.GetProperty("number").GetInt32(), version.GetProperty("state").GetString())));
        Assert.Equal(0.93, versions[0].GetProperty("metrics").GetProperty("boundaryRecallWithin2s").GetDouble());
        Assert.Equal(JsonValueKind.Null, versions[1].GetProperty("metrics").ValueKind);
        JsonElement metrics = data.GetProperty("metrics");
        Assert.Equal((0.93, 0.8, 0.9), (metrics.GetProperty("boundaryRecallWithin2s").GetDouble(), metrics.GetProperty("boundaryRecallWithin1s").GetDouble(), metrics.GetProperty("kinds")[0].GetProperty("recall").GetDouble()));
        Assert.Equal(0.93, Assert.Single(data.GetProperty("stations").EnumerateArray()).GetProperty("metrics").GetProperty("boundaryRecallWithin2s").GetDouble());
        Assert.Equal((12, true), (data.GetProperty("evidence").GetProperty("recordings").GetInt32(), data.GetProperty("evidence").GetProperty("enough").GetBoolean()));
        JsonElement round = data.GetProperty("lastRound");
        Assert.Equal(("adopted", 2, "adopted"), (round.GetProperty("outcome").GetString(), round.GetProperty("candidate").GetInt32(), round.GetProperty("verdict").GetString()));
        Assert.Equal((0.85, 0.93, 0.9), (round.GetProperty("inUse").GetProperty("boundaryRecallWithin2s").GetDouble(), round.GetProperty("metrics").GetProperty("boundaryRecallWithin2s").GetDouble(), round.GetProperty("metrics").GetProperty("breakRecall").GetDouble()));
        Assert.Equal((12, true), (round.GetProperty("evidence").GetProperty("recordings").GetInt32(), round.GetProperty("evidence").GetProperty("enough").GetBoolean()));
        Assert.Equal("2026-11-08T18:00:00Z", data.GetProperty("nextRoundNotBefore").GetString());
    }

    [Fact(DisplayName = "BR-CD-008: a candidate the day's round keeps only is answered among the versions with its figures, while the version in use stays and is answered with the figures the round measured it by")]
    public async Task ACandidateKeptOnlyIsAnsweredAmongTheVersions()
    {
        await using var feature = new SegmentTrainingFeature();
        feature.Trainer.Candidate = SegmentTrainingFeature.CannedTrainer.Evaluation(0.80, 0.9, 12);

        SegmentTrainingLook look = await feature.RoundAsync();
        (_, JsonElement data) = await feature.StatusAsync();

        Assert.Equal((SegmentTrainingOutcome.Kept, (AdoptionVerdict?)AdoptionVerdict.NoBetterAtBoundaries), (look.Round?.Outcome, look.Round?.Verdict));
        Assert.Equal(1, data.GetProperty("version").GetProperty("number").GetInt32());
        JsonElement[] versions = [.. data.GetProperty("versions").EnumerateArray()];
        Assert.Equal([(2, "candidate"), (1, "active")], versions.Select(version => (version.GetProperty("number").GetInt32(), version.GetProperty("state").GetString())));
        Assert.Equal(0.80, versions[0].GetProperty("metrics").GetProperty("boundaryRecallWithin2s").GetDouble());
        Assert.Equal(JsonValueKind.Null, versions[0].GetProperty("adoptedAt").ValueKind);
        Assert.Equal(0.85, versions[1].GetProperty("metrics").GetProperty("boundaryRecallWithin2s").GetDouble());
        Assert.Equal(0.85, data.GetProperty("metrics").GetProperty("boundaryRecallWithin2s").GetDouble());
        Assert.Equal(12, data.GetProperty("evidence").GetProperty("recordings").GetInt32());
        JsonElement round = data.GetProperty("lastRound");
        Assert.Equal(("kept", 2, "noBetterAtBoundaries"), (round.GetProperty("outcome").GetString(), round.GetProperty("candidate").GetInt32(), round.GetProperty("verdict").GetString()));
        Assert.Equal((0.85, 0.80), (round.GetProperty("inUse").GetProperty("boundaryRecallWithin2s").GetDouble(), round.GetProperty("metrics").GetProperty("boundaryRecallWithin2s").GetDouble()));
        Assert.Single(feature.Rounds.Taken);
    }
}
