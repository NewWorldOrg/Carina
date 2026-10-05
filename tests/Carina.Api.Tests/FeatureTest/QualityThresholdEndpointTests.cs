using System.Net;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Quality;

namespace Carina.Api.Tests.FeatureTest;

public sealed class QualityThresholdEndpointTests
{
    [Fact(DisplayName = "every level answers with its shipped value, marked provisional and standing on nothing")]
    public async Task EveryLevelAnswersWithItsShippedValue()
    {
        await using var feature = new QualityFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/thresholds");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement items = body.GetProperty("data").GetProperty("items");

        Assert.Equal(QualityThresholdShapes.Consulted.Count, items.GetArrayLength());
        Assert.All(items.EnumerateArray(), item =>
        {
            Assert.True(item.GetProperty("provisional").GetBoolean());
            Assert.Equal(0, item.GetProperty("observations").GetInt64());
            Assert.False(item.GetProperty("stored").GetBoolean());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("updatedAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("lastChange").ValueKind);
            Assert.Equal(item.GetProperty("defaultValue").GetDouble(), item.GetProperty("currentValue").GetDouble());
        });
    }

    [Fact]
    public async Task EveryLevelCarriesTheRangeItIsHeldInAndTheMeasureItJudges()
    {
        await using var feature = new QualityFeature();

        JsonElement warning = Named(
            (await feature.GetAsync("/api/quality/thresholds")).Body,
            "packetsLostWarning");

        Assert.Equal("packetsLost", warning.GetProperty("metric").GetString());
        Assert.Equal("ceiling", warning.GetProperty("sense").GetString());
        Assert.Equal(0, warning.GetProperty("lowest").GetDouble());
        Assert.Equal(1, warning.GetProperty("highest").GetDouble());
    }

    [Fact(DisplayName = "a level that moves leaves a record of what it moved from")]
    public async Task ALevelThatMovesLeavesARecordOfWhatItMovedFrom()
    {
        await using var feature = new QualityFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            "/api/quality/thresholds/packetsLostWarning",
            new { value = 0.0005 });

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement moved = body.GetProperty("data");

        Assert.Equal(0.0005, moved.GetProperty("currentValue").GetDouble());
        Assert.Equal(0.0002, moved.GetProperty("defaultValue").GetDouble());
        Assert.True(moved.GetProperty("provisional").GetBoolean());
        Assert.True(moved.GetProperty("stored").GetBoolean());
        Assert.Equal(QualityFeature.Noon, moved.GetProperty("updatedAt").GetDateTime());
        Assert.Equal(0.0002, moved.GetProperty("lastChange").GetProperty("previousValue").GetDouble());
        Assert.Equal(0.0005, moved.GetProperty("lastChange").GetProperty("nextValue").GetDouble());

        QualityThresholdChange recorded = Assert.Single(feature.Changes.Changes);

        Assert.Equal(QualityThresholdKey.PacketsLostWarning, recorded.Key);
        Assert.Equal(0.0002, recorded.PreviousValue);
        Assert.Equal(0.0005, recorded.NextValue);
    }

