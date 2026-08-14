using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class DvbChannelTests
{
    private const int SyntheticStream = 50_001;


    [Theory]
    [InlineData(13)]
    [InlineData(40)]
    [InlineData(62)]
    public void TerrestrialChannelsInsideTheUhfPlanAreAccepted(int physicalChannel)
    {
        var channel = DvbChannel.Terrestrial(physicalChannel);

        Assert.Equal(physicalChannel, Assert.IsType<TerrestrialChannel>(channel).PhysicalChannel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(63)]
    [InlineData(-1)]
    public void TerrestrialChannelsOutsideTheUhfPlanAreRefusedByName(int physicalChannel)
    {
        var refusal = Assert.Throws<DvbDeviceException>(
            () => DvbChannel.Terrestrial(physicalChannel)
        );

        Assert.Contains(physicalChannel.ToString(), refusal.Message, StringComparison.Ordinal);
        Assert.Contains("13", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("62", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(23)]
    public void OddBroadcastSatelliteSlotsAreAccepted(int slot)
    {
        var channel = DvbChannel.BroadcastSatellite(slot, SyntheticStream);

        Assert.Equal(slot, Assert.IsType<BroadcastSatelliteChannel>(channel).Slot);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(17)]
    public void TheTwoBroadcastSatelliteSlotsTheDemodulatorCannotUseAreRefused(int slot)
    {
        var refusal = Assert.Throws<DvbDeviceException>(
            () => DvbChannel.BroadcastSatellite(slot, SyntheticStream)
        );

        Assert.Contains(slot.ToString(), refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(24)]
    [InlineData(25)]
    public void EvenOrOutOfRangeBroadcastSatelliteSlotsAreRefused(int slot)
    {
        Assert.Throws<DvbDeviceException>(
            () => DvbChannel.BroadcastSatellite(slot, SyntheticStream)
        );
    }

    [Theory]
    [InlineData(2)]
    [InlineData(12)]
    [InlineData(24)]
    public void EvenCommunicationSatelliteSlotsAreAccepted(int slot)
    {
        var channel = DvbChannel.CommunicationSatellite(slot);

        Assert.Equal(slot, Assert.IsType<CommunicationSatelliteChannel>(channel).Slot);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(26)]
    public void OddOrOutOfRangeCommunicationSatelliteSlotsAreRefused(int slot)
    {
        Assert.Throws<DvbDeviceException>(() => DvbChannel.CommunicationSatellite(slot));
    }

    [Fact]
    public void ABroadcastSatelliteChannelCarriesTheStreamItWasToldToTake()
    {
        var channel = Assert.IsType<BroadcastSatelliteChannel>(
            DvbChannel.BroadcastSatellite(1, SyntheticStream)
        );

        Assert.Equal(SyntheticStream, channel.TransportStreamId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(23)]
    public void ABroadcastSatelliteSlotThatNamesNoStreamIsRefusedBecauseTwoStreamsShareASlot(
        int slot
    )
    {
        var refusal = Assert.Throws<DvbDeviceException>(
            () => DvbChannel.BroadcastSatellite(slot, transportStreamId: null)
        );

        Assert.Contains("transportStreamId", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(slot.ToString(), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRefusalToTuneAnUnnamedStreamSaysWhatWouldOtherwiseComeBack()
    {
        var refusal = Assert.Throws<DvbDeviceException>(
            () => DvbChannel.BroadcastSatellite(15, transportStreamId: null)
        );

        Assert.Contains("first", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATransportStreamIdOutsideSixteenBitsIsRefused()
    {
        Assert.Throws<DvbDeviceException>(() => DvbChannel.BroadcastSatellite(1, 0x1_0000));
        Assert.Throws<DvbDeviceException>(() => DvbChannel.BroadcastSatellite(1, -1));
    }

    [Fact]
    public void EveryChannelCanSayWhichAerialItNeeds()
    {
        Assert.False(DvbChannel.Terrestrial(55).NeedsSatelliteAerial);
        Assert.True(DvbChannel.BroadcastSatellite(1, SyntheticStream).NeedsSatelliteAerial);
        Assert.True(DvbChannel.CommunicationSatellite(2).NeedsSatelliteAerial);
    }
}
