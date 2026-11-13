using System.Net;
using System.Text.Json;

using Carina.Domain.Quality;

namespace Carina.Api.Tests.Feature.Quality;

[Trait("Category", "Feature")]
public sealed class GetQualitySummaryActionTests
{
    private const string FirstSatellite = "adapter0.frontend0";

    private const string SecondSatellite = "adapter2.frontend0";

    private const string Terrestrial = "adapter3.frontend0";

    private const long FifteenHoursOfSamples = 5_400;

    [Fact(DisplayName = "a period nothing measured answers unmeasured rather than a clean share")]
    public async Task APeriodNothingMeasuredAnswersUnmeasured()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(dropped: null, total: null, scrambled: null);
        feature.Recorded(dropped: null, total: null, scrambled: null);
        feature.Recorded(dropped: null, total: null, scrambled: null);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/summary");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement data = body.GetProperty("data");
        JsonElement lost = Measure(data, "packetsLost");

        Assert.Equal(3, data.GetProperty("recordings").GetInt32());
        Assert.Equal("unmeasured", lost.GetProperty("state").GetString());
        Assert.Equal(3, lost.GetProperty("unmeasured").GetInt32());
        Assert.Equal(0, lost.GetProperty("measured").GetInt32());
        Assert.Equal(JsonValueKind.Null, lost.GetProperty("average").ValueKind);
    }

    [Fact(DisplayName = "an unmeasured recording stays out of the share and is counted beside it")]
    public async Task AnUnmeasuredRecordingStaysOutOfTheShare()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(dropped: 0, total: 1_000_000);
        feature.Recorded(dropped: null, total: null, scrambled: null);

        JsonElement lost = Measure((await feature.GetAsync("/api/quality/summary")).Body.GetProperty("data"), "packetsLost");

        Assert.Equal("good", lost.GetProperty("state").GetString());
        Assert.Equal(2, lost.GetProperty("subjects").GetInt32());
        Assert.Equal(1, lost.GetProperty("measured").GetInt32());
        Assert.Equal(1, lost.GetProperty("unmeasured").GetInt32());
        Assert.Equal(0, lost.GetProperty("average").GetDouble());
    }

    [Fact(DisplayName = "a period holding no recording answers nothingToMeasure")]
    public async Task APeriodHoldingNoRecordingAnswersNothingToMeasure()
    {
        await using var feature = new QualityFeature();

        JsonElement data = (await feature.GetAsync("/api/quality/summary")).Body.GetProperty("data");

        Assert.Equal(0, data.GetProperty("recordings").GetInt32());
        Assert.Equal("nothingToMeasure", Measure(data, "packetsLost").GetProperty("state").GetString());
    }

    [Fact(DisplayName = "a period beyond the threshold still says how many were measured")]
    public async Task APeriodBeyondTheThresholdStillSaysHowManyWereMeasured()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(dropped: 0, total: 1_000_000);
        feature.Recorded(dropped: 2_000, total: 1_000_000);
        feature.Recorded(dropped: null, total: null, scrambled: null);

        JsonElement lost = Measure((await feature.GetAsync("/api/quality/summary")).Body.GetProperty("data"), "packetsLost");

        Assert.Equal("atOrAboveWarning", lost.GetProperty("state").GetString());
        Assert.Equal(2, lost.GetProperty("measured").GetInt32());
        Assert.Equal(1, lost.GetProperty("beyondThreshold").GetInt32());
        Assert.Equal(1, lost.GetProperty("unmeasured").GetInt32());
        Assert.Equal(1, lost.GetProperty("mayNotBeWatchable").GetInt32());
        Assert.Equal(1, lost.GetProperty("good").GetInt32());
    }

    [Fact(DisplayName = "nothing has ever sampled a signal, so the signal reads unmeasured and never good")]
    public async Task TheSignalReadsUnmeasuredAndNeverGood()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(tuner: "adapter3.frontend0");
        feature.Recorded(tuner: "adapter3.frontend0");

        JsonElement signal = (await feature.GetAsync("/api/quality/summary")).Body
            .GetProperty("data")
            .GetProperty("signal");

        Assert.Equal(3, signal.GetArrayLength());
        Assert.All(signal.EnumerateArray(), facet =>
        {
            Assert.Equal("unmeasured", facet.GetProperty("reading").GetProperty("state").GetString());
            Assert.Equal(1, facet.GetProperty("reading").GetProperty("subjects").GetInt32());
            Assert.Equal(0, facet.GetProperty("reading").GetProperty("measured").GetInt32());
            Assert.Equal(JsonValueKind.Null, facet.GetProperty("lastTakenAt").ValueKind);
        });
        Assert.Equal(
            ["lockRate", "carrierToNoiseFloor", "bitErrorRateCeiling"],
            signal.EnumerateArray().Select(facet => facet.GetProperty("metric").GetString()));
    }

    [Fact]
    public async Task ASignalNoTunerWasSeenForReadsAsNothingToMeasure()
    {
        await using var feature = new QualityFeature();

        JsonElement signal = (await feature.GetAsync("/api/quality/summary")).Body
            .GetProperty("data")
            .GetProperty("signal");

        Assert.Equal(3, signal.GetArrayLength());
        Assert.All(signal.EnumerateArray(), facet =>
            Assert.Equal("nothingToMeasure", facet.GetProperty("reading").GetProperty("state").GetString()));
    }

    [Fact(DisplayName = "every level the answer was judged against is still provisional")]
    public async Task EveryLevelTheAnswerWasJudgedAgainstIsStillProvisional()
    {
        await using var feature = new QualityFeature();
        feature.Recorded();

        Assert.True((await feature.GetAsync("/api/quality/summary")).Body
            .GetProperty("data")
            .GetProperty("provisional")
            .GetBoolean());
    }

    [Fact(DisplayName = "a period wider than the longest span is refused")]
    public async Task APeriodWiderThanTheLongestSpanIsRefused()
    {
        await using var feature = new QualityFeature();
        DateTime tooFarBack = QualityFeature.Noon - QualityPeriod.LongestSpan - TimeSpan.FromDays(1);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await feature.GetAsync($"/api/quality/summary?from={tooFarBack:O}&until={QualityFeature.Noon:O}")).Status);
    }

    [Fact]
    public async Task APeriodNobodyNamedReachesBackOneDay()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(startedAt: QualityFeature.Noon.AddDays(-3));
        feature.Recorded(startedAt: QualityFeature.Noon.AddHours(-3));

        Assert.Equal(
            1,
            (await feature.GetAsync("/api/quality/summary")).Body.GetProperty("data").GetProperty("recordings").GetInt32());
    }

    [Fact(DisplayName = "a tuner the samples reached reads measured, with the moment it was")]
    public async Task ATunerTheSamplesReachedReadsMeasured()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(tuner: "adapter3.frontend0");
        feature.Sampled();

        JsonElement signal = (await feature.GetAsync("/api/quality/summary")).Body
            .GetProperty("data")
            .GetProperty("signal");

        Assert.Equal(3, signal.GetArrayLength());
        Assert.All(signal.EnumerateArray(), facet =>
        {
            Assert.Equal("good", facet.GetProperty("reading").GetProperty("state").GetString());
            Assert.Equal(1, facet.GetProperty("reading").GetProperty("measured").GetInt32());
            Assert.NotEqual(JsonValueKind.Null, facet.GetProperty("lastTakenAt").ValueKind);
        });
    }

    [Fact(DisplayName = "a statistic the tuner does not keep reads unsupported beside the ones it does")]
    public async Task AStatisticTheTunerDoesNotKeepReadsUnsupported()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(tuner: "adapter3.frontend0");
        feature.Sampled(carrierToNoise: null, notRead: ["cnr"]);

        JsonElement signal = (await feature.GetAsync("/api/quality/summary")).Body
            .GetProperty("data")
            .GetProperty("signal");

        Assert.Equal(
            ["good", "unsupported", "good"],
            signal.EnumerateArray().Select(facet => facet.GetProperty("reading").GetProperty("state").GetString()));
    }

    [Fact(DisplayName = "a supply that went silent still says how much of it was measured")]
    public async Task ASupplyThatWentSilentStillSaysHowMuchOfItWasMeasured()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(tuner: "adapter3.frontend0");
        feature.Sampled(tuner: "adapter3.frontend0", carrierToNoise: 6_000);
        feature.Sampled(
            tuner: "adapter3.frontend1",
            samples: 360,
            locked: 0,
            unreachable: 360,
            carrierToNoise: null,
            bitErrorRate: null);

        JsonElement reading = (await feature.GetAsync("/api/quality/summary")).Body
            .GetProperty("data")
            .GetProperty("signal")
            .EnumerateArray()
            .Single(facet => facet.GetProperty("metric").GetString() == "carrierToNoiseFloor")
            .GetProperty("reading");

        Assert.Equal("unreachable", reading.GetProperty("state").GetString());
        Assert.Equal(2, reading.GetProperty("subjects").GetInt32());
        Assert.Equal(1, reading.GetProperty("measured").GetInt32());
        Assert.Equal(1, reading.GetProperty("beyondThreshold").GetInt32());
    }

    [Fact(DisplayName = "samples the driver could not answer do not turn a live supply into an unreachable one")]
    public async Task SamplesTheDriverCouldNotAnswerDoNotTurnALiveSupplyIntoAnUnreachableOne()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(tuner: "adapter3.frontend0");
        feature.Sampled(samples: 361, locked: 360, unreachable: 1);

        JsonElement signal = (await feature.GetAsync("/api/quality/summary")).Body
            .GetProperty("data")
            .GetProperty("signal");

        Assert.Equal(3, signal.GetArrayLength());
        Assert.All(signal.EnumerateArray(), facet =>
        {
            Assert.Equal("good", facet.GetProperty("reading").GetProperty("state").GetString());
            Assert.Equal(1, facet.GetProperty("reading").GetProperty("measured").GetInt32());
            Assert.Equal(0, facet.GetProperty("reading").GetProperty("beyondThreshold").GetInt32());
        });
    }

    [Fact(DisplayName = "two tuners that never locked leave the summary beyond the level while the one that locked is still counted")]
    public async Task TwoTunersThatNeverLockedLeaveTheSummaryBeyondTheLevel()
    {
        await using QualityFeature feature = new();
        NeverLocked(feature, FirstSatellite);
        NeverLocked(feature, SecondSatellite);
        feature.Sampled(tuner: Terrestrial, samples: FifteenHoursOfSamples, locked: FifteenHoursOfSamples);

        JsonElement lockRate = (await feature.GetAsync("/api/quality/summary")).Body
            .GetProperty("data")
            .GetProperty("signal")
            .EnumerateArray()
            .Single(facet => facet.GetProperty("metric").GetString() == "lockRate")
            .GetProperty("reading");

        Assert.Equal("atOrAboveWarning", lockRate.GetProperty("state").GetString());
        Assert.Equal(3, lockRate.GetProperty("subjects").GetInt32());
        Assert.Equal(3, lockRate.GetProperty("measured").GetInt32());
        Assert.Equal(2, lockRate.GetProperty("beyondThreshold").GetInt32());
    }

    [Fact(DisplayName = "a carried shelf reads unmeasured on the summary")]
    public async Task ACarriedShelfReadsUnmeasuredOnTheSummary()
    {
        await using var feature = new QualityFeature();
        string period = CarriedShelf.Shelved(feature);

        JsonElement summary = await CarriedShelf.DataAsync(feature, $"/api/quality/summary?{period}");

        Assert.Equal(CarriedShelf.Shelf, summary.GetProperty("recordings").GetInt32());
        CarriedShelf.ReadsUnmeasuredThroughout(summary.GetProperty("measures"), CarriedShelf.Shelf);
    }

    private static JsonElement Measure(JsonElement data, string metric, string under = "measures")
        => data.GetProperty(under).EnumerateArray()
            .Single(read => read.GetProperty("metric").GetString() == metric)
            .GetProperty("reading");

    private static void NeverLocked(QualityFeature feature, string tuner)
        => feature.Sampled(
            tuner: tuner,
            samples: FifteenHoursOfSamples,
            locked: 0,
            carrierToNoise: null,
            bitErrorRate: null);
}
