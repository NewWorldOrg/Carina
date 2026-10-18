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
            services.AddSingleton<ISegmentAnswerRepository>(Answers);
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

    public HeldSegmentAnswers Answers { get; } = new();

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
            ["catalog", "evidence", "learning", "learningData", "metrics", "skipThreshold", "stations", "version", "versions"],
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
        feature.Versions.Active = SegmentModelVersion.Rehydrate(3, SegmentModelVersionState.Active, true, SegmentModel.First, trained, trained.AddMinutes(5), null, trained.AddDays(1), null);

        (_, JsonElement data) = await feature.DataAsync();
        JsonElement version = data.GetProperty("version");

        Assert.Equal(3, version.GetProperty("number").GetInt32());
        Assert.True(version.GetProperty("byHand").GetBoolean());
        Assert.Equal("2026-11-01T18:00:00Z", version.GetProperty("trainedAt").GetString());
        Assert.Equal("2026-11-01T18:05:00Z", version.GetProperty("adoptedAt").GetString());
    }

    [Fact(DisplayName = "BR-CD-008: with no version evaluated, the evidence is what the answers kept give, there are no figures, and the threshold stands with nothing recommended")]
    public async Task WithNoVersionEvaluatedTheEvidenceIsWhatTheAnswersGive()
    {
        await using var feature = new SegmentStatusFeature();
        await AnsweredAsync(feature, 4, confirmed: true);
        await AnsweredAsync(feature, 1, confirmed: false);
        await AnsweredAsync(feature, 1, confirmed: true, withdrawn: true);

        (HttpStatusCode status, JsonElement data) = await feature.DataAsync();
        JsonElement evidence = data.GetProperty("evidence");
        JsonElement version = Assert.Single(data.GetProperty("versions").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(data.GetProperty("learning").GetBoolean());
        Assert.Equal(["confirmedRecordings", "boundaries", "enough", "recordingsNeeded", "boundariesNeeded"], Names(evidence));
        Assert.Equal((4, 40, false, 10, 100), (evidence.GetProperty("confirmedRecordings").GetInt32(), evidence.GetProperty("boundaries").GetInt32(), evidence.GetProperty("enough").GetBoolean(), evidence.GetProperty("recordingsNeeded").GetInt32(), evidence.GetProperty("boundariesNeeded").GetInt32()));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("metrics").ValueKind);
        Assert.Empty(data.GetProperty("stations").EnumerateArray());
        Assert.Equal(["number", "state", "byHand", "trainedAt", "adoptedAt", "retiredAt", "rolledBack", "metrics"], Names(version));
        Assert.Equal((1, "active", "2026-10-18T00:00:00Z"), (version.GetProperty("number").GetInt32(), version.GetProperty("state").GetString(), version.GetProperty("adoptedAt").GetString()));
        Assert.Equal(JsonValueKind.Null, version.GetProperty("rolledBack").ValueKind);
        Assert.Equal(JsonValueKind.Null, version.GetProperty("metrics").ValueKind);
        Assert.Equal(["current", "recommended"], Names(data.GetProperty("skipThreshold")));
        Assert.Equal(0.70, data.GetProperty("skipThreshold").GetProperty("current").GetDouble());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("skipThreshold").GetProperty("recommended").ValueKind);
    }

    [Fact(DisplayName = "BR-CD-008: with the version in use evaluated, its figures are answered beside what viewers did with its buttons over the last seven days, by station, with every version's history and the threshold it recommends")]
    public async Task WithTheVersionInUseEvaluatedItsFiguresAreAnswered()
    {
        await using var feature = new SegmentStatusFeature();
        DateTime trained = new(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc);
        feature.Versions.Active = Version(3, SegmentModelVersionState.Active, trained, trained.AddMinutes(5), null, Evaluation(0.93, 12, 0.65));
        feature.Versions.Others.Add(Version(4, SegmentModelVersionState.Candidate, trained.AddDays(1), null, null, Evaluation(0.95, 13, 0.6)));
        feature.Versions.Others.Add(Version(2, SegmentModelVersionState.Retired, trained.AddDays(-7), trained.AddDays(-7), trained.AddMinutes(5), Evaluation(0.9, 11, 0.7), new SegmentRollback(0.09, 0.03, 25, 1)));
        feature.Versions.Others.Add(SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Retired, false, SegmentModel.First, HeldSegmentModelVersions.Installed, HeldSegmentModelVersions.Installed, trained.AddDays(-7), null, null));
        await feature.Settings.SaveAsync(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, true, trained, 0.6, trained), CancellationToken.None);
        await AnsweredAsync(feature, 1, confirmed: true);
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
        Assert.True(data.GetProperty("learning").GetBoolean());
        Assert.Equal(3, data.GetProperty("version").GetProperty("number").GetInt32());
        Assert.Equal((13, 150, true), (data.GetProperty("evidence").GetProperty("confirmedRecordings").GetInt32(), data.GetProperty("evidence").GetProperty("boundaries").GetInt32(), data.GetProperty("evidence").GetProperty("enough").GetBoolean()));
        Assert.Equal(
            ["boundaryRecallWithin2s", "boundaryRecallWithin1s", "extraBoundariesPerRecording", "programmeSkippedSecondsPerHour", "kinds", "returnedAfterSkipShare", "skips"],
            Names(metrics));
        Assert.Equal((0.93, 0.85, 1.2, 2.1), (metrics.GetProperty("boundaryRecallWithin2s").GetDouble(), metrics.GetProperty("boundaryRecallWithin1s").GetDouble(), metrics.GetProperty("extraBoundariesPerRecording").GetDouble(), metrics.GetProperty("programmeSkippedSecondsPerHour").GetDouble()));
        JsonElement kind = Assert.Single(metrics.GetProperty("kinds").EnumerateArray());
        Assert.Equal(("break", 0.95, 0.97), (kind.GetProperty("kind").GetString(), kind.GetProperty("recall").GetDouble(), kind.GetProperty("precision").GetDouble()));
        Assert.Equal((2 / 64d, 64), (metrics.GetProperty("returnedAfterSkipShare").GetDouble(), metrics.GetProperty("skips").GetInt32()));
        Assert.Equal(["networkId", "serviceId", "confirmedRecordings", "metrics"], Names(station));
        Assert.Equal((1, 2, 5), (station.GetProperty("networkId").GetInt32(), station.GetProperty("serviceId").GetInt32(), station.GetProperty("confirmedRecordings").GetInt32()));
        Assert.Equal(["boundaryRecallWithin2s", "programmeSkippedSecondsPerHour"], Names(station.GetProperty("metrics")));
        Assert.Equal((0.96, 1.4), (station.GetProperty("metrics").GetProperty("boundaryRecallWithin2s").GetDouble(), station.GetProperty("metrics").GetProperty("programmeSkippedSecondsPerHour").GetDouble()));
        Assert.Equal([4, 3, 2, 1], versions.Select(version => version.GetProperty("number").GetInt32()));
        Assert.Equal(["candidate", "active", "retired", "retired"], versions.Select(version => version.GetProperty("state").GetString()));
        Assert.Equal(JsonValueKind.Null, versions[0].GetProperty("adoptedAt").ValueKind);
        Assert.Equal(["boundaryRecallWithin2s", "programmeSkippedSecondsPerHour"], Names(versions[1].GetProperty("metrics")));
        Assert.Equal((0.93, 2.1), (versions[1].GetProperty("metrics").GetProperty("boundaryRecallWithin2s").GetDouble(), versions[1].GetProperty("metrics").GetProperty("programmeSkippedSecondsPerHour").GetDouble()));
        Assert.Equal(["at", "returnedAfterSkipShare", "previousShare", "skips"], Names(versions[2].GetProperty("rolledBack")));
        Assert.Equal(("2026-11-01T18:05:00Z", 0.09, 0.03, 25), (versions[2].GetProperty("rolledBack").GetProperty("at").GetString(), versions[2].GetProperty("rolledBack").GetProperty("returnedAfterSkipShare").GetDouble(), versions[2].GetProperty("rolledBack").GetProperty("previousShare").GetDouble(), versions[2].GetProperty("rolledBack").GetProperty("skips").GetInt32()));
        Assert.Equal("2026-11-01T18:05:00Z", versions[2].GetProperty("retiredAt").GetString());
        Assert.Equal(JsonValueKind.Null, versions[3].GetProperty("metrics").ValueKind);
        Assert.Equal(JsonValueKind.Null, versions[3].GetProperty("rolledBack").ValueKind);
        Assert.Equal((0.6, 0.65), (data.GetProperty("skipThreshold").GetProperty("current").GetDouble(), data.GetProperty("skipThreshold").GetProperty("recommended").GetDouble()));
    }

    [Fact(DisplayName = "BR-CD-008: a version in use evaluated on no confirmed recording has no figures, and its evidence still stands")]
    public async Task AVersionInUseEvaluatedOnNoConfirmedRecordingHasNoFigures()
    {
        await using var feature = new SegmentStatusFeature();
        DateTime trained = new(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc);
        feature.Versions.Active = Version(2, SegmentModelVersionState.Active, trained, trained, null, new SegmentEvaluation(null, [], new EvaluationEvidence(0, 0, null), null));
        feature.Versions.Others.Add(SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Retired, false, SegmentModel.First, HeldSegmentModelVersions.Installed, HeldSegmentModelVersions.Installed, trained, null, null));
        await AnsweredAsync(feature, 2, confirmed: true);
        await SkippedAsync(feature, 2, ViewerActionKind.Skipped, 3, SegmentStatusFeature.Now.AddDays(-1));

        (_, JsonElement data) = await feature.DataAsync();

        Assert.Equal(JsonValueKind.Null, data.GetProperty("metrics").ValueKind);
        Assert.Equal(0, data.GetProperty("evidence").GetProperty("confirmedRecordings").GetInt32());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("versions")[0].GetProperty("metrics").ValueKind);
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

    /// <summary>
    /// Keeps <paramref name="count"/> answers of half an hour, each with five breaks and so ten boundaries of
    /// breaks.
    /// </summary>
    private static async Task AnsweredAsync(SegmentStatusFeature feature, int count, bool confirmed, bool withdrawn = false)
    {
        SegmentSpan[] stretches =
        [
            .. Enumerable.Range(0, 5).SelectMany(pod => new[]
            {
                new SegmentSpan(TimeSpan.FromSeconds(pod * 300), TimeSpan.FromSeconds((pod * 300) + 240), SegmentKind.Programme, null),
                new SegmentSpan(TimeSpan.FromSeconds((pod * 300) + 240), TimeSpan.FromSeconds((pod * 300) + 300), SegmentKind.Break, null),
            }),
            new SegmentSpan(TimeSpan.FromSeconds(1500), TimeSpan.FromSeconds(1800), SegmentKind.Programme, null),
        ];

        foreach (int _ in Enumerable.Range(0, count))
        {
            RecordingId recording = RecordingId.New();
            DateTime at = SegmentStatusFeature.Now.AddDays(-2);

            await feature.Answers.AnswerAsync(RecordingAnswer.Rehydrate(recording, stretches, TimeSpan.FromMinutes(30), confirmed, confirmed ? at : null, at, 1, null), CancellationToken.None);

            if (withdrawn)
            {
                await feature.Answers.WithdrawAsync(recording, at.AddHours(1), CancellationToken.None);
            }
        }
    }

    private static async Task SkippedAsync(SegmentStatusFeature feature, int version, ViewerActionKind action, int count, DateTime at)
    {
        foreach (int _ in Enumerable.Range(0, count))
        {
            await feature.Skips.TakeAsync(
                SkipEvent.Of(RecordingId.New(), new ViewerAction(at, action, SegmentKind.Break, TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(360), version)),
                CancellationToken.None);
        }
    }

    private static SegmentEvaluation Evaluation(double recall, int confirmed, double recommended)
        => new(
            new SegmentMetrics(recall, 0.85, 1.2, 2.1, [new KindMetrics(SegmentKind.Break, 0.95, 0.97)], 0.5, 4),
            [new StationMetrics(new NetworkId(1), new ServiceId(2), 5, new SegmentMetrics(0.96, 0.9, 1, 1.4, [], null, 0))],
            new EvaluationEvidence(confirmed, confirmed * 10 + 20, null),
            recommended);

    private static SegmentModelVersion Version(
        int number,
        SegmentModelVersionState state,
        DateTime trainedAt,
        DateTime? adoptedAt,
        DateTime? retiredAt,
        SegmentEvaluation evaluation,
        SegmentRollback? rollback = null)
        => SegmentModelVersion.Rehydrate(number, state, false, SegmentModel.First, trainedAt, adoptedAt, retiredAt, null, rollback is null ? null : retiredAt, evaluation, rollback);
}
