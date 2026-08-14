using System.Buffers.Binary;

using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class DemuxFilterTests
{
    [Fact]
    public void TheFilterAsksForEveryPacketRatherThanOneStream()
    {
        var filter = DemuxFilter.EverythingFromTheFrontend();

        Assert.Equal(
            0x2000,
            BinaryPrimitives.ReadUInt16LittleEndian(filter.AsSpan(DvbLayout.PesFilterPidAt))
        );
    }

    [Fact]
    public void TheFilterTakesItsInputFromTheFrontendAndNotFromTheReader()
    {
        var filter = DemuxFilter.EverythingFromTheFrontend();

        Assert.Equal(
            0u,
            BinaryPrimitives.ReadUInt32LittleEndian(filter.AsSpan(DvbLayout.PesFilterInputAt))
        );
    }

    [Fact]
    public void TheFilterRoutesItsOutputToTheTransportStreamReader()
    {
        var filter = DemuxFilter.EverythingFromTheFrontend();

        Assert.Equal(
            2u,
            BinaryPrimitives.ReadUInt32LittleEndian(filter.AsSpan(DvbLayout.PesFilterOutputAt))
        );
    }

    [Fact]
    public void TheFilterDoesNotAskTheDemuxToInterpretThePayload()
    {
        var filter = DemuxFilter.EverythingFromTheFrontend();

        Assert.Equal(
            20u,
            BinaryPrimitives.ReadUInt32LittleEndian(filter.AsSpan(DvbLayout.PesFilterPesTypeAt))
        );
    }

    [Fact]
    public void TheFilterStartsWithoutASecondCall()
    {
        var filter = DemuxFilter.EverythingFromTheFrontend();

        Assert.Equal(
            4u,
            BinaryPrimitives.ReadUInt32LittleEndian(filter.AsSpan(DvbLayout.PesFilterFlagsAt))
        );
    }

    [Fact]
    public void TheFilterIsExactlyAsLongAsTheKernelBlock()
    {
        Assert.Equal(DvbLayout.PesFilterBytes, DemuxFilter.EverythingFromTheFrontend().Length);
    }
}
