using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Quality;

[Trait("Category", "Feature")]
public sealed class ReviseQualityThresholdActionTests
{
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

    [Fact(DisplayName = "the level a supply watch holds silence against can be moved")]
    public async Task TheLevelASupplyWatchHoldsSilenceAgainstCanBeMoved()
    {
        await using var feature = new QualityFeature();

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

    [Fact(DisplayName = "BR-QD-023: a level set by hand says so, and letting go of it returns it to the measurement and records the change")]
    public async Task ALevelSetByHandSaysSoAndLettingGoReturnsItToTheMeasurement()
    {
        await using var feature = new QualityFeature();
        DateTime from = QualityFeature.Noon.AddDays(-7);

        await feature.Thresholds.SaveAsync(
            QualityThreshold.Rehydrate(
                QualityThresholdKey.CarrierToNoiseFloor,
                Threshold.Of(15_000, 19_500, provisional: false, 240, QualityFeature.Noon),
                null,
                byHand: false,
                QualityThresholdMeasurement.Of(19_500, 240, 18, from, QualityFeature.Noon, QualityFeature.Noon)),
            CancellationToken.None);

        JsonElement byHand = (await feature.PatchAsync(
            "/api/quality/thresholds/carrierToNoiseFloor",
            new { value = 20_000.0 })).Body.GetProperty("data");

        Assert.Equal("byHand", byHand.GetProperty("source").GetString());
        Assert.Equal(20_000, byHand.GetProperty("currentValue").GetDouble());
        Assert.Equal(19_500, byHand.GetProperty("measurement").GetProperty("value").GetDouble());

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            "/api/quality/thresholds/carrierToNoiseFloor",
            new { byHand = false });
        JsonElement released = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("measured", released.GetProperty("source").GetString());
        Assert.Equal(19_500, released.GetProperty("currentValue").GetDouble());
        Assert.Equal(
            [(20_000d, QualityThresholdChangeCause.Hand), (19_500d, QualityThresholdChangeCause.Hand)],
            feature.Changes.Changes.Select(change => (change.NextValue, change.Cause)));
        Assert.Equal([AppEventName.Quality, AppEventName.Quality], feature.Events.Signalled);
    }

    [Fact(DisplayName = "BR-QV-002: setting a level by hand and letting go of it name the signed-in person on the level and on the change")]
    public async Task SettingALevelByHandAndLettingGoOfItNameTheSignedInPerson()
    {
        await using var feature = new QualityFeature();

        JsonElement byHand = (await feature.PatchAsync(
            "/api/quality/thresholds/packetsLostWarning",
            new { value = 0.0005 })).Body.GetProperty("data");

        Assert.Equal(TestAuthenticationHandler.TesterDisplayName, byHand.GetProperty("updatedBy").GetString());
        Assert.Equal(
            TestAuthenticationHandler.TesterDisplayName,
            byHand.GetProperty("lastChange").GetProperty("changedBy").GetString());

        JsonElement released = (await feature.PatchAsync(
            "/api/quality/thresholds/packetsLostWarning",
            new { byHand = false })).Body.GetProperty("data");

        Assert.Equal(TestAuthenticationHandler.TesterDisplayName, released.GetProperty("updatedBy").GetString());
        Assert.Equal(
            [TestAuthenticationHandler.TesterDisplayName, TestAuthenticationHandler.TesterDisplayName],
            feature.Changes.Changes.Select(change => change.ChangedBy));
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

    [Fact(DisplayName = "BR-QD-023: letting go of a level that would fall under the warning level beside it is refused, and nothing moves")]
    public async Task LettingGoOfALevelThatWouldFallUnderTheWarningLevelIsRefused()
    {
        await using var feature = new QualityFeature();

        await feature.PatchAsync("/api/quality/thresholds/packetsLostUnwatchable", new { value = 0.5 });
        await feature.PatchAsync("/api/quality/thresholds/packetsLostWarning", new { value = 0.3 });

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            "/api/quality/thresholds/packetsLostUnwatchable",
            new { byHand = false });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal(
            0.5,
            feature.Thresholds.Thresholds.Single(threshold => threshold.Key == QualityThresholdKey.PacketsLostUnwatchable)
                .Setting.Current);
        Assert.Equal(2, feature.Changes.Changes.Count);
    }

    [Fact(DisplayName = "BR-QD-023: a level is moved only after every other writer of the levels has had its turn")]
    public async Task ALevelIsMovedOnlyAfterEveryOtherWriterHasHadItsTurn()
    {
        await using var feature = new QualityFeature();

        await feature.PatchAsync("/api/quality/thresholds/lockRate", new { value = 0.95 });
        await feature.PatchAsync("/api/quality/thresholds/lockRate", new { byHand = false });

        Assert.Equal(2, feature.Thresholds.TurnsTaken);
    }

    [Fact(DisplayName = "moving the level scrambling is held against moves what every recording says")]
    public async Task MovingTheLevelScramblingIsHeldAgainstMovesWhatEveryRecordingSays()
    {
        await using var feature = new RecordingFeature();
        Recording recording = Measured(feature, dropped: 0, total: 1_000_000, scrambled: 20_000);

        (HttpStatusCode moved, _) = await feature.PatchAsync(
            "/api/quality/thresholds/packetsLeftScrambledUnwatchable",
            new { value = 0.05 });
        (_, JsonElement list) = await feature.GetAsync("/api/recordings");
        (_, JsonElement one) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");

        JsonElement listed = list.GetProperty("data").GetProperty("items")[0].GetProperty("drops");
        JsonElement detailed = one.GetProperty("data").GetProperty("recording").GetProperty("drops");

        Assert.Equal(HttpStatusCode.OK, moved);
        Assert.Equal("warning", listed.GetProperty("scrambleQuality").GetString());
        Assert.Equal("warning", listed.GetProperty("quality").GetString());
        Assert.Equal("warning", detailed.GetProperty("scrambleQuality").GetString());
        Assert.Equal("warning", detailed.GetProperty("quality").GetString());
    }

    private static string? State(JsonElement body)
        => body.GetProperty("data").GetProperty("measures").EnumerateArray()
            .Single(read => read.GetProperty("metric").GetString() == "packetsLost")
            .GetProperty("reading")
            .GetProperty("state")
            .GetString();

    private static Recording Measured(RecordingFeature feature, long dropped, long total, long scrambled)
    {
        Recording recording = feature.Held();

        recording.Measure(
            DropCounters.Counted(dropped, total),
            DropTimeline.Unlocated,
            scrambled,
            0,
            RecordingFeature.Noon.AddMinutes(10));

        return recording;
    }
}
