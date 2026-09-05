using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Tables;

public sealed class CommonDataTableTests
{
    private const int SomeNetworkId = 32736;
    private const int SomeDownloadDataId = 0x0001;
    private const int SomeLogoId = 261;
    private const int SomeLogoVersion = 7;
    private const int LargestLogoType = 0x05;

    [Fact]
    public void TheTableHandsBackTheLogoItCarriesAndTheNetworkItBelongsTo()
    {
        CommonDataTable table = Parse(new CdtWriter
        {
            OriginalNetworkId = SomeNetworkId,
            DataModule = CdtWriter.LogoModule(LargestLogoType, SomeLogoId, SomeLogoVersion, AsBroadcast(64, 36)),
        });

        Assert.Equal(SomeNetworkId, table.OriginalNetworkId);
        Assert.Equal(SomeDownloadDataId, table.DownloadDataId);
        Assert.True(table.CarriesALogo);

        CarriedLogo logo = Assert.IsType<CarriedLogo>(table.Logo);
        Assert.Equal(LargestLogoType, logo.LogoType);
        Assert.Equal(SomeLogoId, logo.LogoId);
        Assert.Equal(SomeLogoVersion, logo.LogoVersion);
        Assert.True(logo.IsAPicture);
        Assert.Equal(64, logo.Image!.Width);
        Assert.Equal(36, logo.Image.Height);
    }

    [Fact]
    public void ALogoIdAboveTheEighthBitSurvivesTheSevenReservedBitsBesideIt()
    {
        CommonDataTable table = Parse(new CdtWriter
        {
            OriginalNetworkId = SomeNetworkId,
            DataModule = CdtWriter.LogoModule(LargestLogoType, 511, 4095, AsBroadcast(48, 24)),
        });

        Assert.Equal(511, table.Logo!.LogoId);
        Assert.Equal(4095, table.Logo.LogoVersion);
    }

    [Fact]
    public void TheLogoComesBackWithTheColoursTheBroadcastLeavesOutSoItIsAPictureRatherThanAFile()
    {
        byte[] asBroadcast = AsBroadcast(64, 36);

        CommonDataTable table = Parse(new CdtWriter
        {
            OriginalNetworkId = SomeNetworkId,
            DataModule = CdtWriter.LogoModule(LargestLogoType, SomeLogoId, SomeLogoVersion, asBroadcast),
        });

        Assert.Equal(asBroadcast, table.Logo!.AsBroadcast.ToArray());
        Assert.True(table.Logo.Image!.Bytes.Length > asBroadcast.Length);
        Assert.Contains("PLTE", Latin(table.Logo.Image.Bytes.ToArray()), StringComparison.Ordinal);
        Assert.Contains("tRNS", Latin(table.Logo.Image.Bytes.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public void ADescriptorBeforeTheLogoIsStillCarriedAndDoesNotMoveWhereTheLogoStarts()
    {
        CommonDataTable table = Parse(new CdtWriter
        {
            OriginalNetworkId = SomeNetworkId,
            Descriptors = SiDescriptorWriter.LogoNamedOnly(SomeLogoId),
            DataModule = CdtWriter.LogoModule(LargestLogoType, SomeLogoId, SomeLogoVersion, AsBroadcast(48, 24)),
        });

        Assert.Single(table.Descriptors);
        Assert.Equal(DescriptorTags.LogoTransmission, table.Descriptors[0].Tag);
        Assert.Equal(SomeLogoId, table.Logo!.LogoId);
    }

    [Fact]
    public void SomethingOtherThanALogoIsCarriedWholeRatherThanReadAsOne()
    {
        CommonDataTable table = Parse(new CdtWriter
        {
            OriginalNetworkId = SomeNetworkId,
            DataType = 0x02,
            DataModule = [1, 2, 3, 4],
        });

        Assert.False(table.CarriesALogo);
        Assert.Null(table.Logo);
        Assert.Equal<byte[]>([1, 2, 3, 4], table.DataModule.ToArray());
    }

    [Fact]
    public void APictureThatIsNotAPngIsCarriedAsNoPictureRatherThanRefusingTheWholeTable()
    {
        CommonDataTable table = Parse(new CdtWriter
        {
            OriginalNetworkId = SomeNetworkId,
            DataModule = CdtWriter.LogoModule(LargestLogoType, SomeLogoId, SomeLogoVersion, [0x00, 0x01, 0x02]),
        });

        Assert.False(table.Logo!.IsAPicture);
        Assert.Null(table.Logo.Image);
    }

    [Theory]
    [InlineData(ServiceDescriptionTable.ActualStreamTableId)]
    [InlineData(NetworkInformationTable.ActualNetworkTableId)]
    public void ASectionThatIsNotTheCommonDataTableIsRefused(int tableId)
    {
        Assert.Equal(TableDefect.WrongTableId, Rejection(Read(new CdtWriter(), tableId)));
    }

    [Fact]
    public void ASectionTooShortForTheFixedFieldsIsRefused()
    {
        Assert.Equal(
            TableDefect.SectionTooShort,
            Rejection(CommonDataTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = CommonDataTable.TableId,
                TableIdExtension = SomeDownloadDataId,
                Body = [0x7F, 0xE0, 0x01],
            }))));
    }