    [Fact(DisplayName = "a level outside its range is refused rather than saved and warned about")]
    public async Task ALevelOutsideItsRangeIsRefused()
    {
        await using var feature = new QualityFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            "/api/quality/thresholds/packetsLostWarning",
            new { value = 2.0 });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Empty(feature.Changes.Changes);
        Assert.Empty(feature.Thresholds.Thresholds);
    }

    [Fact(DisplayName = "a warning level pushed past the unwatchable one is refused")]
    public async Task AWarningLevelPushedPastTheUnwatchableOneIsRefused()
    {
        await using var feature = new QualityFeature();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await feature.PatchAsync("/api/quality/thresholds/packetsLostWarning", new { value = 0.5 })).Status);
        Assert.Empty(feature.Changes.Changes);
    }

    [Fact]
    public async Task ALevelThisDomainDoesNotNameIsNotThereToMove()
    {
        await using var feature = new QualityFeature();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await feature.PatchAsync("/api/quality/thresholds/somethingElse", new { value = 0.5 })).Status);
    }

    [Fact(DisplayName = "the level a supply watch holds silence against is offered and can be moved")]
    public async Task TheLevelASupplyWatchHoldsSilenceAgainstIsOfferedAndCanBeMoved()
    {
        await using var feature = new QualityFeature();

        JsonElement items = (await feature.GetAsync("/api/quality/thresholds")).Body
            .GetProperty("data")
            .GetProperty("items");

        Assert.Contains(
            "supplySilence",
            items.EnumerateArray().Select(item => item.GetProperty("key").GetString()));
        Assert.Equal(
            HttpStatusCode.OK,
            (await feature.PatchAsync("/api/quality/thresholds/supplySilence", new { value = 600.0 })).Status);
        Assert.Equal(600.0, Assert.Single(feature.Changes.Changes).NextValue);
    }

    [Fact]
    public async Task AChangeNamingNoValueIsRefused()
    {
        await using var feature = new QualityFeature();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await feature.PatchAsync("/api/quality/thresholds/lockRate", new { })).Status);
    }

    [Fact(DisplayName = "a level that moved is what the next answer is judged against")]
    public async Task ALevelThatMovedIsWhatTheNextAnswerIsJudgedAgainst()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(dropped: 300, total: 1_000_000);

        Assert.Equal(
            "atOrAboveWarning",
            State((await feature.GetAsync("/api/quality/summary")).Body));

        await feature.PatchAsync("/api/quality/thresholds/packetsLostWarning", new { value = 0.0009 });

        Assert.Equal("good", State((await feature.GetAsync("/api/quality/summary")).Body));
    }

    [Fact(DisplayName = "a level that moves does not rewrite what an earlier judgement was held against")]
    public async Task ALevelThatMovesDoesNotRewriteAnEarlierJudgement()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(dropped: 300, total: 1_000_000);

        JsonElement before = (await feature.GetAsync("/api/quality/recordings")).Body
            .GetProperty("data")
            .GetProperty("items")[0];

        Threshold applied = Threshold.Provisionally(
            before.GetProperty("verdicts").EnumerateArray()
                .Single(read => read.GetProperty("metric").GetString() == "packetsLost")
                .GetProperty("appliedValue")
                .GetDouble(),
            0,
            QualityFeature.Noon);

        await feature.PatchAsync("/api/quality/thresholds/packetsLostWarning", new { value = 0.0009 });

        Assert.Equal(0.0002, applied.Current);
        Assert.Equal(0.0002, Assert.Single(feature.Changes.Changes).PreviousValue);
    }

    private static JsonElement Named(JsonElement body, string key)
        => body.GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == key);

    private static string? State(JsonElement body)
        => body.GetProperty("data").GetProperty("measures").EnumerateArray()
            .Single(read => read.GetProperty("metric").GetString() == "packetsLost")
            .GetProperty("reading")
            .GetProperty("state")
            .GetString();

    [Fact]
    public async Task MovingALevelTellsTheScreensTheQualityMoved()
    {
        await using var feature = new QualityFeature();

        await feature.PatchAsync("/api/quality/thresholds/packetsLostWarning", new { value = 0.0009 });

        Assert.Equal([AppEventName.Quality], feature.Events.Signalled);
    }

    [Fact]
    public async Task ALevelThatWasRefusedTellsTheScreensNothing()
    {
        await using var feature = new QualityFeature();

        await feature.PatchAsync("/api/quality/thresholds/packetsLostWarning", new { value = 500.0 });

        Assert.Empty(feature.Events.Signalled);
    }

    [Fact(DisplayName = "BR-QD-023: every level says where it came from, and a shipped one stands on no measurement")]
    public async Task EveryLevelSaysWhereItCameFrom()
    {
        await using var feature = new QualityFeature();

        JsonElement items = (await feature.GetAsync("/api/quality/thresholds")).Body
            .GetProperty("data")
            .GetProperty("items");

        Assert.All(items.EnumerateArray(), item =>
        {
            Assert.Equal("shipped", item.GetProperty("source").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("measurement").ValueKind);
        });
    }

    [Fact(DisplayName = "BR-QD-023: a measured level answers with the measurement it stands on")]
    public async Task AMeasuredLevelAnswersWithTheMeasurementItStandsOn()
    {
        await using var feature = new QualityFeature();
        DateTime from = QualityFeature.Noon.AddDays(-7);

        await feature.Thresholds.SaveAsync(
            QualityThreshold.Rehydrate(
                QualityThresholdKey.CarrierToNoiseFloor,
                Threshold.Of(15_000, 18_600, provisional: false, 328, QualityFeature.Noon),
                null,
                byHand: false,
                QualityThresholdMeasurement.Of(18_600, 328, 24, from, QualityFeature.Noon, QualityFeature.Noon)),
            CancellationToken.None);

        JsonElement measured = Named((await feature.GetAsync("/api/quality/thresholds")).Body, "carrierToNoiseFloor");
        JsonElement measurement = measured.GetProperty("measurement");

        Assert.Equal("measured", measured.GetProperty("source").GetString());
        Assert.False(measured.GetProperty("provisional").GetBoolean());
        Assert.Equal(328, measured.GetProperty("observations").GetInt64());
        Assert.Equal(18_600, measurement.GetProperty("value").GetDouble());
        Assert.Equal(328, measurement.GetProperty("sessions").GetInt64());
        Assert.Equal(24, measurement.GetProperty("sessionsDropped").GetInt64());
        Assert.Equal(from, measurement.GetProperty("from").GetDateTime());
        Assert.Equal(QualityFeature.Noon, measurement.GetProperty("until").GetDateTime());
        Assert.Equal(QualityFeature.Noon, measurement.GetProperty("measuredAt").GetDateTime());
    }

    [Fact(DisplayName = "BR-QD-023: a level set by hand says so, and letting go of it returns it to the measurement and records the change")]
    public async Task ALevelSetByHandSaysSoAndLettingGoReturnsItToTheMeasurement()
    {
        await using var feature = new QualityFeature();
        DateTime from = QualityFeature.Noon.AddDays(-7);

        await feature.Thresholds.SaveAsync(
            QualityThreshold.Rehydrate(
                QualityThresholdKey.CarrierToNoiseFloor,
                Threshold.Of(15_000, 18_600, provisional: false, 328, QualityFeature.Noon),
                null,
                byHand: false,
                QualityThresholdMeasurement.Of(18_600, 328, 24, from, QualityFeature.Noon, QualityFeature.Noon)),
            CancellationToken.None);

        JsonElement byHand = (await feature.PatchAsync(
            "/api/quality/thresholds/carrierToNoiseFloor",
            new { value = 20_000.0 })).Body.GetProperty("data");

        Assert.Equal("byHand", byHand.GetProperty("source").GetString());
        Assert.Equal(20_000, byHand.GetProperty("currentValue").GetDouble());
        Assert.Equal(18_600, byHand.GetProperty("measurement").GetProperty("value").GetDouble());

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            "/api/quality/thresholds/carrierToNoiseFloor",
            new { byHand = false });
        JsonElement released = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("measured", released.GetProperty("source").GetString());
        Assert.Equal(18_600, released.GetProperty("currentValue").GetDouble());
        Assert.Equal(
            [(20_000d, QualityThresholdChangeCause.Hand), (18_600d, QualityThresholdChangeCause.Hand)],
            feature.Changes.Changes.Select(change => (change.NextValue, change.Cause)));
        Assert.Equal([AppEventName.Quality, AppEventName.Quality], feature.Events.Signalled);
    }

    [Fact(DisplayName = "BR-QD-023: letting go of a level nobody set by hand changes nothing and tells the screens nothing")]
    public async Task LettingGoOfALevelNobodySetByHandChangesNothing()
    {
        await using var feature = new QualityFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            "/api/quality/thresholds/lockRate",
            new { byHand = false });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("shipped", body.GetProperty("data").GetProperty("source").GetString());
        Assert.Empty(feature.Changes.Changes);
        Assert.Empty(feature.Thresholds.Thresholds);
        Assert.Empty(feature.Events.Signalled);
    }

    [Fact(DisplayName = "BR-QD-023: a value and letting go sent together are refused")]
    public async Task AValueAndLettingGoSentTogetherAreRefused()
    {
        await using var feature = new QualityFeature();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await feature.PatchAsync("/api/quality/thresholds/lockRate", new { value = 0.95, byHand = false })).Status);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await feature.PatchAsync("/api/quality/thresholds/lockRate", new { byHand = true })).Status);
        Assert.Empty(feature.Changes.Changes);
    }
}
