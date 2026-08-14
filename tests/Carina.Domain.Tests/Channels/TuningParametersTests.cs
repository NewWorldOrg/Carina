using Carina.Contracts;
using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class TuningParametersTests
{
    [Theory]
    [InlineData(13)]
    [InlineData(62)]
    public void ATerrestrialChannelInsideTheRangeIsAccepted(int physicalChannel)
    {
        var tuning = TuningParameters.Terrestrial(physicalChannel);

        Assert.Equal(TuneSystem.IsdbT, tuning.System);
        Assert.Equal(physicalChannel, tuning.PhysicalChannel);
        Assert.Null(tuning.TransportStreamId);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(63)]
    [InlineData(0)]
    public void ATerrestrialChannelOutsideTheRangeCannotBeExpressed(int physicalChannel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TuningParameters.Terrestrial(physicalChannel));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(23)]
    public void AnOddBsSlotThatDemodulatesIsAccepted(int bsChannel)
    {
        var tuning = TuningParameters.Bs(bsChannel, new TransportStreamId(0x4010));

        Assert.Equal(TuneSystem.IsdbSBs, tuning.System);
        Assert.Equal(bsChannel, tuning.PhysicalChannel);
        Assert.Equal(new TransportStreamId(0x4010), tuning.TransportStreamId);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(25)]
    public void AnEvenOrOutOfRangeBsSlotCannotBeExpressed(int bsChannel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TuningParameters.Bs(bsChannel, new TransportStreamId(0x4010)));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(17)]
    public void TheTwoBsSlotsWithoutDemodulationCannotBeExpressed(int bsChannel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TuningParameters.Bs(bsChannel, new TransportStreamId(0x4010)));
    }

    [Fact]
    public void ABsSlotCarriesTheTransportStreamItWasTunedFor()
    {
        Assert.Throws<ArgumentNullException>(() => TuningParameters.Bs(1, null!));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(24)]
    public void AnEvenCs110SlotIsAccepted(int csChannel)
    {
        var tuning = TuningParameters.Cs110(csChannel);

        Assert.Equal(TuneSystem.IsdbSCs110, tuning.System);
        Assert.Equal(csChannel, tuning.PhysicalChannel);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(26)]
    [InlineData(0)]
    public void AnOddOrOutOfRangeCs110SlotCannotBeExpressed(int csChannel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TuningParameters.Cs110(csChannel));
    }

    [Fact]
    public void ACs110SlotNeedsNoTransportStreamIdBecauseItCarriesOneStream()
    {
        Assert.Null(TuningParameters.Cs110(2).TransportStreamId);
    }

    [Fact]
    public void TwoWaysOfReachingTheSameStreamAreTheSameValue()
    {
        Assert.Equal(
            TuningParameters.Bs(15, new TransportStreamId(0x40F1)),
            TuningParameters.Bs(15, new TransportStreamId(0x40F1)));
        Assert.NotEqual(
            TuningParameters.Bs(15, new TransportStreamId(0x40F1)),
            TuningParameters.Bs(15, new TransportStreamId(0x40F2)));
    }

    [Fact]
    public void ATerrestrialChannelNumberIsNotAlsoASatelliteOne()
    {
        Assert.NotEqual(TuningParameters.Terrestrial(24), TuningParameters.Cs110(24));
    }
}
