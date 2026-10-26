using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

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

public sealed class SegmentStatusEndpointTests
{
    [Fact(DisplayName = "with no learning data kept every figure of it is zero, and only the figures are answered")]
    public async Task WithNoLearningDataKeptEveryFigureIsZero()
    {
        await using var feature = new SegmentStatusFeature();

        (HttpStatusCode status, JsonElement learningData) = await feature.GetAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["bytes", "recordings", "seconds", "waiting"],
            learningData.EnumerateObject().Select(figure => figure.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(0, learningData.GetProperty("recordings").GetInt32());
        Assert.Equal(0, learningData.GetProperty("seconds").GetInt64());
        Assert.Equal(0, learningData.GetProperty("bytes").GetInt64());
        Assert.Equal(0, learningData.GetProperty("waiting").GetInt32());
        Assert.Equal(1, feature.LearningData.Reads);
    }

    [Fact(DisplayName = "the learning data kept is answered as recordings, whole seconds read and the bytes the tables take")]
    public async Task TheLearningDataKeptIsAnsweredAsRecordingsSecondsAndBytes()
    {
        await using var feature = new SegmentStatusFeature();
        feature.LearningData.Amount = new LearningDataAmount(3, TimeSpan.FromSeconds(5_400.75), 5_000_000_123, 0);

        (HttpStatusCode status, JsonElement learningData) = await feature.GetAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, learningData.GetProperty("recordings").GetInt32());
        Assert.Equal(5_400, learningData.GetProperty("seconds").GetInt64());
        Assert.Equal(5_000_000_123, learningData.GetProperty("bytes").GetInt64());
        Assert.Equal(0, learningData.GetProperty("waiting").GetInt32());
    }