    [Fact]
    public void ADescriptorLoopLongerThanTheSectionRefusesTheWholeTable()
    {
        Assert.Equal(
            TableDefect.LoopOverrun,
            Rejection(Read(new CdtWriter
            {
                OriginalNetworkId = SomeNetworkId,
                DataType = 0x02,
                DeclaredDescriptorsLength = 64,
            })));
    }

    [Fact]
    public void ADescriptorWhoseLengthOverrunsTheLoopRefusesTheWholeTable()
    {
        Assert.Equal(
            TableDefect.MalformedDescriptor,
            Rejection(Read(new CdtWriter
            {
                OriginalNetworkId = SomeNetworkId,
                Descriptors = DescriptorWriter.Overrunning(DescriptorTags.LogoTransmission, 8, 0x02, 0xFE, 0x05),
            })));
    }

    [Fact]
    public void ALogoDeclaringMoreBytesThanTheSectionHoldsIsRefusedRatherThanHalfRead()
    {
        Assert.Equal(
            TableDefect.DataModuleOverrun,
            Rejection(Read(new CdtWriter
            {
                OriginalNetworkId = SomeNetworkId,
                DataModule = CdtWriter.LogoModule(
                    LargestLogoType,
                    SomeLogoId,
                    SomeLogoVersion,
                    AsBroadcast(48, 24),
                    declaredSize: 4096),
            })));
    }

    [Fact]
    public void ALogoModuleCutShortOfItsOwnHeaderIsRefusedRatherThanHalfRead()
    {
        Assert.Equal(
            TableDefect.DataModuleOverrun,
            Rejection(Read(new CdtWriter
            {
                OriginalNetworkId = SomeNetworkId,
                DataModule = [LargestLogoType, 0xFE, 0x05, 0xF0],
            })));
    }

    [Fact]
    public void ASectionWhoseChecksumDoesNotAddUpNeverReachesTheTableAtAll()
    {
        var assembler = new SectionAssembler(CommonDataTable.Pid);

        IReadOnlyList<SectionRead> reads = new TransportStreamWriter(CommonDataTable.Pid)
            .Sections(new SectionWriter
            {
                TableId = CommonDataTable.TableId,
                TableIdExtension = SomeDownloadDataId,
                Body = new CdtWriter { OriginalNetworkId = SomeNetworkId }.ToBody(),
                CorruptChecksum = true,
            }.ToBytes())
            .Packets
            .SelectMany(packet => assembler.Push(packet))
            .ToArray();

        Assert.Equal(
            SectionDefect.ChecksumMismatch,
            Assert.IsType<SectionRead.Rejected>(Assert.Single(reads)).Defect);
    }

    [Fact]
    public void AVersionChangeIsCarriedThroughSoTheCallerCanTellTheTablesApart()
    {
        var writer = new CdtWriter { OriginalNetworkId = SomeNetworkId, DataType = 0x02 };

        Assert.Equal(2, Parse(writer, version: 2).VersionNumber);
        Assert.Equal(3, Parse(writer, version: 3).VersionNumber);
    }

    private static byte[] AsBroadcast(int width, int height)
        => new LogoPngWriter { Width = width, Height = height, Index = 7 }.ToBytes();

    private static string Latin(byte[] bytes) => string.Concat(bytes.Select(octet => (char)octet));

    private static CommonDataTable Parse(CdtWriter writer, int version = 1)
        => Assert.IsType<TableRead<CommonDataTable>.Parsed>(
            Read(writer, CommonDataTable.TableId, version)).Table;

    private static TableDefect Rejection(TableRead<CommonDataTable> read)
        => Assert.IsType<TableRead<CommonDataTable>.Rejected>(read).Defect;

    private static TableRead<CommonDataTable> Read(
        CdtWriter writer,
        int tableId = CommonDataTable.TableId,
        int version = 1)
        => CommonDataTable.Read(CarriedSection.Of(new SectionWriter
        {
            TableId = tableId,
            TableIdExtension = SomeDownloadDataId,
            VersionNumber = version,
            Body = writer.ToBody(),
        }));
}
