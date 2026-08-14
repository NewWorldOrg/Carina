using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class SatelliteTransportStreamTests
{
    [Fact]
    public void AReferenceRowTunesTheSlotItStandsFor()
    {
        var row = SatelliteTransportStream.Rehydrate(15, 0, new TransportStreamId(0x40F0));

        var tuning = row.ToTuningParameters();

        Assert.Equal(TuneSystem.IsdbSBs, tuning.System);
        Assert.Equal(15, tuning.PhysicalChannel);
        Assert.Equal(new TransportStreamId(0x40F0), tuning.TransportStreamId);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(17)]
    [InlineData(25)]
    public void ASlotThatCannotBeDemodulatedHasNoReferenceRow(int bsChannel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SatelliteTransportStream.Rehydrate(bsChannel, 0, new TransportStreamId(0x4010)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void ARelativeStreamNumberOutsideThreeBitsIsRefused(int relativeStreamNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SatelliteTransportStream.Rehydrate(1, relativeStreamNumber, new TransportStreamId(0x4010)));
    }
}
