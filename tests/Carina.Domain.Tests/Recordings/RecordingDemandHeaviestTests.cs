using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingDemandHeaviestTests
{
    private static readonly DateTime Noon = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

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
