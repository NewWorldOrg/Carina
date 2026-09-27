using System.Net;
using System.Text.Json;

using Carina.Contracts;

namespace Carina.Api.Tests.FeatureTest;

public sealed class TunersThatNeverLockTests
{
    private const string FirstSatellite = "adapter0.frontend0";

    private const string SecondSatellite = "adapter2.frontend0";

    private const string Terrestrial = "adapter3.frontend0";

    private const long FifteenHoursOfSamples = 5_400;

    [Fact(DisplayName = "a tuner that answered for fifteen hours without once locking reads as beyond the level beside one that locked")]
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

    [Fact(DisplayName = "the carrier to noise of a tuner that never locked is no figure on the tuners list")]
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

    [Fact(DisplayName = "BR-QD-017: an enabled tuner with no samples and no recordings still has a row, and it reads as unmeasured")]
    public async Task AnEnabledTunerWithNoSamplesStillHasARowThatReadsAsUnmeasured()
    {
        await using QualityFeature feature = new();
        feature.Driver.Tuners =
        [
            new TunerSnapshot(FirstSatellite, TunerKind.Satellite, TunerState.Idle),
            new TunerSnapshot(SecondSatellite, TunerKind.Satellite, TunerState.Disabled),
            new TunerSnapshot(Terrestrial, TunerKind.Terrestrial, TunerState.Idle),
        ];
        feature.Sampled(tuner: Terrestrial, samples: FifteenHoursOfSamples, locked: FifteenHoursOfSamples);

        JsonElement items = await TunersAsync(feature);

        Assert.Equal([Terrestrial, FirstSatellite], DeviceIds(items));
        Assert.Equal("good", Tuner(items, Terrestrial).GetProperty("standing").GetString());

        JsonElement unmeasured = Tuner(items, FirstSatellite);

        Assert.Equal("unmeasured", unmeasured.GetProperty("standing").GetString());
        Assert.False(unmeasured.GetProperty("cannotLock").GetBoolean());
        Assert.Equal("unmeasured", State(Signal(unmeasured, "lockRate")));
    }

    [Fact(DisplayName = "BR-QD-017: a tuner an unsettled incident says cannot lock heads the list as unable to lock, samples or not")]
    public async Task ATunerAnIncidentSaysCannotLockHeadsTheListAsUnableToLock()
    {
        await using QualityFeature feature = new();
        feature.Driver.Tuners =
        [
            new TunerSnapshot(FirstSatellite, TunerKind.Satellite, TunerState.Faulted),
            new TunerSnapshot(Terrestrial, TunerKind.Terrestrial, TunerState.Idle),
        ];
        feature.Sampled(tuner: Terrestrial, samples: FifteenHoursOfSamples, locked: FifteenHoursOfSamples);
        feature.Recorded(tuner: Terrestrial);
        feature.CannotLock(FirstSatellite);
        feature.CannotLock(SecondSatellite);

        JsonElement items = await TunersAsync(feature);

        Assert.Equal([FirstSatellite, SecondSatellite, Terrestrial], DeviceIds(items));

        foreach (string satellite in new[] { FirstSatellite, SecondSatellite })
        {
            JsonElement cannotLock = Tuner(items, satellite);

            Assert.Equal("mayNotBeWatchable", cannotLock.GetProperty("standing").GetString());
            Assert.True(cannotLock.GetProperty("cannotLock").GetBoolean());
        }

        Assert.False(Tuner(items, Terrestrial).GetProperty("cannotLock").GetBoolean());
    }

    [Fact(DisplayName = "BR-QD-017: a bit error rate beyond its level puts the row beyond the level while nothing was recorded")]
    public async Task ABitErrorRateBeyondItsLevelPutsTheRowBeyondTheLevelWhileNothingWasRecorded()
    {
        await using QualityFeature feature = new();
        feature.Driver.Tuners = [new TunerSnapshot(Terrestrial, TunerKind.Terrestrial, TunerState.Idle)];
        feature.Sampled(tuner: Terrestrial, bitErrorRate: 0.01);

        JsonElement row = Tuner(await TunersAsync(feature), Terrestrial);

        Assert.Equal("nothingToMeasure", row.GetProperty("measures")[0].GetProperty("reading").GetProperty("state").GetString());
        Assert.Equal("atOrAboveWarning", State(Signal(row, "bitErrorRateCeiling")));
        Assert.Equal("warning", row.GetProperty("standing").GetString());
    }

    [Fact(DisplayName = "BR-QD-017: a driver that cannot be asked still leaves the sampled tuners and the ones that cannot lock on the list")]
    public async Task ADriverThatCannotBeAskedStillLeavesTheSampledTunersOnTheList()
    {
        await using QualityFeature feature = new();
        feature.Driver.Unreachable = "the driver socket is gone";
        feature.Sampled(tuner: Terrestrial, samples: FifteenHoursOfSamples, locked: FifteenHoursOfSamples);
        feature.CannotLock(FirstSatellite);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/tuners");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal([FirstSatellite, Terrestrial], DeviceIds(body.GetProperty("data").GetProperty("items")));
    }

    private static async Task<JsonElement> TunersAsync(QualityFeature feature)
        => (await feature.GetAsync("/api/quality/tuners")).Body
            .GetProperty("data")
            .GetProperty("items");

    private static string[] DeviceIds(JsonElement items)
        => [.. items.EnumerateArray().Select(item => item.GetProperty("deviceId").GetString() ?? string.Empty)];

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
