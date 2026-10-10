using System.Text;

using Carina.Broadcast.DsmCc;
using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.DsmCc;

public sealed class DataCarouselsTests
{
    private const int EntryTag = 0x40;

    private const int OtherTag = 0x50;

    private const int CarouselPid = 0x0140;

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

    [Fact(DisplayName = "BR-BD-002: a carousel written into transport packets comes out as resources with the document in UTF-8")]
    public void ACarouselWrittenIntoTransportPacketsComesOutAsResourcesWithTheDocumentInUtf8()
    {
        byte[] bml = [.. "<bml><body><p>"u8, 0xC5, 0xB7, 0xFA, 0xA1, .. "</p></body></bml>"u8];
        byte[] entity = EntityWriter.Multipart(
            "part",
            new EntityPart("startup.bml", EntityWriter.BmlType, bml),
            new EntityPart("logo.png", EntityWriter.PngType, Png));
        byte[] compressed = EntityWriter.Zlib(entity);
        List<byte[]> sections =
        [
            new DiiWriter { BlockSize = 64, Modules = [DiiModule.Of(0, compressed.Length, 1, ModuleDescriptorWriter.Compression(entity.Length))] }.ToSection().ToBytes(),
            .. DsmCcWriter.Blocks(1, 0, 1, compressed, 64).Select(block => block.ToBytes()),
        ];
        DataCarousels carousels = new();
        SectionReader reader = new(CarouselPid);

        IReadOnlyList<CarouselChange> changes = new TransportStreamWriter(CarouselPid)
            .Sections([.. sections])
            .Packets
            .SelectMany(packet => reader.Push(packet))
            .OfType<SectionRead.Assembled>()
            .SelectMany(assembled => carousels.Push(EntryTag, assembled.Section))
            .ToArray();

        Assert.IsType<CarouselChange.CatalogueUpdated>(changes[0]);
        CompletedModule module = Assert.Single(changes.OfType<CarouselChange.ModuleCompleted>()).Module;
        Assert.Equal(["startup.bml", "logo.png"], module.Resources.Select(resource => resource.Location));
        Assert.Equal("<bml><body><p>天⛌</p></body></bml>", Encoding.UTF8.GetString(module.Resources[0].Body.Span));
        Assert.Equal(Png, module.Resources[1].Body.ToArray());
    }

    [Fact(DisplayName = "BR-BV-001: an indication with a broken checksum or a length past the limit never reaches the carousels")]
    public void AnIndicationWithABrokenChecksumOrALengthPastTheLimitNeverReachesTheCarousels()
    {
        SectionWriter dii = new DiiWriter { Modules = [DiiModule.Of(0, 10, 1)] }.ToSection();
        SectionReader reader = new(CarouselPid);

        IReadOnlyList<SectionRead> reads = new TransportStreamWriter(CarouselPid)
            .Sections(
                new SectionWriter { TableId = dii.TableId, TableIdExtension = dii.TableIdExtension, Body = dii.Body, CorruptChecksum = true }.ToBytes(),
                new SectionWriter { TableId = dii.TableId, Body = dii.Body, DeclaredLength = Section.MaximumDeclaredLength + 1 }.ToBytes())
            .Packets
            .SelectMany(packet => reader.Push(packet))
            .ToArray();

        Assert.DoesNotContain(reads, read => read is SectionRead.Assembled);
        Assert.Contains(reads, read => read is SectionRead.Rejected { Defect: SectionDefect.ChecksumMismatch });
        Assert.Contains(reads, read => read is SectionRead.Rejected { Defect: SectionDefect.LengthOutOfRange });
    }

    [Fact(DisplayName = "BR-BV-001: an indication that cannot be read is reported with the reason")]
    public void AnIndicationThatCannotBeReadIsReportedWithTheReason()
    {
        DataCarousels carousels = new();

        IReadOnlyList<CarouselChange> changes = carousels.Push(EntryTag, CarriedSection.Of(new DiiWriter
        {
            Modules = [DiiModule.Of(1, 10, 0)],
            DeclaredModuleCount = 3,
        }.ToSection()));

        CarouselChange.Unreadable unreadable = Assert.IsType<CarouselChange.Unreadable>(Assert.Single(changes));
        Assert.Equal(TableDefect.LoopOverrun, unreadable.Defect);
        Assert.Equal(EntryTag, unreadable.ComponentTag);
    }

