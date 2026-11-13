using System.Net;
using System.Text.Json;

using Carina.Domain.Quality;

namespace Carina.Api.Tests.Feature.Quality;

[Trait("Category", "Feature")]
public sealed class ListQualityThresholdsActionTests
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

    [Fact(DisplayName = "the level a supply watch holds silence against is offered")]
    public async Task TheLevelASupplyWatchHoldsSilenceAgainstIsOffered()
    {
        await using var feature = new QualityFeature();

        JsonElement items = (await feature.GetAsync("/api/quality/thresholds")).Body
            .GetProperty("data")
            .GetProperty("items");

        Assert.Contains(
            "supplySilence",
            items.EnumerateArray().Select(item => item.GetProperty("key").GetString()));
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
                Threshold.Of(15_000, 19_500, provisional: false, 240, QualityFeature.Noon),
                null,
                byHand: false,
                QualityThresholdMeasurement.Of(19_500, 240, 18, from, QualityFeature.Noon, QualityFeature.Noon)),
            CancellationToken.None);

        JsonElement measured = Named((await feature.GetAsync("/api/quality/thresholds")).Body, "carrierToNoiseFloor");
        JsonElement measurement = measured.GetProperty("measurement");

        Assert.Equal("measured", measured.GetProperty("source").GetString());
        Assert.False(measured.GetProperty("provisional").GetBoolean());
        Assert.Equal(240, measured.GetProperty("observations").GetInt64());
        Assert.Equal(19_500, measurement.GetProperty("value").GetDouble());
        Assert.Equal(240, measurement.GetProperty("sessions").GetInt64());
        Assert.Equal(18, measurement.GetProperty("sessionsDropped").GetInt64());
        Assert.Equal(from, measurement.GetProperty("from").GetDateTime());
        Assert.Equal(QualityFeature.Noon, measurement.GetProperty("until").GetDateTime());
        Assert.Equal(QualityFeature.Noon, measurement.GetProperty("measuredAt").GetDateTime());
    }

    private static JsonElement Named(JsonElement body, string key)
        => body.GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == key);
}
