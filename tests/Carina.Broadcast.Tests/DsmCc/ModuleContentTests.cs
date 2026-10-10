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

    [Fact(DisplayName = "BR-BD-002: a repeated header field keeps the first and its folded lines stay with it")]
    public void ARepeatedHeaderFieldKeepsTheFirstAndItsFoldedLinesStayWithIt()
    {
        byte[] entity = EntityWriter.Ascii(
            "Content-Type: multipart/mixed; boundary=b1\r\n\r\n--b1\r\nContent-Location: a.png\r\nContent-Type: image/png\r\nContent-Location: b.png\r\n c.png\r\n\r\nx\r\n--b1--\r\n");

        ModuleResource resource = Opened(entity).Single();

        Assert.Equal("a.png", resource.Location);
        Assert.Equal("image/png", resource.MediaType);
    }

    [Theory(DisplayName = "BR-BD-002: a part with no body comes out empty and the parts beside it stay")]
    [InlineData(true)]
    [InlineData(false)]
    public void APartWithNoBodyComesOutEmptyAndThePartsBesideItStay(bool endsOnTheBlankLine)
    {
        IReadOnlyList<ModuleResource> resources = Opened(EntityWriter.Multipart(
            Boundary,
            new EntityPart("empty.png", EntityWriter.PngType, []) { EndsOnTheBlankLine = endsOnTheBlankLine },
            new EntityPart("logo.png", EntityWriter.PngType, Picture)));

        Assert.Equal(["empty.png", "logo.png"], resources.Select(resource => resource.Location));
        Assert.Empty(resources[0].Body.ToArray());
        Assert.Equal(Picture, resources[1].Body.ToArray());
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
        CarouselLimits limits = Limits with { MostParts = 3 };
        EntityPart[] parts = Enumerable.Range(0, 4).Select(index => new EntityPart($"{index}.png", EntityWriter.PngType, Picture)).ToArray();

        ModuleContentRead atTheLimit = ModuleContent.Open(EntityWriter.Multipart(Boundary, parts[..3]), Info(), limits);
        ModuleContentRead pastIt = ModuleContent.Open(EntityWriter.Multipart(Boundary, parts), Info(), limits);

        Assert.Equal(3, Assert.IsType<ModuleContentRead.Opened>(atTheLimit).Resources.Count);
        Assert.Equal(CarouselDefect.TooManyParts, Assert.IsType<ModuleContentRead.Rejected>(pastIt).Defect);
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
        ModuleInfo info = Info(ModuleDescriptorWriter.Compression(1024));

        long before = GC.GetAllocatedBytesForCurrentThread();
        ModuleContentRead read = ModuleContent.Open(compressed, info, Limits);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(CarouselDefect.OriginalSizeExceeded, Assert.IsType<ModuleContentRead.Rejected>(read).Defect);
        Assert.True(allocated < 4 * 1024 * 1024, $"{allocated} bytes were allocated");
    }

    [Fact(DisplayName = "BR-BV-002: an original size above the largest module is discarded before inflating")]
    public void AnOriginalSizeAboveTheLargestModuleIsDiscardedBeforeInflating()
    {
        Assert.Equal(
            CarouselDefect.ModuleTooLarge,
            Defect(EntityWriter.Zlib([0x01]), ModuleDescriptorWriter.Compression(Largest + 1)));
    }

    [Fact(DisplayName = "BR-BV-002: an original size past what one array can hold is discarded before inflating whatever the largest module")]
    public void AnOriginalSizePastWhatOneArrayCanHoldIsDiscardedBeforeInflatingWhateverTheLargestModule()
    {
        ModuleContentRead read = ModuleContent.Open(EntityWriter.Zlib([0x01]), Info(ModuleDescriptorWriter.Compression(0xFFFF_FFFF)), Limits with { LargestModule = long.MaxValue });

        Assert.Equal(CarouselDefect.ModuleTooLarge, Assert.IsType<ModuleContentRead.Rejected>(read).Defect);
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
        ModuleInfo info = Info(ModuleDescriptorWriter.Compression(data.Length));

        for (int at = 0; at < compressed.Length; at++)
        {
            byte[] flipped = [.. compressed];
            flipped[at] ^= 0x5A;

            Assert.IsType<ModuleContentRead.Rejected>(ModuleContent.Open(compressed[..at], info, Limits));
            _ = ModuleContent.Open(flipped, info, Limits);
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
        ModuleInfo plain = Info(ModuleDescriptorWriter.Type($"multipart/mixed; boundary={Boundary}"));
        ModuleInfo compressed = Info(ModuleDescriptorWriter.Compression(256));

        for (int round = 0; round < 2000; round++)
        {
            byte[] module = new byte[random.Next(0, 200)];
            random.NextBytes(module);

            _ = ModuleContent.Open(module, plain, Limits);
            _ = ModuleContent.Open(module, compressed, Limits);
            _ = ModuleContent.Open(EntityWriter.Ascii($"--{Boundary}\r\n").Concat(module).ToArray(), plain, Limits);
        }
    }

    private static IReadOnlyList<ModuleResource> Opened(byte[] module, params byte[][] descriptors)
        => Assert.IsType<ModuleContentRead.Opened>(ModuleContent.Open(module, Info(descriptors), Limits)).Resources;

    private static CarouselDefect Defect(byte[] module, params byte[][] descriptors)
        => Assert.IsType<ModuleContentRead.Rejected>(ModuleContent.Open(module, Info(descriptors), Limits)).Defect;

    private static ModuleInfo Info(params byte[][] descriptors)
    {
        DiiWriter writer = new() { Modules = [DiiModule.Of(0, 100, 1, descriptors)] };

        return Assert.IsType<TableRead<DownloadInfoIndication>.Parsed>(
            DownloadInfoIndication.Read(CarriedSection.Of(writer.ToSection()))).Table.Modules.Single();
    }
}
