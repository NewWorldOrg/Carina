using System.Diagnostics;
using System.Text;

using Carina.Broadcast.DsmCc;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.DsmCc;

public sealed class ModuleContentTests
{
    private static readonly CarouselLimits Limits = CarouselLimits.Broadcast;

    private static readonly long Largest = Limits.LargestModule;

    private const string Boundary = "carina-part";

    private const int MimeHeaderLimit = 64 * 1024;

    private const int MimeHeaderFields = 256;

    private static readonly byte[] BmlInEucJp =
    [
        .. "<bml><body><p>"u8,
        0xC5, 0xB7, 0xB5, 0xA4,
        0xFA, 0xA1,
        .. "</p></body></bml>"u8,
    ];

    private const string BmlInUtf8 = "<bml><body><p>天気⛌</p></body></bml>";

    private static readonly byte[] Picture = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xC5, 0xB7, 0x00, 0xFF];

    [Fact(DisplayName = "BR-BD-002: a module of one resource takes its location from the module name and its type from the type descriptor")]
    public void AModuleOfOneResourceTakesItsLocationFromTheModuleNameAndItsTypeFromTheTypeDescriptor()
    {
        ModuleResource resource = Opened(
            BmlInEucJp,
            ModuleDescriptorWriter.Type("text/X-arib-bml"),
            ModuleDescriptorWriter.Name("startup.bml")).Single();

        Assert.Equal("startup.bml", resource.Location);
        Assert.Equal("text/X-arib-bml", resource.MediaType);
        Assert.Equal(ResourceContent.Text, resource.Content);
        Assert.Equal(BmlInUtf8, Encoding.UTF8.GetString(resource.Body.Span));
    }

    [Fact(DisplayName = "BR-BD-002: a compressed module is inflated before it is taken apart")]
    public void ACompressedModuleIsInflatedBeforeItIsTakenApart()
    {
        byte[] entity = EntityWriter.Multipart(Boundary, new EntityPart("startup.bml", EntityWriter.BmlType, BmlInEucJp));

        ModuleResource resource = Opened(
            EntityWriter.Zlib(entity),
            ModuleDescriptorWriter.Compression(entity.Length)).Single();

        Assert.Equal("startup.bml", resource.Location);
        Assert.Equal(BmlInUtf8, Encoding.UTF8.GetString(resource.Body.Span));
    }

    [Fact(DisplayName = "BR-BD-002: a multipart module is taken apart by its content location and content type")]
    public void AMultipartModuleIsTakenApartByItsContentLocationAndContentType()
    {
        IReadOnlyList<ModuleResource> resources = Opened(EntityWriter.Multipart(
            Boundary,
            new EntityPart("startup.bml", EntityWriter.BmlType, BmlInEucJp),
            new EntityPart("logo.png", EntityWriter.PngType, Picture)));

        Assert.Equal(["startup.bml", "logo.png"], resources.Select(resource => resource.Location));
        Assert.Equal(["text/X-arib-bml", EntityWriter.PngType], resources.Select(resource => resource.MediaType));
        Assert.Equal(BmlInUtf8, Encoding.UTF8.GetString(resources[0].Body.Span));
        Assert.Equal(ResourceContent.Binary, resources[1].Content);
        Assert.Equal(Picture, resources[1].Body.ToArray());
    }

    [Fact(DisplayName = "BR-BD-002: the boundary can come from the type descriptor when the entity carries no header")]
    public void TheBoundaryCanComeFromTheTypeDescriptorWhenTheEntityCarriesNoHeader()
    {
        IReadOnlyList<ModuleResource> resources = Opened(
            EntityWriter.Parts(Boundary, new EntityPart("a.png", EntityWriter.PngType, Picture)),
            ModuleDescriptorWriter.Type($"multipart/mixed; boundary={Boundary}"));

        Assert.Equal("a.png", resources.Single().Location);
        Assert.Equal(Picture, resources.Single().Body.ToArray());
    }

    [Fact(DisplayName = "BR-BD-002: an entity of one resource with a header takes its location from the header")]
    public void AnEntityOfOneResourceWithAHeaderTakesItsLocationFromTheHeader()
    {
        byte[] entity = [.. EntityWriter.Ascii("Content-Type: image/jpeg\r\nContent-Location: photo.jpg\r\n\r\n"), .. Picture];

        ModuleResource resource = Opened(entity, ModuleDescriptorWriter.Name("module-name")).Single();

        Assert.Equal("photo.jpg", resource.Location);
        Assert.Equal("image/jpeg", resource.MediaType);
        Assert.Equal(Picture, resource.Body.ToArray());
    }

    [Fact(DisplayName = "BR-BD-002: a header without a content type takes the type from the type descriptor and keeps its location")]
    public void AHeaderWithoutAContentTypeTakesTheTypeFromTheTypeDescriptorAndKeepsItsLocation()
    {
        byte[] entity = [.. EntityWriter.Ascii("Content-Location: photo.png\r\n\r\n"), .. Picture];

        ModuleResource resource = Opened(entity, ModuleDescriptorWriter.Type(EntityWriter.PngType), ModuleDescriptorWriter.Name("module-name")).Single();

        Assert.Equal("photo.png", resource.Location);
        Assert.Equal(EntityWriter.PngType, resource.MediaType);
        Assert.Equal(Picture, resource.Body.ToArray());
    }

    [Theory(DisplayName = "BR-BV-001: a header that starts but breaks off is rejected rather than read as the body")]
    [InlineData("Content-Location: photo.png\r\nnot a field\r\n\r\nbody")]
    [InlineData("Content-Type: image/png\r\nbody without the empty line")]
    public void AHeaderThatStartsButBreaksOffIsRejectedRatherThanReadAsTheBody(string entity)
    {
        Assert.Equal(CarouselDefect.EntityMalformed, Defect(EntityWriter.Ascii(entity), ModuleDescriptorWriter.Type(EntityWriter.PngType)));
    }

    [Fact(DisplayName = "BR-BD-002: a preamble transport padding folded headers and bare line feeds are all read")]
    public void APreambleTransportPaddingFoldedHeadersAndBareLineFeedsAreAllRead()
    {
        byte[] entity = EntityWriter.Ascii(
            "Content-Type: multipart/mixed;\n boundary=\"b1\"\n\nthis is a preamble\n--b1  \nContent-Location: a.css\nContent-Type: text/css\n\nbody{}\n--b1--\n");

        ModuleResource resource = Opened(entity).Single();

        Assert.Equal("a.css", resource.Location);
        Assert.Equal("body{}", Encoding.UTF8.GetString(resource.Body.Span));
    }

    [Fact(DisplayName = "BR-BD-002: a repeated header field other than the type and location keeps the first and its folded lines stay with it")]
    public void ARepeatedHeaderFieldOtherThanTheTypeAndLocationKeepsTheFirstAndItsFoldedLinesStayWithIt()
    {
        byte[] entity = EntityWriter.Ascii(
            "Content-Type: multipart/mixed; boundary=b1\r\n\r\n--b1\r\nContent-Location: a.png\r\nX-Note: one\r\nContent-Type: image/png\r\nX-Note: two\r\n c.png\r\n\r\nx\r\n--b1--\r\n");

        ModuleResource resource = Opened(entity).Single();

        Assert.Equal("a.png", resource.Location);
        Assert.Equal("image/png", resource.MediaType);
    }

    [Theory(DisplayName = "BR-BV-001: a part without a location, a location used twice or a type or location given twice is rejected")]
    [InlineData("--b1\r\nContent-Type: image/png\r\n\r\nx\r\n--b1--\r\n")]
    [InlineData("--b1\r\nContent-Location: a.png\r\n\r\nx\r\n--b1\r\nContent-Location: a.png\r\n\r\ny\r\n--b1--\r\n")]
    [InlineData("--b1\r\nContent-Location: a.png\r\nContent-Location: b.png\r\n\r\nx\r\n--b1--\r\n")]
    [InlineData("--b1\r\nContent-Location: a.png\r\nContent-Type: image/png\r\ncontent-type: image/jpeg\r\n\r\nx\r\n--b1--\r\n")]
    public void APartWithoutALocationALocationUsedTwiceOrATypeOrLocationGivenTwiceIsRejected(string parts)
    {
        byte[] entity = EntityWriter.Ascii($"Content-Type: multipart/mixed; boundary=b1\r\n\r\n{parts}");

        Assert.Equal(CarouselDefect.EntityMalformed, Defect(entity));
    }

    [Fact(DisplayName = "BR-BV-001: an entity header giving the type twice is rejected")]
    public void AnEntityHeaderGivingTheTypeTwiceIsRejected()
    {
        byte[] entity = [.. EntityWriter.Ascii("Content-Type: image/png\r\nContent-Type: image/jpeg\r\n\r\n"), .. Picture];

        Assert.Equal(CarouselDefect.EntityMalformed, Defect(entity));
    }

    [Fact(DisplayName = "BR-BD-002: a line that only starts with the boundary is part of the body")]
    public void ALineThatOnlyStartsWithTheBoundaryIsPartOfTheBody()
    {
        byte[] body = EntityWriter.Ascii($"first\r\n--{Boundary}x\r\n--{Boundary}--x\r\nlast");

        IReadOnlyList<ModuleResource> resources = Opened(EntityWriter.Multipart(
            Boundary,
            new EntityPart("a.png", EntityWriter.PngType, body),
            new EntityPart("b.png", EntityWriter.PngType, Picture)));

        Assert.Equal(body, resources[0].Body.ToArray());
        Assert.Equal(Picture, resources[1].Body.ToArray());
    }

    [Fact(DisplayName = "BR-BD-002: the boundary inside a line is part of the body")]
    public void TheBoundaryInsideALineIsPartOfTheBody()
    {
        byte[] body = EntityWriter.Ascii($"x --{Boundary} y");

        ModuleResource resource = Opened(EntityWriter.Multipart(Boundary, new EntityPart("a.png", EntityWriter.PngType, body))).Single();

        Assert.Equal(body, resource.Body.ToArray());
    }

    [Theory(DisplayName = "BR-BD-002: stylesheets scripts and documents are text turned into UTF-8")]
    [InlineData("text/css")]
    [InlineData("text/X-arib-ecmascript")]
    [InlineData("application/X-arib-ecmascript")]
    [InlineData("text/X-arib-bml")]
    public void StylesheetsScriptsAndDocumentsAreTextTurnedIntoUtf8(string type)
    {
        ModuleResource resource = Opened(EntityWriter.Multipart(Boundary, new EntityPart("a", type, [0xC5, 0xB7]))).Single();

        Assert.Equal(ResourceContent.Text, resource.Content);
        Assert.Equal("天", Encoding.UTF8.GetString(resource.Body.Span));
    }

    [Fact(DisplayName = "BR-BD-002: text read as EUC-JP counts the bytes it could not decode")]
    public void TextReadAsEucJpCountsTheBytesItCouldNotDecode()
    {
        ModuleResource resource = Opened(EntityWriter.Multipart(Boundary, new EntityPart("a.css", "text/css", Encoding.UTF8.GetBytes("p{content:\"天気\"}")))).Single();
        ModuleResource clean = Opened(EntityWriter.Multipart(Boundary, new EntityPart("b.css", "text/css", [0xC5, 0xB7]))).Single();

        Assert.True(resource.Substitutions > 0);
        Assert.Equal(0, clean.Substitutions);
    }

    [Fact(DisplayName = "BR-BD-002: text already declared as UTF-8 is left as it is")]
    public void TextAlreadyDeclaredAsUtf8IsLeftAsItIs()
    {
        byte[] body = Encoding.UTF8.GetBytes("天");

        ModuleResource resource = Opened(EntityWriter.Multipart(Boundary, new EntityPart("a.css", "text/css; charset=UTF-8", body))).Single();

        Assert.Equal(ResourceContent.Text, resource.Content);
        Assert.Equal(body, resource.Body.ToArray());
    }

    [Fact(DisplayName = "BR-BD-002: text in a character set other than EUC-JP or UTF-8 is handed on as received with its character set, apart from binaries")]
    public void TextInACharacterSetOtherThanEucJpOrUtf8IsHandedOnAsReceivedWithItsCharacterSetApartFromBinaries()
    {
        byte[] body = [0x93, 0x56];

        ModuleResource resource = Opened(EntityWriter.Multipart(Boundary, new EntityPart("a.css", "text/css; charset=Shift_JIS", body))).Single();

        Assert.Equal(ResourceContent.UndecodedText, resource.Content);
        Assert.Equal("Shift_JIS", resource.Charset);
        Assert.Equal(body, resource.Body.ToArray());
    }

    [Theory(DisplayName = "BR-BV-001: a multipart entity without its delimiters or headers is rejected")]
    [InlineData("--carina-part\r\nContent-Location: a\r\n\r\nbody\r\n")]
    [InlineData("Content-Location: a\r\n\r\nbody\r\n")]
    [InlineData("--carina-part\r\nContent-Location: a\r\nbody without the empty line\r\n--carina-part--\r\n")]
    [InlineData("--carina-part")]
    public void AMultipartEntityWithoutItsDelimitersOrHeadersIsRejected(string parts)
    {
        byte[] entity = [.. EntityWriter.Ascii($"Content-Type: multipart/mixed; boundary={Boundary}\r\n\r\n"), .. EntityWriter.Ascii(parts)];

        Assert.Equal(CarouselDefect.EntityMalformed, Defect(entity));
    }

    [Fact(DisplayName = "BR-BV-002: a multipart module of more parts than the limit is discarded")]
    public void AMultipartModuleOfMorePartsThanTheLimitIsDiscarded()
    {
        CarouselLimits limits = new(Limits.MostCarousels, Limits.MostModules, Limits.LargestModule, Limits.LargestTotal, mostParts: 3);
        EntityPart[] parts = Enumerable.Range(0, 4).Select(index => new EntityPart($"{index}.png", EntityWriter.PngType, Picture)).ToArray();

        IReadOnlyList<CarouselChange> atTheLimit = Open(EntityWriter.Multipart(Boundary, parts[..3]), limits);
        IReadOnlyList<CarouselChange> pastIt = Open(EntityWriter.Multipart(Boundary, parts), limits);

        Assert.Equal(3, Resources(atTheLimit).Count);
        Assert.Equal(CarouselDefect.TooManyParts, Defect(pastIt));
    }

    [Fact(DisplayName = "BR-BV-002: a header folded over many lines is read in linear time up to its limit")]
    public void AHeaderFoldedOverManyLinesIsReadInLinearTimeUpToItsLimit()
    {
        string folded = string.Concat(Enumerable.Repeat("\r\n x", 16_000));
        byte[] entity = [.. EntityWriter.Ascii($"Content-Location: a.png{folded}\r\n\r\n"), .. Picture];
        Stopwatch watch = Stopwatch.StartNew();

        ModuleResource resource = Opened(entity, ModuleDescriptorWriter.Type(EntityWriter.PngType)).Single();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"{watch.Elapsed} to read the header");
        Assert.StartsWith("a.png x x", resource.Location, StringComparison.Ordinal);
        Assert.Equal(Picture, resource.Body.ToArray());
    }

    [Fact(DisplayName = "BR-BV-002: a header longer than its limit is rejected")]
    public void AHeaderLongerThanItsLimitIsRejected()
    {
        string folded = string.Concat(Enumerable.Repeat("\r\n x", (MimeHeaderLimit / 4) + 1));
        byte[] entity = [.. EntityWriter.Ascii($"Content-Location: a.png{folded}\r\n\r\n"), .. Picture];

        Assert.Equal(CarouselDefect.EntityMalformed, Defect(entity, ModuleDescriptorWriter.Type(EntityWriter.PngType)));
    }

    [Theory(DisplayName = "BR-BV-002: a header of more fields than its limit is rejected")]
    [InlineData(MimeHeaderFields, true)]
    [InlineData(MimeHeaderFields + 1, false)]
    public void AHeaderOfMoreFieldsThanItsLimitIsRejected(int count, bool read)
    {
        string fields = string.Concat(Enumerable.Range(1, count - 1).Select(index => $"X-Field-{index}: {index}\r\n"));
        byte[] entity = [.. EntityWriter.Ascii($"Content-Location: a.png\r\n{fields}\r\n"), .. Picture];

        IReadOnlyList<CarouselChange> changes = Open(entity, Limits, ModuleDescriptorWriter.Type(EntityWriter.PngType));

        CarouselDefect? expected = read ? null : CarouselDefect.EntityMalformed;

        Assert.Equal(expected, (Assert.Single(changes) as CarouselChange.Rejected)?.Defect);
    }

    [Fact(DisplayName = "BR-BV-001: an empty boundary is rejected")]
    public void AnEmptyBoundaryIsRejected()
    {
        Assert.Equal(CarouselDefect.EntityMalformed, Defect(EntityWriter.Ascii("Content-Type: multipart/mixed; boundary=\"\"\r\n\r\n--\r\n----\r\n")));
    }

    [Fact(DisplayName = "BR-BV-002: inflating stops at the original size and a module that goes past it is discarded")]
    public void InflatingStopsAtTheOriginalSizeAndAModuleThatGoesPastItIsDiscarded()
    {
        byte[] data = new byte[1000];

        Assert.Equal(CarouselDefect.OriginalSizeExceeded, Defect(EntityWriter.Zlib(data), ModuleDescriptorWriter.Compression(999)));
    }

    [Fact(DisplayName = "BR-BV-002: a module that inflates far past its original size is not inflated to the end")]
    public void AModuleThatInflatesFarPastItsOriginalSizeIsNotInflatedToTheEnd()
    {
        byte[] compressed = EntityWriter.Zlib(new byte[64 * 1024 * 1024]);
        byte[] described = ModuleDescriptorWriter.Compression(1024);
        IReadOnlyList<SectionWriter> blocks = DsmCcWriter.Blocks(1, 0, 0, compressed, DsmCcWriter.LargestBlock);
        ModuleAssembler assembler = Assembler(compressed.Length, Limits, described);
        DownloadDataBlock[] read = blocks.Select(Block).ToArray();

        long before = GC.GetAllocatedBytesForCurrentThread();
        IReadOnlyList<CarouselChange> changes = read.SelectMany(assembler.Accept).ToArray();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(CarouselDefect.OriginalSizeExceeded, Defect(changes));
        Assert.True(allocated < 4 * 1024 * 1024, $"{allocated} bytes were allocated");
    }

    [Fact(DisplayName = "BR-BV-002: an original size above the largest module is discarded before inflating")]
    public void AnOriginalSizeAboveTheLargestModuleIsDiscardedBeforeInflating()
    {
        Assert.Equal(
            CarouselDefect.ModuleTooLarge,
            Defect(EntityWriter.Zlib([0x01]), ModuleDescriptorWriter.Compression(Largest + 1)));
    }

    [Fact(DisplayName = "BR-BV-002: an original size past what one array can hold is discarded before inflating")]
    public void AnOriginalSizePastWhatOneArrayCanHoldIsDiscardedBeforeInflating()
    {
        Assert.Equal(CarouselDefect.ModuleTooLarge, Defect(EntityWriter.Zlib([0x01]), ModuleDescriptorWriter.Compression(0xFFFF_FFFF)));
    }

    [Fact(DisplayName = "BR-BV-002: zlib that ends short of the original size is discarded on the assumption that the size is exact")]
    public void ZlibThatEndsShortOfTheOriginalSizeIsDiscardedOnTheAssumptionThatTheSizeIsExact()
    {
        Assert.Equal(CarouselDefect.InflatedSizeMismatch, Defect(EntityWriter.Zlib(new byte[10]), ModuleDescriptorWriter.Compression(11)));
    }

    [Theory(DisplayName = "BR-BV-002: zlib cut off before its end and its checksum is discarded as failing to inflate")]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(40)]
    public void ZlibCutOffBeforeItsEndAndItsChecksumIsDiscardedAsFailingToInflate(int dropped)
    {
        byte[] data = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 200).Select(index => $"{index},")));
        byte[] compressed = EntityWriter.Zlib(data);

        Assert.Equal(CarouselDefect.DecompressionFailed, Defect(compressed[..^dropped], ModuleDescriptorWriter.Compression(data.Length)));
    }

    [Fact(DisplayName = "BR-BV-002: bytes left over after a complete zlib stream make the module fail to inflate")]
    public void BytesLeftOverAfterACompleteZlibStreamMakeTheModuleFailToInflate()
    {
        byte[] data = new byte[100];

        Assert.Equal(
            CarouselDefect.DecompressionFailed,
            Defect([.. EntityWriter.Zlib(data), 0x00, 0x01, 0x02], ModuleDescriptorWriter.Compression(data.Length)));
    }

    [Fact(DisplayName = "BR-BV-002: no cut or flipped byte of a zlib stream lets an exception out")]
    public void NoCutOrFlippedByteOfAZlibStreamLetsAnExceptionOut()
    {
        byte[] data = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 100).Select(index => $"<p>{index}</p>")));
        byte[] compressed = EntityWriter.Zlib(data);
        byte[] described = ModuleDescriptorWriter.Compression(data.Length);

        for (int at = 0; at < compressed.Length; at++)
        {
            byte[] flipped = [.. compressed];
            flipped[at] ^= 0x5A;

            Assert.IsType<CarouselChange.Rejected>(Assert.Single(Open(compressed[..at], Limits, described)));
            _ = Open(flipped, Limits, described);
        }
    }

    [Fact(DisplayName = "BR-BV-002: zlib that cannot be inflated is discarded")]
    public void ZlibThatCannotBeInflatedIsDiscarded()
    {
        byte[] compressed = EntityWriter.Zlib(new byte[100]);
        compressed[0] = 0x00;

        Assert.Equal(CarouselDefect.DecompressionFailed, Defect(compressed, ModuleDescriptorWriter.Compression(100)));
    }

    [Fact(DisplayName = "BR-BV-002: a compression type other than zero is taken to be something other than zlib and discarded")]
    public void ACompressionTypeOtherThanZeroIsTakenToBeSomethingOtherThanZlibAndDiscarded()
    {
        Assert.Equal(
            CarouselDefect.UnsupportedCompression,
            Defect(EntityWriter.Zlib([0x01]), ModuleDescriptorWriter.Compression(1, compressionType: 0x01)));
    }

    [Fact(DisplayName = "BR-BV-001: no module of random bytes makes opening it throw")]
    public void NoModuleOfRandomBytesMakesOpeningItThrow()
    {
        var random = new Random(20261013);
        byte[] plain = ModuleDescriptorWriter.Type($"multipart/mixed; boundary={Boundary}");
        byte[] compressed = ModuleDescriptorWriter.Compression(256);

        for (int round = 0; round < 2000; round++)
        {
            byte[] module = new byte[random.Next(0, 200)];
            random.NextBytes(module);

            _ = Open(module, Limits, plain);
            _ = Open(module, Limits, compressed);
            _ = Open(EntityWriter.Ascii($"--{Boundary}\r\n").Concat(module).ToArray(), Limits, plain);
        }
    }

    private static IReadOnlyList<ModuleResource> Opened(byte[] module, params byte[][] descriptors)
        => Resources(Open(module, Limits, descriptors));

    private static CarouselDefect Defect(byte[] module, params byte[][] descriptors) => Defect(Open(module, Limits, descriptors));

    private static IReadOnlyList<ModuleResource> Resources(IReadOnlyList<CarouselChange> changes)
        => Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(changes)).Module.Resources;

    private static CarouselDefect Defect(IReadOnlyList<CarouselChange> changes)
        => Assert.IsType<CarouselChange.Rejected>(changes.First(change => change is CarouselChange.Rejected)).Defect;

    private static IReadOnlyList<CarouselChange> Open(byte[] module, CarouselLimits limits, params byte[][] descriptors)
    {
        List<CarouselChange> changes = [];
        ModuleAssembler assembler = Assembler(module.Length, limits, descriptors, changes);

        changes.AddRange(DsmCcWriter.Blocks(1, 0, 0, module, DsmCcWriter.LargestBlock).SelectMany(block => assembler.Accept(Block(block))));

        return changes;
    }

    private static ModuleAssembler Assembler(int size, CarouselLimits limits, params byte[][] descriptors)
        => Assembler(size, limits, descriptors, []);

    private static ModuleAssembler Assembler(int size, CarouselLimits limits, byte[][] descriptors, List<CarouselChange> changes)
    {
        ModuleAssembler assembler = new(0x40, limits);
        DownloadInfoIndication indication = Assert.IsType<TableRead<DownloadInfoIndication>.Parsed>(DownloadInfoIndication.Read(CarriedSection.Of(new DiiWriter
        {
            Modules = [DiiModule.Of(0, size, 0, descriptors)],
        }.ToSection()))).Table;

        changes.AddRange(assembler.Accept(indication).Where(change => change is not CarouselChange.CatalogueUpdated));

        return assembler;
    }

    private static DownloadDataBlock Block(SectionWriter section)
        => Assert.IsType<TableRead<DownloadDataBlock>.Parsed>(DownloadDataBlock.Read(CarriedSection.Of(section))).Table;
}
