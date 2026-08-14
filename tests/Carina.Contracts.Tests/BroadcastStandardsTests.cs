namespace Carina.Contracts.Tests;

public sealed class BroadcastStandardsTests
{
    [Theory]
    [InlineData(13, 473_142_857L)]
    [InlineData(27, 557_142_857L)]
    [InlineData(62, 767_142_857L)]
    public void ATerrestrialChannelSitsOnItsAgreedCentreInHertz(int channel, long hz)
    {
        Assert.Equal(hz, BroadcastStandards.TerrestrialCentreHz(channel));
    }

    [Theory]
    [InlineData(1, 1_049_480L)]
    [InlineData(15, 1_318_000L)]
    [InlineData(23, 1_471_440L)]
    public void ABsSlotSitsOnItsAgreedCentreInKilohertz(int channel, long kHz)
    {
        Assert.Equal(kHz, BroadcastStandards.BsCentreKHz(channel));
    }

    [Theory]
    [InlineData(2, 1_613_000L)]
    [InlineData(4, 1_653_000L)]
    [InlineData(24, 2_053_000L)]
    public void ACs110SlotSitsOnItsAgreedCentreInKilohertz(int channel, long kHz)
    {
        Assert.Equal(kHz, BroadcastStandards.Cs110CentreKHz(channel));
    }

    [Fact]
    public void TheTwoFamiliesKeepTheirOwnUnit()
    {
        Assert.Equal(6_000_000L, BroadcastStandards.TerrestrialChannelSpacingHz);
        Assert.Equal(19_180L, BroadcastStandards.BsSlotSpacingKHz);
        Assert.Equal(20_000L, BroadcastStandards.Cs110SlotSpacingKHz);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(63)]
    [InlineData(0)]
    public void AFrequencyIsNotDerivedForAChannelOutsideTheRange(int channel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BroadcastStandards.TerrestrialCentreHz(channel)
        );
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(17)]
    [InlineData(25)]
    public void AFrequencyIsNotDerivedForABsSlotOutsideTheRange(int channel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BroadcastStandards.BsCentreKHz(channel));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(26)]
    public void AFrequencyIsNotDerivedForACs110SlotOutsideTheRange(int channel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BroadcastStandards.Cs110CentreKHz(channel));
    }

    [Theory]
    [InlineData(13, true)]
    [InlineData(62, true)]
    [InlineData(12, false)]
    [InlineData(63, false)]
    public void TheTerrestrialRangeIsThirteenToSixtyTwo(int channel, bool inRange)
    {
        Assert.Equal(inRange, BroadcastStandards.IsTerrestrialChannel(channel));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(23, true)]
    [InlineData(2, false)]
    [InlineData(7, false)]
    [InlineData(17, false)]
    [InlineData(25, false)]
    public void TheBsRangeIsTheOddSlotsWithADemodulator(int channel, bool inRange)
    {
        Assert.Equal(inRange, BroadcastStandards.IsBsChannel(channel));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(24, true)]
    [InlineData(3, false)]
    [InlineData(0, false)]
    [InlineData(26, false)]
    public void TheCs110RangeIsTheEvenSlots(int channel, bool inRange)
    {
        Assert.Equal(inRange, BroadcastStandards.IsCs110Channel(channel));
    }

    [Fact]
    public void TheSlotsWithoutADemodulatorAreNamed()
    {
        Assert.Equal(new[] { 7, 17 }, BroadcastStandards.BsChannelsWithoutDemodulation);
    }
}
