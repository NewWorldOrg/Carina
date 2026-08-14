using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class DvbLayoutTests
{
    [Fact]
    public void APropertyRecordIsSeventySixBytesBecauseTheKernelPacksIt()
    {
        Assert.Equal(76, DvbLayout.PropertyBytes);
    }

    [Fact]
    public void ThePayloadSitsAfterTheCommandAndItsThreeReservedWords()
    {
        Assert.Equal(0, DvbLayout.PropertyCommandAt);
        Assert.Equal(16, DvbLayout.PropertyPayloadAt);
    }

    [Fact]
    public void TheResultWordIsTheLastFourBytesOfTheRecord()
    {
        Assert.Equal(72, DvbLayout.PropertyResultAt);
        Assert.Equal(DvbLayout.PropertyBytes, DvbLayout.PropertyResultAt + 4);
    }

    [Fact]
    public void EachStatisticLayerIsAScaleByteFollowedByAnEightByteValue()
    {
        Assert.Equal(9, DvbLayout.StatisticBytes);
        Assert.Equal(16, DvbLayout.StatisticCountAt);
        Assert.Equal(17, DvbLayout.StatisticsAt);
        Assert.Equal(4, DvbLayout.MaxStatisticLayers);
    }

    [Fact]
    public void TheFourStatisticLayersFitInsideThePayload()
    {
        var used = 1 + (DvbLayout.MaxStatisticLayers * DvbLayout.StatisticBytes);

        Assert.True(used <= DvbLayout.PayloadBytes);
    }

    [Fact]
    public void TheBufferLengthFollowsThirtyTwoBytesOfBufferData()
    {
        Assert.Equal(16, DvbLayout.BufferDataAt);
        Assert.Equal(32, DvbLayout.BufferDataBytes);
        Assert.Equal(48, DvbLayout.BufferLengthAt);
    }

    [Fact]
    public void ThePayloadIsAsWideAsItsWidestMember()
    {
        Assert.Equal(56, DvbLayout.PayloadBytes);
    }

    [Fact]
    public void ThePropertyListHeaderIsACountThenAPointer()
    {
        Assert.Equal(0, DvbLayout.PropertyListCountAt);
        Assert.Equal(8, DvbLayout.PropertyListPointerAt);
        Assert.Equal(16, DvbLayout.PropertyListHeaderBytes);
    }

    [Fact]
    public void TheFrontendInfoBlockIsANameThenTenWords()
    {
        Assert.Equal(168, DvbLayout.FrontendInfoBytes);
        Assert.Equal(128, DvbLayout.FrontendNameBytes);
    }

    [Fact]
    public void ThePesFilterBlockIsTwentyBytes()
    {
        Assert.Equal(20, DvbLayout.PesFilterBytes);
        Assert.Equal(0, DvbLayout.PesFilterPidAt);
        Assert.Equal(4, DvbLayout.PesFilterInputAt);
        Assert.Equal(8, DvbLayout.PesFilterOutputAt);
        Assert.Equal(12, DvbLayout.PesFilterPesTypeAt);
        Assert.Equal(16, DvbLayout.PesFilterFlagsAt);
    }

    [Fact]
    public void APollRecordIsADescriptorAndTwoFlagWords()
    {
        Assert.Equal(8, DvbLayout.PollBytes);
        Assert.Equal(4, DvbLayout.PollEventsAt);
        Assert.Equal(6, DvbLayout.PollReturnedEventsAt);
    }

    [Fact]
    public void TheseOffsetsOnlyClaimToDescribeSixtyFourBitLittleEndianMachines()
    {
        Assert.Equal(
            BitConverter.IsLittleEndian && Environment.Is64BitProcess,
            DvbLayout.DescribesThisMachine
        );
    }
}