    [Fact(DisplayName = "recordings still waiting to be read are answered beside the learning data kept")]
    public async Task RecordingsStillWaitingToBeReadAreAnswered()
    {
        await using var feature = new SegmentStatusFeature();
        feature.LearningData.Amount = new LearningDataAmount(1, TimeSpan.FromMinutes(30), 4_096, 12);

        (HttpStatusCode status, JsonElement learningData) = await feature.GetAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, learningData.GetProperty("recordings").GetInt32());
        Assert.Equal(1_800, learningData.GetProperty("seconds").GetInt64());
        Assert.Equal(12, learningData.GetProperty("waiting").GetInt32());
    }

    [Fact(DisplayName = "with an empty catalog every kind counts zero, and only the six kinds are answered")]
    public async Task WithAnEmptyCatalogEveryKindCountsZero()
    {
        await using var feature = new SegmentStatusFeature();

        (HttpStatusCode status, JsonElement data) = await feature.DataAsync();
        JsonElement catalog = data.GetProperty("catalog");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["catalog", "evidence", "lastRound", "learningData", "metrics", "nextRoundNotBefore", "skipThreshold", "stations", "version", "versions"],
            data.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["cm", "ending", "opening", "programmeFixture", "stationFixture", "unknown"],
            catalog.EnumerateObject().Select(kind => kind.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.All(catalog.EnumerateObject(), kind => Assert.Equal(0, kind.Value.GetInt32()));
        Assert.Equal(1, feature.Catalog.Reads);
    }

    [Fact(DisplayName = "the catalog is answered as the entries of each kind it holds")]
    public async Task TheCatalogIsAnsweredByKind()
    {
        await using var feature = new SegmentStatusFeature();
        feature.Catalog.Counts = new CatalogCounts(820, 0, 0, 40, 6, 133);

        (HttpStatusCode status, JsonElement data) = await feature.DataAsync();
        JsonElement catalog = data.GetProperty("catalog");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(820, catalog.GetProperty("cm").GetInt32());
        Assert.Equal(0, catalog.GetProperty("opening").GetInt32());
        Assert.Equal(0, catalog.GetProperty("ending").GetInt32());
        Assert.Equal(40, catalog.GetProperty("programmeFixture").GetInt32());
        Assert.Equal(6, catalog.GetProperty("stationFixture").GetInt32());
        Assert.Equal(133, catalog.GetProperty("unknown").GetInt32());
    }

    [Fact(DisplayName = "BR-CS-003: the version in use is answered by its number, where it stands, whether it was chosen by hand, and when it was made and put in use")]
    public async Task TheVersionInUseIsAnswered()
    {
        await using var feature = new SegmentStatusFeature();

        (HttpStatusCode status, JsonElement data) = await feature.DataAsync();
        JsonElement version = data.GetProperty("version");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["adoptedAt", "byHand", "number", "state", "trainedAt"],
            version.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(1, version.GetProperty("number").GetInt32());
        Assert.Equal("active", version.GetProperty("state").GetString());
        Assert.False(version.GetProperty("byHand").GetBoolean());
        Assert.Equal("2026-10-18T00:00:00Z", version.GetProperty("trainedAt").GetString());
        Assert.Equal("2026-10-18T00:00:00Z", version.GetProperty("adoptedAt").GetString());
        Assert.Equal(1, feature.Versions.Reads);
    }

    [Fact(DisplayName = "a version chosen by hand is answered as such, with when it was made and put in use")]
    public async Task AVersionChosenByHandIsAnswered()
    {
        await using var feature = new SegmentStatusFeature();
        DateTime trained = new(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc);
        feature.Versions.Active = SegmentModelVersion.Rehydrate(3, SegmentModelVersionState.Active, true, HandMadeModels.First, trained, trained.AddMinutes(5), null, trained.AddDays(1), null);

        (_, JsonElement data) = await feature.DataAsync();
        JsonElement version = data.GetProperty("version");

        Assert.Equal(3, version.GetProperty("number").GetInt32());
        Assert.True(version.GetProperty("byHand").GetBoolean());
        Assert.Equal("2026-11-01T18:00:00Z", version.GetProperty("trainedAt").GetString());
        Assert.Equal("2026-11-01T18:05:00Z", version.GetProperty("adoptedAt").GetString());
    }

    [Fact(DisplayName = "BR-CD-008: with no version evaluated, the evidence is what the catalog as it stands gives, there are no figures, and the threshold stands as the settings hold it")]
    public async Task WithNoVersionEvaluatedTheEvidenceIsWhatTheCatalogGives()
    {
        await using var feature = new SegmentStatusFeature();
        feature.Evidence.Evidence = new EvaluationEvidence(4, 40, null);

        (HttpStatusCode status, JsonElement data) = await feature.DataAsync();
        JsonElement evidence = data.GetProperty("evidence");
        JsonElement version = Assert.Single(data.GetProperty("versions").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["recordings", "boundaries", "enough", "recordingsNeeded", "boundariesNeeded"], Names(evidence));
        Assert.Equal((4, 40, false, 10, 30), (evidence.GetProperty("recordings").GetInt32(), evidence.GetProperty("boundaries").GetInt32(), evidence.GetProperty("enough").GetBoolean(), evidence.GetProperty("recordingsNeeded").GetInt32(), evidence.GetProperty("boundariesNeeded").GetInt32()));
        Assert.Equal(1, feature.Evidence.Reads);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("metrics").ValueKind);
        Assert.Empty(data.GetProperty("stations").EnumerateArray());
        Assert.Equal(["number", "state", "byHand", "trainedAt", "adoptedAt", "retiredAt", "rolledBack", "metrics"], Names(version));
        Assert.Equal((1, "active", "2026-10-18T00:00:00Z"), (version.GetProperty("number").GetInt32(), version.GetProperty("state").GetString(), version.GetProperty("adoptedAt").GetString()));
        Assert.Equal(JsonValueKind.Null, version.GetProperty("rolledBack").ValueKind);
        Assert.Equal(JsonValueKind.Null, version.GetProperty("metrics").ValueKind);
        Assert.Equal(["current"], Names(data.GetProperty("skipThreshold")));
        Assert.Equal(0.70, data.GetProperty("skipThreshold").GetProperty("current").GetDouble());
    }

    [Fact(DisplayName = "BR-CD-008: with the version in use evaluated, its figures are answered beside what viewers did with its buttons over the last seven days, by station, with every version's history, and the catalog is not asked")]
    public async Task WithTheVersionInUseEvaluatedItsFiguresAreAnswered()
    {
        await using var feature = new SegmentStatusFeature();
        DateTime trained = new(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc);
        feature.Versions.Active = Version(3, SegmentModelVersionState.Active, trained, trained.AddMinutes(5), null, Evaluation(0.93, 12));
        feature.Versions.Others.Add(Version(4, SegmentModelVersionState.Candidate, trained.AddDays(1), null, null, Evaluation(0.95, 13)));
        feature.Versions.Others.Add(Version(2, SegmentModelVersionState.Retired, trained.AddDays(-7), trained.AddDays(-7), trained.AddMinutes(5), Evaluation(0.9, 11), new SegmentRollback(0.09, 0.03, 25, 1)));
        feature.Versions.Others.Add(SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Retired, false, HandMadeModels.First, HeldSegmentModelVersions.Installed, HeldSegmentModelVersions.Installed, trained.AddDays(-7), null, null));
        await feature.Settings.SaveAsync(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, 0.6, trained), CancellationToken.None);
        feature.Evidence.Evidence = new EvaluationEvidence(1, 10, null);
        await SkippedAsync(feature, 3, ViewerActionKind.Skipped, 64, SegmentStatusFeature.Now.AddDays(-1));
        await SkippedAsync(feature, 3, ViewerActionKind.Returned, 2, SegmentStatusFeature.Now.AddDays(-6));
        await SkippedAsync(feature, 3, ViewerActionKind.Dismissed, 5, SegmentStatusFeature.Now.AddDays(-1));
        await SkippedAsync(feature, 3, ViewerActionKind.Returned, 9, SegmentStatusFeature.Now.AddDays(-8));
        await SkippedAsync(feature, 2, ViewerActionKind.Returned, 9, SegmentStatusFeature.Now.AddDays(-1));

        (HttpStatusCode status, JsonElement data) = await feature.DataAsync();
        JsonElement metrics = data.GetProperty("metrics");
        JsonElement station = Assert.Single(data.GetProperty("stations").EnumerateArray());
        JsonElement[] versions = [.. data.GetProperty("versions").EnumerateArray()];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, data.GetProperty("version").GetProperty("number").GetInt32());
        Assert.Equal(["recordings", "boundaries", "enough", "recordingsNeeded", "boundariesNeeded"], Names(data.GetProperty("evidence")));
        Assert.Equal((13, 150, true), (data.GetProperty("evidence").GetProperty("recordings").GetInt32(), data.GetProperty("evidence").GetProperty("boundaries").GetInt32(), data.GetProperty("evidence").GetProperty("enough").GetBoolean()));
        Assert.Equal(0, feature.Evidence.Reads);
        Assert.Equal(["boundaryRecallWithin2s", "boundaryRecallWithin1s", "kinds", "returnedAfterSkipShare", "skips"], Names(metrics));
        Assert.Equal((0.93, 0.85), (metrics.GetProperty("boundaryRecallWithin2s").GetDouble(), metrics.GetProperty("boundaryRecallWithin1s").GetDouble()));
        JsonElement kind = Assert.Single(metrics.GetProperty("kinds").EnumerateArray());
        Assert.Equal(["kind", "recall"], Names(kind));
        Assert.Equal(("break", 0.95), (kind.GetProperty("kind").GetString(), kind.GetProperty("recall").GetDouble()));
        Assert.Equal((2 / 64d, 64), (metrics.GetProperty("returnedAfterSkipShare").GetDouble(), metrics.GetProperty("skips").GetInt32()));
        Assert.Equal(["networkId", "serviceId", "recordings", "metrics"], Names(station));
        Assert.Equal((1, 2, 5), (station.GetProperty("networkId").GetInt32(), station.GetProperty("serviceId").GetInt32(), station.GetProperty("recordings").GetInt32()));
        Assert.Equal(["boundaryRecallWithin2s"], Names(station.GetProperty("metrics")));
        Assert.Equal(0.96, station.GetProperty("metrics").GetProperty("boundaryRecallWithin2s").GetDouble());
        Assert.Equal([4, 3, 2, 1], versions.Select(version => version.GetProperty("number").GetInt32()));
        Assert.Equal(["candidate", "active", "retired", "retired"], versions.Select(version => version.GetProperty("state").GetString()));
        Assert.Equal(JsonValueKind.Null, versions[0].GetProperty("adoptedAt").ValueKind);
        Assert.Equal(["boundaryRecallWithin2s"], Names(versions[1].GetProperty("metrics")));
        Assert.Equal(0.93, versions[1].GetProperty("metrics").GetProperty("boundaryRecallWithin2s").GetDouble());
        Assert.Equal(["at", "returnedAfterSkipShare", "previousShare", "skips"], Names(versions[2].GetProperty("rolledBack")));
        Assert.Equal(("2026-11-01T18:05:00Z", 0.09, 0.03, 25), (versions[2].GetProperty("rolledBack").GetProperty("at").GetString(), versions[2].GetProperty("rolledBack").GetProperty("returnedAfterSkipShare").GetDouble(), versions[2].GetProperty("rolledBack").GetProperty("previousShare").GetDouble(), versions[2].GetProperty("rolledBack").GetProperty("skips").GetInt32()));
        Assert.Equal("2026-11-01T18:05:00Z", versions[2].GetProperty("retiredAt").GetString());
        Assert.Equal(JsonValueKind.Null, versions[3].GetProperty("metrics").ValueKind);
        Assert.Equal(JsonValueKind.Null, versions[3].GetProperty("rolledBack").ValueKind);
        Assert.Equal(["current"], Names(data.GetProperty("skipThreshold")));
        Assert.Equal(0.6, data.GetProperty("skipThreshold").GetProperty("current").GetDouble());
    }

    [Fact(DisplayName = "BR-CD-008: a version in use that tells openings and endings apart answers a row of figures for breaks, openings and endings, null where the catalog was sure of none, and leaves the summaries by station and by version as they were")]
    public async Task AVersionThatTellsOpeningsAndEndingsApartAnswersARowForEachKind()
    {
        await using var feature = new SegmentStatusFeature();
        DateTime trained = new(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc);
        SegmentMetrics metrics = new(
            0.93,
            0.85,
            [new KindMetrics(SegmentKind.Break, 0.95), new KindMetrics(SegmentKind.Opening, 0.88), new KindMetrics(SegmentKind.Ending, null)],
            0.5,
            4);
        feature.Versions.Active = Version(
            2,
            SegmentModelVersionState.Active,
            trained,
            trained,
            null,
            new SegmentEvaluation(metrics, [new StationMetrics(new NetworkId(1), new ServiceId(2), 5, metrics)], new EvaluationEvidence(12, 150, null)));
        feature.Versions.Others.Add(SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Retired, false, HandMadeModels.First, HeldSegmentModelVersions.Installed, HeldSegmentModelVersions.Installed, trained, null, null));

        (HttpStatusCode status, JsonElement data) = await feature.DataAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [("break", (double?)0.95), ("opening", 0.88), ("ending", null)],
            data.GetProperty("metrics").GetProperty("kinds").EnumerateArray().Select(kind => (kind.GetProperty("kind").GetString(), Figure(kind.GetProperty("recall")))));
        Assert.Equal(["boundaryRecallWithin2s"], Names(data.GetProperty("stations")[0].GetProperty("metrics")));
        Assert.Equal(["boundaryRecallWithin2s"], Names(data.GetProperty("versions")[0].GetProperty("metrics")));
    }

    [Fact(DisplayName = "BR-CD-008: a version in use evaluated on no recording has no figures, and its evidence still stands")]
    public async Task AVersionInUseEvaluatedOnNoRecordingHasNoFigures()
    {
        await using var feature = new SegmentStatusFeature();
        DateTime trained = new(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc);
        feature.Versions.Active = Version(2, SegmentModelVersionState.Active, trained, trained, null, new SegmentEvaluation(null, [], new EvaluationEvidence(0, 0, null)));
        feature.Versions.Others.Add(SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Retired, false, HandMadeModels.First, HeldSegmentModelVersions.Installed, HeldSegmentModelVersions.Installed, trained, null, null));
        feature.Evidence.Evidence = new EvaluationEvidence(2, 20, null);
        await SkippedAsync(feature, 2, ViewerActionKind.Skipped, 3, SegmentStatusFeature.Now.AddDays(-1));

        (_, JsonElement data) = await feature.DataAsync();

        Assert.Equal(JsonValueKind.Null, data.GetProperty("metrics").ValueKind);
        Assert.Equal(0, data.GetProperty("evidence").GetProperty("recordings").GetInt32());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("versions")[0].GetProperty("metrics").ValueKind);
    }

    [Fact(DisplayName = "BR-CD-008: a version in use that was never evaluated itself is answered with the figures the latest round measured it by, in its figures and in its row of the history")]
    public async Task AVersionInUseNeverEvaluatedIsAnsweredWithTheFiguresOfTheLatestRound()
    {
        await using SegmentStatusFeature feature = new();
        DateTime taken = new(2026, 11, 7, 18, 6, 0, DateTimeKind.Utc);
        await feature.Rounds.TakeAsync(
            SegmentLearningRound.Ended(new DateOnly(2026, 11, 8), taken, SegmentTrainingOutcome.Kept, 5, AdoptionVerdict.TooLittleEvidence, new SegmentMetrics(0.08, 0.05, [new KindMetrics(SegmentKind.Break, 0.38)], null, 0), SegmentModelVersion.FirstNumber),
            CancellationToken.None);

        (HttpStatusCode status, JsonElement data) = await feature.DataAsync();
        JsonElement metrics = data.GetProperty("metrics");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((0.08, 0.05), (metrics.GetProperty("boundaryRecallWithin2s").GetDouble(), metrics.GetProperty("boundaryRecallWithin1s").GetDouble()));
        Assert.Equal(0.38, Assert.Single(metrics.GetProperty("kinds").EnumerateArray()).GetProperty("recall").GetDouble());
        Assert.Equal(0.08, Assert.Single(data.GetProperty("versions").EnumerateArray()).GetProperty("metrics").GetProperty("boundaryRecallWithin2s").GetDouble());
    }

    [Fact(DisplayName = "BR-CD-004: before any round of learning there is no last round, and the next may begin at three in the morning in Japan of the day it is")]
    public async Task BeforeAnyRoundThereIsNoLastRound()
    {
        await using SegmentStatusFeature feature = new();

        (HttpStatusCode status, JsonElement data) = await feature.DataAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("lastRound").ValueKind);
        Assert.Equal("2026-11-07T18:00:00Z", data.GetProperty("nextRoundNotBefore").GetString());
    }

    [Fact(DisplayName = "BR-CD-008: a round that kept a candidate on too little evidence is answered with what became of it, the verdict, the figures of the version in use beside it, and the candidate's own evidence and figures")]
    public async Task ARoundThatKeptACandidateIsAnsweredWithHowItWasJudged()
    {
        await using SegmentStatusFeature feature = new();
        DateTime taken = new(2026, 11, 7, 18, 6, 0, DateTimeKind.Utc);
        feature.Versions.Others.Add(Version(
            5,
            SegmentModelVersionState.Candidate,
            taken.AddMinutes(20),
            null,
            null,
            new SegmentEvaluation(new SegmentMetrics(0.11, 0.07, [new KindMetrics(SegmentKind.Break, 0.42)], null, 0), [], new EvaluationEvidence(12, 26, null))));
        await feature.Rounds.TakeAsync(
            SegmentLearningRound.Ended(new DateOnly(2026, 11, 8), taken, SegmentTrainingOutcome.Kept, 5, AdoptionVerdict.TooLittleEvidence, new SegmentMetrics(0.08, 0.05, [new KindMetrics(SegmentKind.Break, 0.38)], null, 0)),
            CancellationToken.None);

        (HttpStatusCode status, JsonElement data) = await feature.DataAsync();
        JsonElement round = data.GetProperty("lastRound");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["day", "takenAt", "outcome", "candidate", "verdict", "inUse", "evidence", "metrics"], Names(round));
        Assert.Equal(
            ("2026-11-08", "2026-11-07T18:06:00Z", "kept", 5, "tooLittleEvidence"),
            (round.GetProperty("day").GetString(), round.GetProperty("takenAt").GetString(), round.GetProperty("outcome").GetString(), round.GetProperty("candidate").GetInt32(), round.GetProperty("verdict").GetString()));
        Assert.Equal(["boundaryRecallWithin2s", "breakRecall"], Names(round.GetProperty("inUse")));
        Assert.Equal((0.08, 0.38), (round.GetProperty("inUse").GetProperty("boundaryRecallWithin2s").GetDouble(), round.GetProperty("inUse").GetProperty("breakRecall").GetDouble()));
        Assert.Equal(["recordings", "boundaries", "enough", "recordingsNeeded", "boundariesNeeded"], Names(round.GetProperty("evidence")));
        Assert.Equal((12, 26, false), (round.GetProperty("evidence").GetProperty("recordings").GetInt32(), round.GetProperty("evidence").GetProperty("boundaries").GetInt32(), round.GetProperty("evidence").GetProperty("enough").GetBoolean()));
        Assert.Equal((0.11, 0.42), (round.GetProperty("metrics").GetProperty("boundaryRecallWithin2s").GetDouble(), round.GetProperty("metrics").GetProperty("breakRecall").GetDouble()));
        Assert.Equal("2026-11-08T18:00:00Z", data.GetProperty("nextRoundNotBefore").GetString());
    }

    [Fact(DisplayName = "BR-CS-003: a round whose candidate learning let go is answered with the candidate's evidence and figures as the round kept them, under the number it was given, and the versions answered keep their numbers")]
    public async Task ARoundWhoseCandidateWasLetGoIsAnsweredFromTheRound()
    {
        await using SegmentStatusFeature feature = new();
        DateTime taken = new(2026, 11, 7, 18, 6, 0, DateTimeKind.Utc);
        feature.Versions.Others.Add(Version(7, SegmentModelVersionState.Candidate, taken.AddDays(-1), null, null, Evaluation(0.2, 3)));
        await feature.Rounds.TakeAsync(
            SegmentLearningRound.Ended(
                new DateOnly(2026, 11, 8),
                taken,
                SegmentTrainingOutcome.Kept,
                9,
                AdoptionVerdict.NoBetterAtBoundaries,
                new SegmentMetrics(0.14, 0.1, [new KindMetrics(SegmentKind.Break, 0.4)], null, 0),
                SegmentModelVersion.FirstNumber,
                new SegmentMetrics(0.11, 0.07, [new KindMetrics(SegmentKind.Break, 0.42)], null, 0),
                new EvaluationEvidence(12, 56, null)),
            CancellationToken.None);

        (HttpStatusCode status, JsonElement data) = await feature.DataAsync();
        JsonElement round = data.GetProperty("lastRound");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((9, "noBetterAtBoundaries"), (round.GetProperty("candidate").GetInt32(), round.GetProperty("verdict").GetString()));
        Assert.Equal((0.11, 0.42), (round.GetProperty("metrics").GetProperty("boundaryRecallWithin2s").GetDouble(), round.GetProperty("metrics").GetProperty("breakRecall").GetDouble()));
        Assert.Equal((0.14, 0.4), (round.GetProperty("inUse").GetProperty("boundaryRecallWithin2s").GetDouble(), round.GetProperty("inUse").GetProperty("breakRecall").GetDouble()));
        Assert.Equal((12, 56), (round.GetProperty("evidence").GetProperty("recordings").GetInt32(), round.GetProperty("evidence").GetProperty("boundaries").GetInt32()));
        Assert.Equal([7, 1], data.GetProperty("versions").EnumerateArray().Select(version => version.GetProperty("number").GetInt32()));
    }

    [Fact(DisplayName = "BR-CS-003: a round kept before rounds kept their candidate's figures is answered with them from the candidate while it is still kept")]
    public async Task ARoundKeptBeforeItsCandidatesFiguresIsAnsweredFromTheCandidate()
    {
        await using SegmentStatusFeature feature = new();
        DateTime taken = new(2026, 11, 7, 18, 6, 0, DateTimeKind.Utc);
        feature.Versions.Others.Add(Version(9, SegmentModelVersionState.Candidate, taken.AddMinutes(20), null, null, Evaluation(0.3, 12)));
        await feature.Rounds.TakeAsync(
            SegmentLearningRound.Ended(new DateOnly(2026, 11, 8), taken, SegmentTrainingOutcome.Kept, 9, AdoptionVerdict.NoBetterAtBoundaries, new SegmentMetrics(0.4, 0.3, [], null, 0), SegmentModelVersion.FirstNumber),
            CancellationToken.None);

        (_, JsonElement data) = await feature.DataAsync();
        JsonElement round = data.GetProperty("lastRound");

        Assert.Equal((0.3, 12), (round.GetProperty("metrics").GetProperty("boundaryRecallWithin2s").GetDouble(), round.GetProperty("evidence").GetProperty("recordings").GetInt32()));
    }

    [Theory(DisplayName = "BR-CD-004: a round that learned nothing, or failed, is answered with what became of it and nothing of a candidate")]
    [InlineData(SegmentTrainingOutcome.NothingNew, "nothingNew")]
    [InlineData(SegmentTrainingOutcome.NothingToLearnFrom, "nothingToLearnFrom")]
    [InlineData(SegmentTrainingOutcome.Failed, "failed")]
    public async Task ARoundThatLearnedNothingIsAnsweredWithoutACandidate(SegmentTrainingOutcome outcome, string said)
    {
        await using SegmentStatusFeature feature = new();
        await feature.Rounds.TakeAsync(SegmentLearningRound.Ended(new DateOnly(2026, 11, 7), new DateTime(2026, 11, 6, 18, 2, 0, DateTimeKind.Utc), outcome), CancellationToken.None);

        (_, JsonElement data) = await feature.DataAsync();
        JsonElement round = data.GetProperty("lastRound");

        Assert.Equal(("2026-11-07", said), (round.GetProperty("day").GetString(), round.GetProperty("outcome").GetString()));
        Assert.All(
            ["candidate", "verdict", "inUse", "evidence", "metrics"],
            field => Assert.Equal(JsonValueKind.Null, round.GetProperty(field).ValueKind));
        Assert.Equal("2026-11-07T18:00:00Z", data.GetProperty("nextRoundNotBefore").GetString());
    }

    [Fact(DisplayName = "BR-CD-004: a round kept before what became of rounds was kept is not answered, and still counts as the day's round for when the next may begin")]
    public async Task ARoundKeptBeforeOutcomesWereKeptIsNotAnswered()
    {
        await using SegmentStatusFeature feature = new();
        await feature.Rounds.TakeAsync(SegmentLearningRound.Ended(new DateOnly(2026, 11, 7), new DateTime(2026, 11, 6, 18, 2, 0, DateTimeKind.Utc), SegmentTrainingOutcome.NothingNew), CancellationToken.None);
        await feature.Rounds.TakeAsync(SegmentLearningRound.Taken(new DateTime(2026, 11, 7, 18, 4, 0, DateTimeKind.Utc), 2), CancellationToken.None);

        (_, JsonElement data) = await feature.DataAsync();

        Assert.Equal("2026-11-07", data.GetProperty("lastRound").GetProperty("day").GetString());
        Assert.Equal("2026-11-08T18:00:00Z", data.GetProperty("nextRoundNotBefore").GetString());
    }

    [Fact(DisplayName = "a caller who has not signed in cannot read the status, and nothing is counted for them")]
    public async Task ACallerWhoHasNotSignedInCannotReadTheStatus()
    {
        await using var feature = new SegmentStatusFeature();
        using HttpClient anonymous = feature.Anonymous();

        using HttpResponseMessage response = await anonymous.GetAsync(new Uri(SegmentStatusFeature.Path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(0, feature.LearningData.Reads);
        Assert.Equal(0, feature.Catalog.Reads);
        Assert.Equal(0, feature.Versions.Reads);
    }

    private static string[] Names(JsonElement shape) => [.. shape.EnumerateObject().Select(field => field.Name)];

    private static async Task SkippedAsync(SegmentStatusFeature feature, int version, ViewerActionKind action, int count, DateTime at)
    {
        foreach (int _ in Enumerable.Range(0, count))
        {
            await feature.Skips.TakeAsync(
                SkipEvent.Of(RecordingId.New(), new ViewerAction(at, action, SegmentKind.Break, TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(360), version)),
                CancellationToken.None);
        }
    }

    private static double? Figure(JsonElement figure) => figure.ValueKind is JsonValueKind.Null ? null : figure.GetDouble();

    private static SegmentEvaluation Evaluation(double recall, int recordings)
        => new(
            new SegmentMetrics(recall, 0.85, [new KindMetrics(SegmentKind.Break, 0.95)], 0.5, 4),
            [new StationMetrics(new NetworkId(1), new ServiceId(2), 5, new SegmentMetrics(0.96, 0.9, [], null, 0))],
            new EvaluationEvidence(recordings, recordings * 10 + 20, null));

    private static SegmentModelVersion Version(
        int number,
        SegmentModelVersionState state,
        DateTime trainedAt,
        DateTime? adoptedAt,
        DateTime? retiredAt,
        SegmentEvaluation evaluation,
        SegmentRollback? rollback = null)
        => SegmentModelVersion.Rehydrate(number, state, false, HandMadeModels.First, trainedAt, adoptedAt, retiredAt, null, rollback is null ? null : retiredAt, evaluation, rollback);
}
