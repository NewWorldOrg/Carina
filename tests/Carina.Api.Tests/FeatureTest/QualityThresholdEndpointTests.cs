using System.Net;
using System.Text.Json;

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

        Assert.Equal(QualityThresholdShapes.All.Count, items.GetArrayLength());
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
}
