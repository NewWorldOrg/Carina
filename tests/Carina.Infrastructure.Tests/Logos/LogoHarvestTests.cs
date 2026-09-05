using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Infrastructure.Logos;

namespace Carina.Infrastructure.Tests.Logos;

public sealed class LogoHarvestTests
{
    private const int SomeNetworkId = 32736;
    private const int SomeTransportStreamId = 32737;
    private const int SomeServiceId = 1024;
    private const int AnotherServiceId = 1025;
    private const int SomeLogoId = 261;
    private const int LargestLogoType = 0x05;

    [Fact]
    public void APictureAndTheServicesThatUseItAreBothReadOffTheOneTransport()
    {
        var harvest = new LogoHarvest();

        harvest.Push(Carrying(
            Cdt(SomeLogoId, 64, 36),
            Sdt(
                (SomeServiceId, SiDescriptorWriter.LogoNamedOnly(SomeLogoId)),
                (AnotherServiceId, SiDescriptorWriter.LogoNamedOnly(SomeLogoId)))));

        HarvestedLogo logo = Assert.Single(harvest.Logos);
        Assert.Equal(SomeNetworkId, logo.NetworkId);
        Assert.Equal(SomeLogoId, logo.LogoId);
        Assert.Equal(64, logo.Image.Width);

        Assert.Equal(
            [SomeLogoId, SomeLogoId],
            harvest.Links.OrderBy(link => link.ServiceId).Select(link => link.LogoId));
    }

    [Fact]
    public void AStationThatSendsAStringInsteadOfAPictureIsRememberedAsHavingNone()
    {
        var harvest = new LogoHarvest();

        harvest.Push(Carrying(
            [],
            Sdt((SomeServiceId, SiDescriptorWriter.LogoAsACharacterString(new AribTextWriter().Kanji("試験").ToArray())))));

        HarvestedLogoLink link = Assert.Single(harvest.Links);
        Assert.Equal(SomeServiceId, link.ServiceId);
        Assert.Null(link.LogoId);
    }

    [Fact]
    public void AStationThatSaysNothingAboutALogoIsNotRememberedEitherWay()
    {
        var harvest = new LogoHarvest();

        harvest.Push(Carrying([], Sdt((SomeServiceId, []))));

        Assert.Empty(harvest.Links);
    }

    [Fact]
    public void ALargerDrawingOfTheSameLogoTakesTheSmallerOnesPlace()
    {
        var harvest = new LogoHarvest();

        harvest.Push(Carrying(Cdt(SomeLogoId, 48, 24), []));
        harvest.Push(Carrying(Cdt(SomeLogoId, 64, 36), []));

        Assert.Equal(64, Assert.Single(harvest.Logos).Image.Width);
    }

    [Fact]
    public void ASmallerDrawingArrivingLaterDoesNotDisplaceTheLargerOneAlreadyRead()
    {
        var harvest = new LogoHarvest();

        harvest.Push(Carrying(Cdt(SomeLogoId, 64, 36), []));
        harvest.Push(Carrying(Cdt(SomeLogoId, 48, 24), []));

        Assert.Equal(64, Assert.Single(harvest.Logos).Image.Width);
    }

    [Fact]
    public void TheReadIsOverOnceEveryServiceOnTheTransportIsAccountedFor()
    {
        var harvest = new LogoHarvest();
        ServiceId[] onTheTransport = [new ServiceId(SomeServiceId), new ServiceId(AnotherServiceId)];

        harvest.Push(Carrying(
            Cdt(SomeLogoId, 64, 36),
            Sdt((SomeServiceId, SiDescriptorWriter.LogoNamedOnly(SomeLogoId)))));

        Assert.False(harvest.EverythingOnTheTransportIsAccountedFor(onTheTransport));

        harvest.Push(Carrying([], Sdt((AnotherServiceId, SiDescriptorWriter.LogoAsACharacterString([])))));

        Assert.True(harvest.EverythingOnTheTransportIsAccountedFor(onTheTransport));
    }

    [Fact]
    public void AServiceNamingALogoNobodyHasSeenYetKeepsTheReadOpen()
    {
        var harvest = new LogoHarvest();

        harvest.Push(Carrying([], Sdt((SomeServiceId, SiDescriptorWriter.LogoNamedOnly(SomeLogoId)))));

        Assert.False(harvest.EverythingOnTheTransportIsAccountedFor([new ServiceId(SomeServiceId)]));
    }

    [Fact]
    public void ASectionSplitAcrossTwoReadsIsStillReadWhole()
    {
        var harvest = new LogoHarvest();
        byte[] stream = Carrying(Cdt(SomeLogoId, 64, 36), []);

        harvest.Push(stream.AsSpan(0, 100));
        harvest.Push(stream.AsSpan(100));

        Assert.Single(harvest.Logos);
    }

    [Fact]
    public void ASectionWhoseChecksumDoesNotAddUpLeavesNothingBehind()
    {
        var harvest = new LogoHarvest();

        harvest.Push(Carrying(Cdt(SomeLogoId, 64, 36, corrupt: true), []));

        Assert.Empty(harvest.Logos);
    }

    private static byte[] Cdt(int logoId, int width, int height, bool corrupt = false)
        => new SectionWriter
        {
            TableId = CommonDataTable.TableId,
            TableIdExtension = 1,
            Body = new CdtWriter
            {
                OriginalNetworkId = SomeNetworkId,
                DataModule = CdtWriter.LogoModule(
                    LargestLogoType,
                    logoId,
                    3,
                    new LogoPngWriter { Width = width, Height = height }.ToBytes()),
            }.ToBody(),
            CorruptChecksum = corrupt,
        }.ToBytes();

    private static byte[] Sdt(params (int ServiceId, byte[] Descriptors)[] services)
        => new SectionWriter
        {
            TableId = ServiceDescriptionTable.ActualStreamTableId,
            TableIdExtension = SomeTransportStreamId,
            Body = new SdtWriter
            {
                OriginalNetworkId = SomeNetworkId,
                Services =
                [
                    .. services.Select(service => SdtWriter.Service(service.ServiceId, service.Descriptors)),
                ],
            }.ToBody(),
        }.ToBytes();

    private static byte[] Carrying(byte[] commonData, byte[] descriptions)
    {
        var stream = new List<byte>();

        if (commonData.Length > 0)
        {
            stream.AddRange(new TransportStreamWriter(CommonDataTable.Pid).Sections(commonData).Bytes);
        }

        if (descriptions.Length > 0)
        {
            stream.AddRange(new TransportStreamWriter(ServiceDescriptionTable.Pid).Sections(descriptions).Bytes);
        }

        return stream.ToArray();
    }
}
