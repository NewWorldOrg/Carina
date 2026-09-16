using System.Net;
using System.Text.Json;

namespace Carina.Api.Tests.FeatureTest;

public sealed class TunersThatNeverLockTests
{
    private const string FirstSatellite = "adapter0.frontend0";

    private const string SecondSatellite = "adapter2.frontend0";

    private const string Terrestrial = "adapter3.frontend0";

    private const long FifteenHoursOfSamples = 5_400;

    [Fact(DisplayName = "BR-QS-001: a tuner that answered for fifteen hours without once locking reads as beyond the level beside one that locked")]
    public async Task ATunerThatAnsweredForFifteenHoursWithoutOnceLockingReadsAsBeyondTheLevel()
    {
        await using QualityFeature feature = new();
        NeverLocked(feature, FirstSatellite);
        NeverLocked(feature, SecondSatellite);
        feature.Sampled(tuner: Terrestrial, samples: FifteenHoursOfSamples, locked: FifteenHoursOfSamples);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/tuners");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement items = body.GetProperty("data").GetProperty("items");

        Assert.Equal("atOrAboveWarning", State(Signal(Tuner(items, FirstSatellite), "lockRate")));
        Assert.Equal("atOrAboveWarning", State(Signal(Tuner(items, SecondSatellite), "lockRate")));
        Assert.Equal("good", State(Signal(Tuner(items, Terrestrial), "lockRate")));
    }

    [Fact(DisplayName = "BR-QD-004: the carrier to noise of a tuner that never locked is no figure on the tuners list")]
    public async Task TheCarrierToNoiseOfATunerThatNeverLockedIsNoFigureOnTheTunersList()
    {
        await using QualityFeature feature = new();
        NeverLocked(feature, FirstSatellite);
        feature.Sampled(tuner: Terrestrial, samples: FifteenHoursOfSamples, locked: FifteenHoursOfSamples);

        JsonElement items = (await feature.GetAsync("/api/quality/tuners")).Body
            .GetProperty("data")
            .GetProperty("items");

        JsonElement never = Signal(Tuner(items, FirstSatellite), "carrierToNoiseFloor");

        Assert.Equal("unmeasured", State(never));
        Assert.Equal(0, never.GetProperty("reading").GetProperty("measured").GetInt32());
        Assert.Equal("good", State(Signal(Tuner(items, Terrestrial), "carrierToNoiseFloor")));
    }

    [Fact(DisplayName = "BR-QD-014: two tuners that never locked leave the summary beyond the level while the one that locked is still counted")]
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

    private static void NeverLocked(QualityFeature feature, string tuner)
        => feature.Sampled(
            tuner: tuner,
            samples: FifteenHoursOfSamples,
            locked: 0,
            carrierToNoise: null,
            bitErrorRate: null);

    private static JsonElement Tuner(JsonElement items, string deviceId)
        => items.EnumerateArray().Single(item => item.GetProperty("deviceId").GetString() == deviceId);

    private static JsonElement Signal(JsonElement tuner, string metric)
        => tuner.GetProperty("signal")
            .EnumerateArray()
            .Single(facet => facet.GetProperty("metric").GetString() == metric);

    private static string? State(JsonElement facet)
        => facet.GetProperty("reading").GetProperty("state").GetString();
}