    [Fact(DisplayName = "BR-BV-003: each component tag is a carousel of its own")]
    public void EachComponentTagIsACarouselOfItsOwn()
    {
        DataCarousels carousels = new();
        carousels.Push(EntryTag, CarriedSection.Of(Indication(1, DiiModule.Of(1, Png.Length, 0))));
        carousels.Push(OtherTag, CarriedSection.Of(Indication(1, DiiModule.Of(1, Png.Length, 3))));

        IReadOnlyList<CarouselChange> entry = carousels.Push(EntryTag, CarriedSection.Of(DsmCcWriter.Blocks(1, 1, 0, Png, 64)[0]));
        IReadOnlyList<CarouselChange> other = carousels.Push(OtherTag, CarriedSection.Of(DsmCcWriter.Blocks(1, 1, 0, Png, 64)[0]));

        Assert.Equal(EntryTag, Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(entry)).ComponentTag);
        Assert.Equal(CarouselDefect.VersionMismatch, Assert.IsType<CarouselChange.Rejected>(Assert.Single(other)).Defect);
    }

    [Fact(DisplayName = "BR-BV-003: a block on a tag with no carousel is discarded")]
    public void ABlockOnATagWithNoCarouselIsDiscarded()
    {
        DataCarousels carousels = new();

        IReadOnlyList<CarouselChange> changes = carousels.Push(OtherTag, CarriedSection.Of(DsmCcWriter.Blocks(1, 1, 0, Png, 64)[0]));

        Assert.Equal(CarouselDefect.NotInCatalogue, Assert.IsType<CarouselChange.Rejected>(Assert.Single(changes)).Defect);
    }

    [Fact(DisplayName = "BR-BD-003: stream descriptors and other tables are left to their own readers")]
    public void StreamDescriptorsAndOtherTablesAreLeftToTheirOwnReaders()
    {
        DataCarousels carousels = new();

        Assert.Empty(carousels.Push(EntryTag, CarriedSection.Of(new StreamDescriptorWriter
        {
            Descriptors = StreamDescriptorWriter.GeneralEvent(1, StreamDescriptorWriter.Immediate, 0, 0, 1),
        }.ToSection())));
        Assert.Empty(carousels.Push(EntryTag, CarriedSection.Of(new SectionWriter { TableId = 0x3E })));
    }

    [Fact(DisplayName = "BR-BV-002: the carousel past the sixteenth is dropped and the others carry on")]
    public void TheCarouselPastTheSixteenthIsDroppedAndTheOthersCarryOn()
    {
        DataCarousels carousels = new();

        for (int tag = 0; tag < CarouselLimits.Broadcast.MostCarousels; tag++)
        {
            Assert.IsType<CarouselChange.CatalogueUpdated>(Assert.Single(carousels.Push(tag, CarriedSection.Of(Indication(1, DiiModule.Of(1, Png.Length, 0))))));
        }

        IReadOnlyList<CarouselChange> seventeenth = carousels.Push(0x7F, CarriedSection.Of(Indication(1, DiiModule.Of(1, Png.Length, 0))));
        IReadOnlyList<CarouselChange> first = carousels.Push(0, CarriedSection.Of(DsmCcWriter.Blocks(1, 1, 0, Png, 64)[0]));

        CarouselChange.Dropped dropped = Assert.IsType<CarouselChange.Dropped>(Assert.Single(seventeenth));
        Assert.Equal(CarouselDefect.TooManyCarousels, dropped.Defect);
        Assert.Equal(0x7F, dropped.ComponentTag);
        Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(first));
    }

    [Fact(DisplayName = "BR-BV-002: the carousel that takes the whole past sixty-four mebibytes is dropped and the others carry on")]
    public void TheCarouselThatTakesTheWholePastSixtyFourMebibytesIsDroppedAndTheOthersCarryOn()
    {
        DataCarousels carousels = new();
        long largest = CarouselLimits.Broadcast.LargestModule;

        for (int tag = 0; tag < 4; tag++)
        {
            Assert.IsType<CarouselChange.CatalogueUpdated>(Assert.Single(carousels.Push(tag, CarriedSection.Of(Indication(1, DiiModule.Of(1, largest, 0))))));
        }

        IReadOnlyList<CarouselChange> fifth = carousels.Push(4, CarriedSection.Of(Indication(1, DiiModule.Of(1, 1, 0))));
        IReadOnlyList<CarouselChange> grown = carousels.Push(3, CarriedSection.Of(Indication(1, 0x8000_0004, DiiModule.Of(1, largest, 0), DiiModule.Of(2, 1, 0))));
        IReadOnlyList<CarouselChange> afterwards = carousels.Push(4, CarriedSection.Of(Indication(1, DiiModule.Of(1, 1, 0))));

        Assert.Equal(CarouselDefect.TotalTooLarge, Assert.IsType<CarouselChange.Dropped>(Assert.Single(fifth)).Defect);
        Assert.Equal(CarouselDefect.TotalTooLarge, Assert.IsType<CarouselChange.Dropped>(Assert.Single(grown)).Defect);
        Assert.IsType<CarouselChange.CatalogueUpdated>(Assert.Single(afterwards));
    }

    [Fact(DisplayName = "BR-BV-001: no section of random bytes makes the carousels throw")]
    public void NoSectionOfRandomBytesMakesTheCarouselsThrow()
    {
        var random = new Random(20261014);
        DataCarousels carousels = new();
        carousels.Push(EntryTag, CarriedSection.Of(Indication(1, DiiModule.Of(1, 300, 0), DiiModule.Of(2, 40, 1, ModuleDescriptorWriter.Compression(80)))));
        int[] tableIds = [DsmCcWriter.DownloadInfoIndicationTableId, DsmCcWriter.DownloadDataBlockTableId];

        for (int round = 0; round < 2000; round++)
        {
            byte[] body = new byte[random.Next(0, 200)];
            random.NextBytes(body);
            body = round % 3 == 0 ? new DdbWriter { ModuleId = 1 + (round % 2), ModuleVersion = round % 2, BlockNumber = round % 3, Data = body }.ToMessage() : body;

            _ = carousels.Push(EntryTag, CarriedSection.Of(new SectionWriter
            {
                TableId = tableIds[round % 2],
                Body = body,
            }));
        }
    }

    private static SectionWriter Indication(long downloadId, params DiiModule[] modules) => Indication(downloadId, 0x8000_0002, modules);

    private static SectionWriter Indication(long downloadId, long transactionId, params DiiModule[] modules)
        => new DiiWriter { DownloadId = downloadId, TransactionId = transactionId, BlockSize = DsmCcWriter.LargestBlock, Modules = modules }.ToSection();
}
