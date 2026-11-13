using Carina.Domain.Recordings;
using Puppis.Contracts;

namespace Carina.Domain.Tests.Unit.Recordings;

[Trait("Category", "Unit")]
public sealed class RecordingDemandTests
{
    private static readonly DateTime Noon = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ATerrestrialHourAsksForTheTopOfTheMeasuredRange()
    {
        var demand = new RecordingDemand(TunerKind.Terrestrial, Noon, Noon.AddHours(1));

        Assert.Equal((Int128)7_425_000_000L, demand.HeaviestBytes(Noon));
    }

    [Fact]
    public void ASatelliteHourAsksForTheTopOfItsOwnRange()
    {
        var demand = new RecordingDemand(TunerKind.Satellite, Noon, Noon.AddHours(1));

        Assert.Equal((Int128)5_490_000_000L, demand.HeaviestBytes(Noon));
    }

    [Fact]
    public void WhatIsLeftIsMeasuredFromWhicheverOfNowAndTheStartComesLater()
    {
        var demand = new RecordingDemand(TunerKind.Terrestrial, Noon, Noon.AddHours(1));

        Assert.Equal(TimeSpan.FromHours(1), demand.Remaining(Noon.AddMinutes(-30)));
        Assert.Equal(TimeSpan.FromHours(1), demand.Remaining(Noon));
        Assert.Equal(TimeSpan.FromMinutes(45), demand.Remaining(Noon.AddMinutes(15)));
    }

    [Fact]
    public void ADemandThatHasRunOutHasNothingLeftToWrite()
    {
        var demand = new RecordingDemand(TunerKind.Terrestrial, Noon, Noon.AddHours(1));

        Assert.Equal(TimeSpan.FromTicks(1), demand.Remaining(Noon.AddHours(1).AddTicks(-1)));
        Assert.Equal(TimeSpan.Zero, demand.Remaining(Noon.AddHours(1)));
        Assert.Equal(TimeSpan.Zero, demand.Remaining(Noon.AddHours(2)));
        Assert.Equal(Int128.Zero, demand.HeaviestBytes(Noon.AddHours(1)));
    }

    [Fact]
    public void AWindowThatDoesNotRunForwardsIsNoDemandAtAll()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => new RecordingDemand(TunerKind.Terrestrial, Noon, Noon));

        Assert.Equal("until", refusal.ParamName);

        Assert.Equal(
            "until",
            Assert.Throws<ArgumentException>(
                () => new RecordingDemand(TunerKind.Terrestrial, Noon, Noon.AddTicks(-1))).ParamName);

        Assert.Equal(
            TimeSpan.FromTicks(1),
            new RecordingDemand(TunerKind.Terrestrial, Noon, Noon.AddTicks(1)).Remaining(Noon));
    }

    [Fact]
    public void AKindWithNoMeasuredRateHasNothingToBeWeighedAgainst()
    {
        ArgumentOutOfRangeException refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RecordingDemand(TunerKind.Unspecified, Noon, Noon.AddHours(1)));

        Assert.Equal("kind", refusal.ParamName);

        Assert.Equal(
            "kind",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new RecordingDemand((TunerKind)99, Noon, Noon.AddHours(1))).ParamName);
    }

    [Fact]
    public void ADemandIsBoundedInUtc()
    {
        Assert.Equal(
            "from",
            Assert.Throws<ArgumentException>(
                () => new RecordingDemand(
                    TunerKind.Terrestrial,
                    DateTime.SpecifyKind(Noon, DateTimeKind.Local),
                    Noon.AddHours(1))).ParamName);

        Assert.Equal(
            "until",
            Assert.Throws<ArgumentException>(
                () => new RecordingDemand(
                    TunerKind.Terrestrial,
                    Noon,
                    DateTime.SpecifyKind(Noon.AddHours(1), DateTimeKind.Unspecified))).ParamName);
    }

    [Fact]
    public void TheInstantADemandIsMeasuredAgainstIsAskedForInUtc()
    {
        var demand = new RecordingDemand(TunerKind.Terrestrial, Noon, Noon.AddHours(1));

        Assert.Equal(
            "asOf",
            Assert.Throws<ArgumentException>(
                () => demand.Remaining(DateTime.SpecifyKind(Noon, DateTimeKind.Local))).ParamName);
    }

    [Fact]
    public void TheHeaviestRateIsTheTopOfTheHeavierOfTheTwoMeasuredRanges()
    {
        RecordingDemand demand = RecordingDemand.AtTheHeaviestRate(Noon, Noon.AddHours(1));

        Assert.Equal(16_500_000, demand.Bitrate.MostBitsPerSecond);
        Assert.Equal(14_300_000, demand.Bitrate.LeastBitsPerSecond);
    }

    [Fact]
    public void AnHourAtTheHeaviestRateWeighsWhatThatRateSays()
    {
        Assert.Equal(
            (Int128)7_425_000_000,
            RecordingDemand.AtTheHeaviestRate(Noon, Noon.AddHours(1)).HeaviestBytes(Noon));
    }

    [Fact]
    public void NeitherMeasuredRangeReachesAboveTheOneTheHeaviestRateUses()
    {
        RecordingDemand demand = RecordingDemand.AtTheHeaviestRate(Noon, Noon.AddHours(1));

        Assert.True(demand.Bitrate.MostBitsPerSecond >= ExpectedBitrate.Terrestrial.MostBitsPerSecond);
        Assert.True(demand.Bitrate.MostBitsPerSecond >= ExpectedBitrate.Satellite.MostBitsPerSecond);
    }

    [Fact]
    public void TheWindowIsTheOneItWasHanded()
    {
        RecordingDemand demand = RecordingDemand.AtTheHeaviestRate(Noon, Noon.AddHours(2));

        Assert.Equal(Noon, demand.From);
        Assert.Equal(Noon.AddHours(2), demand.Until);
    }
}
