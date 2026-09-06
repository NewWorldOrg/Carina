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
    private const int AnotherLogoId = 262;
    private const int ASmallPictureType = 0x01;
    private const int APictureTypeInTheMiddle = 0x03;
    private const int TheHighestPictureType = 0x05;

    [Fact]
    public void APictureAndTheServicesThatUseItAreBothReadOffTheOneTransport()
    {
        var harvest = new LogoHarvest();

        harvest.Push(Carrying(
            [Cdt(SomeLogoId, 64, 36)],
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

        harvest.Push(Carrying([Cdt(SomeLogoId, 48, 24)], []));
        harvest.Push(Carrying([Cdt(SomeLogoId, 64, 36)], []));

        Assert.Equal(64, Assert.Single(harvest.Logos).Image.Width);
    }

    [Fact]
    public void ASmallerDrawingArrivingLaterDoesNotDisplaceTheLargerOneAlreadyRead()
    {
        var harvest = new LogoHarvest();

        harvest.Push(Carrying([Cdt(SomeLogoId, 64, 36)], []));
        harvest.Push(Carrying([Cdt(SomeLogoId, 48, 24)], []));

        Assert.Equal(64, Assert.Single(harvest.Logos).Image.Width);
    }

    [Fact]
    public void ThePictureWithTheHighestTypeNumberIsNotTheOneKeptWhenAnotherTypeIsDrawnLarger()
    {
        var harvest = new LogoHarvest();

        harvest.Push(Carrying([Cdt(SomeLogoId, 64, 36, logoType: TheHighestPictureType)], []));
        harvest.Push(Carrying([Cdt(SomeLogoId, 72, 36, logoType: APictureTypeInTheMiddle)], []));

        HarvestedLogo kept = Assert.Single(harvest.Logos);
        Assert.Equal(72, kept.Image.Width);
        Assert.Equal(APictureTypeInTheMiddle, kept.LogoType);
    }

    [Fact]
    public void TheReadIsOverOnceEveryServiceOnTheTransportIsAccountedFor()
    {
        var harvest = new LogoHarvest();
        ServiceId[] onTheTransport = [new ServiceId(SomeServiceId), new ServiceId(AnotherServiceId)];

        harvest.Push(Carrying(
            PicturesAt(CarriedLogo.EveryPictureType),
            Sdt((SomeServiceId, SiDescriptorWriter.LogoNamedOnly(SomeLogoId)))));

        Assert.False(harvest.ThereIsNothingLeftToWaitFor(onTheTransport));

        harvest.Push(Carrying([], Sdt((AnotherServiceId, SiDescriptorWriter.LogoAsACharacterString([])))));

        Assert.True(harvest.ThereIsNothingLeftToWaitFor(onTheTransport));
    }

    [Fact]
    public void AServiceNamingALogoNobodyHasSeenYetKeepsTheReadOpen()
    {
        var harvest = new LogoHarvest();

        harvest.Push(Carrying([], Sdt((SomeServiceId, SiDescriptorWriter.LogoNamedOnly(SomeLogoId)))));

        Assert.False(harvest.ThereIsNothingLeftToWaitFor([new ServiceId(SomeServiceId)]));
    }

    [Fact]
    public void ALogoOfferedAtEveryPictureTypeTheStandardDefinesEndsTheRead()
    {
        var harvest = new LogoHarvest();

        harvest.Push(Carrying(
            PicturesAt(CarriedLogo.EveryPictureType),
            Sdt((SomeServiceId, SiDescriptorWriter.LogoNamedOnly(SomeLogoId)))));

        Assert.True(harvest.ThereIsNothingLeftToWaitFor([new ServiceId(SomeServiceId)]));
    }

    [Fact]
    public void ALogoOfferedAtEveryTypeButOneKeepsTheReadOpenEvenWithTheHighestTypeAmongThem()
    {
        var harvest = new LogoHarvest();

        harvest.Push(Carrying(
            PicturesAt(CarriedLogo.EveryPictureType.Where(type => type != APictureTypeInTheMiddle)),
            Sdt((SomeServiceId, SiDescriptorWriter.LogoNamedOnly(SomeLogoId)))));

        Assert.False(harvest.ThereIsNothingLeftToWaitFor([new ServiceId(SomeServiceId)]));

        harvest.Push(Carrying([Cdt(SomeLogoId, 72, 36, logoType: APictureTypeInTheMiddle)], []));

        Assert.True(harvest.ThereIsNothingLeftToWaitFor([new ServiceId(SomeServiceId)]));
    }

    [Fact]
    public void OneLogoFinishedDoesNotEndTheReadWhileAnotherOnTheSameTransportIsStillComing()
    {
        var harvest = new LogoHarvest();
        ServiceId[] onTheTransport = [new ServiceId(SomeServiceId), new ServiceId(AnotherServiceId)];

        harvest.Push(Carrying(
            [.. PicturesAt(CarriedLogo.EveryPictureType), Cdt(AnotherLogoId, 36, 24)],
            Sdt(
                (SomeServiceId, SiDescriptorWriter.LogoNamedOnly(SomeLogoId)),
                (AnotherServiceId, SiDescriptorWriter.LogoNamedOnly(AnotherLogoId)))));

        Assert.False(harvest.ThereIsNothingLeftToWaitFor(onTheTransport));
    }

    [Fact]
    public void ATransportWhereNobodyBroadcastsAPictureIsNotWaitedOutForOne()
    {
        var harvest = new LogoHarvest();

        harvest.Push(Carrying(
            [],
            Sdt((SomeServiceId, SiDescriptorWriter.LogoAsACharacterString([])))));

        Assert.True(harvest.ThereIsNothingLeftToWaitFor([new ServiceId(SomeServiceId)]));
    }

    [Fact]
    public void ASectionSplitAcrossTwoReadsIsStillReadWhole()
    {
        var harvest = new LogoHarvest();
        byte[] stream = Carrying([Cdt(SomeLogoId, 64, 36)], []);

        harvest.Push(stream.AsSpan(0, 100));
        harvest.Push(stream.AsSpan(100));

        Assert.Single(harvest.Logos);
    }

    [Fact]
    public void ASectionWhoseChecksumDoesNotAddUpLeavesNothingBehind()
    {
        var harvest = new LogoHarvest();

        harvest.Push(Carrying([Cdt(SomeLogoId, 64, 36, corrupt: true)], []));

        Assert.Empty(harvest.Logos);
    }

    private static byte[][] PicturesAt(IEnumerable<int> types)
        => [.. types.Select(type => Cdt(SomeLogoId, 36, 24, logoType: type))];

    private static byte[] Cdt(
        int logoId,
        int width,
        int height,
        bool corrupt = false,
        int logoType = ASmallPictureType)
        => new SectionWriter
        {
            TableId = CommonDataTable.TableId,
            TableIdExtension = 1,
            Body = new CdtWriter
            {
                OriginalNetworkId = SomeNetworkId,
                DataModule = CdtWriter.LogoModule(
                    logoType,
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

    private static byte[] Carrying(byte[][] commonData, byte[] descriptions)
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
