using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class DvbFrequencyTests
{
    [Theory]
    [InlineData(13, 473_142_857u)]
    [InlineData(14, 479_142_857u)]
    [InlineData(27, 557_142_857u)]
    [InlineData(62, 767_142_857u)]
    public void TerrestrialChannelsSitSixMegahertzApartAboveChannelThirteen(
        int physicalChannel,
        uint hertz
    )
    {
        Assert.Equal(hertz, DvbFrequency.TerrestrialHertz(physicalChannel));
    }

    [Fact]
    public void TerrestrialFrequenciesAreCountedInHertz()
    {
        Assert.True(DvbFrequency.TerrestrialHertz(13) > 400_000_000u);
    }

    [Theory]
    [InlineData(1, 1_049_480u)]
    [InlineData(3, 1_087_840u)]
    [InlineData(15, 1_318_000u)]
    [InlineData(23, 1_471_440u)]
    public void BroadcastSatelliteSlotsSitAboutThirtyEightMegahertzApart(int slot, uint kilohertz)
    {
        Assert.Equal(kilohertz, DvbFrequency.BroadcastSatelliteKilohertz(slot));
    }

    [Theory]
    [InlineData(2, 1_613_000u)]
    [InlineData(4, 1_653_000u)]
    [InlineData(24, 2_053_000u)]
    public void CommunicationSatelliteSlotsSitFortyMegahertzApart(int slot, uint kilohertz)
    {
        Assert.Equal(kilohertz, DvbFrequency.CommunicationSatelliteKilohertz(slot));
    }

    [Fact]
    public void SatelliteFrequenciesAreCountedInKilohertzSoTheyStayUnderTwoMillion()
    {
        Assert.True(DvbFrequency.BroadcastSatelliteKilohertz(23) < 1_500_000u);
        Assert.True(DvbFrequency.CommunicationSatelliteKilohertz(24) < 3_000_000u);
    }

    [Fact]
    public void TheTerrestrialBandwidthIsSixMegahertz()
    {
        Assert.Equal(6_000_000u, DvbFrequency.TerrestrialBandwidthHertz);
    }
}
