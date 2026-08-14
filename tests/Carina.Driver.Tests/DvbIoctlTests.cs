using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class DvbIoctlTests
{
    [Fact]
    public void AnArgumentlessCommandCarriesNeitherDirectionNorSize()
    {
        Assert.Equal(0x00006F2Au, DvbIoctl.DemuxStop);
        Assert.Equal(0x00006F29u, DvbIoctl.DemuxStart);
        Assert.Equal(0x00006F2Du, DvbIoctl.DemuxSetBufferSize);
    }

    [Fact]
    public void SettingTheLnbVoltageTakesItsArgumentByValue()
    {
        Assert.Equal(0x00006F43u, DvbIoctl.FrontendSetVoltage);
    }

    [Fact]
    public void ReadingTheFrontendStatusAsksForFourBytesBack()
    {
        Assert.Equal(0x80046F45u, DvbIoctl.FrontendReadStatus);
    }

    [Fact]
    public void ReadingTheFrontendInfoAsksForOneHundredSixtyEightBytesBack()
    {
        Assert.Equal(0x80A86F3Du, DvbIoctl.FrontendGetInfo);
    }

    [Fact]
    public void ThePropertyIoctlsCarryTheSixteenByteListHeader()
    {
        Assert.Equal(0x40106F52u, DvbIoctl.FrontendSetProperty);
        Assert.Equal(0x80106F53u, DvbIoctl.FrontendGetProperty);
    }

    [Fact]
    public void TheDemuxFilterIoctlCarriesTheTwentyByteFilterBlock()
    {
        Assert.Equal(0x40146F2Cu, DvbIoctl.DemuxSetPesFilter);
    }

    [Fact]
    public void EveryDvbRequestNamesTheDvbCharacterAsItsType()
    {
        uint[] requests =
        [
            DvbIoctl.DemuxStart,
            DvbIoctl.DemuxStop,
            DvbIoctl.DemuxSetBufferSize,
            DvbIoctl.DemuxSetPesFilter,
            DvbIoctl.FrontendGetInfo,
            DvbIoctl.FrontendGetProperty,
            DvbIoctl.FrontendReadStatus,
            DvbIoctl.FrontendSetProperty,
            DvbIoctl.FrontendSetVoltage,
        ];

        Assert.All(requests, request => Assert.Equal((uint)'o', (request >> 8) & 0xFF));
    }

    [Fact]
    public void TheSizeFieldOfAReadRequestMatchesTheBlockItFills()
    {
        Assert.Equal(
            (uint)DvbLayout.FrontendInfoBytes,
            (DvbIoctl.FrontendGetInfo >> 16) & 0x3FFF
        );
        Assert.Equal(
            (uint)DvbLayout.PropertyListHeaderBytes,
            (DvbIoctl.FrontendSetProperty >> 16) & 0x3FFF
        );
        Assert.Equal((uint)DvbLayout.PesFilterBytes, (DvbIoctl.DemuxSetPesFilter >> 16) & 0x3FFF);
    }
}
