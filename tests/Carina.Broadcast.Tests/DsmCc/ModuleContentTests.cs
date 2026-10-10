using System.Text;

using Carina.Broadcast.DsmCc;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.DsmCc;

public sealed class ModuleContentTests
{
    private const long Largest = 16L * 1024 * 1024;

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

    [Fact]
    public void BR_BD_002_AModuleOfOneResourceTakesItsLocationFromTheModuleNameAndItsTypeFromTheTypeDescriptor()
    {
        ModuleResource resource = Opened(
            BmlInEucJp,
            ModuleDescriptorWriter.Type("text/X-arib-bml"),
            ModuleDescriptorWriter.Name("startup.bml")).Single();

        Assert.Equal("startup.bml", resource.Location);
        Assert.Equal("text/X-arib-bml", resource.MediaType);
        Assert.True(resource.IsText);
        Assert.Equal(BmlInUtf8, Encoding.UTF8.GetString(resource.Body.Span));
    }

    [Fact]
    public void BR_BD_002_ACompressedModuleIsInflatedBeforeItIsTakenApart()
    {
        byte[] entity = EntityWriter.Multipart(Boundary, new EntityPart("startup.bml", EntityWriter.BmlType, BmlInEucJp));

        ModuleResource resource = Opened(
            EntityWriter.Zlib(entity),
            ModuleDescriptorWriter.Compression(entity.Length)).Single();

        Assert.Equal("startup.bml", resource.Location);
        Assert.Equal(BmlInUtf8, Encoding.UTF8.GetString(resource.Body.Span));
    }

    [Fact]
    public void BR_BD_002_AMultipartModuleIsTakenApartByItsContentLocationAndContentType()
    {
        IReadOnlyList<ModuleResource> resources = Opened(EntityWriter.Multipart(
            Boundary,
            new EntityPart("startup.bml", EntityWriter.BmlType, BmlInEucJp),
            new EntityPart("logo.png", EntityWriter.PngType, Picture)));

        Assert.Equal(["startup.bml", "logo.png"], resources.Select(resource => resource.Location));
        Assert.Equal(["text/X-arib-bml", EntityWriter.PngType], resources.Select(resource => resource.MediaType));
        Assert.Equal(BmlInUtf8, Encoding.UTF8.GetString(resources[0].Body.Span));
        Assert.False(resources[1].IsText);
        Assert.Equal(Picture, resources[1].Body.ToArray());
    }

    [Fact]
    public void BR_BD_002_TheBoundaryCanComeFromTheTypeDescriptorWhenTheEntityCarriesNoHeader()
    {
        IReadOnlyList<ModuleResource> resources = Opened(
            EntityWriter.Parts(Boundary, new EntityPart("a.png", EntityWriter.PngType, Picture)),
            ModuleDescriptorWriter.Type($"multipart/mixed; boundary={Boundary}"));

        Assert.Equal("a.png", resources.Single().Location);
        Assert.Equal(Picture, resources.Single().Body.ToArray());
    }

    [Fact]
    public void BR_BD_002_AnEntityOfOneResourceWithAHeaderTakesItsLocationFromTheHeader()
    {
        byte[] entity = [.. EntityWriter.Ascii("Content-Type: image/jpeg\r\nContent-Location: photo.jpg\r\n\r\n"), .. Picture];

        ModuleResource resource = Opened(entity, ModuleDescriptorWriter.Name("module-name")).Single();

        Assert.Equal("photo.jpg", resource.Location);
        Assert.Equal("image/jpeg", resource.MediaType);
        Assert.Equal(Picture, resource.Body.ToArray());
    }

    [Fact]
    public void BR_BD_002_APreambleTransportPaddingFoldedHeadersAndBareLineFeedsAreAllRead()
    {
        byte[] entity = EntityWriter.Ascii(
            "Content-Type: multipart/mixed;\n boundary=\"b1\"\n\nthis is a preamble\n--b1  \nContent-Location: a.css\nContent-Type: text/css\n\nbody{}\n--b1--\n");

        ModuleResource resource = Opened(entity).Single();

        Assert.Equal("a.css", resource.Location);
        Assert.Equal("body{}", Encoding.UTF8.GetString(resource.Body.Span));
    }

    [Fact]
    public void BR_BD_002_TheBoundaryInsideALineIsPartOfTheBody()
    {
        byte[] body = EntityWriter.Ascii($"x --{Boundary} y");

        ModuleResource resource = Opened(EntityWriter.Multipart(Boundary, new EntityPart("a.png", EntityWriter.PngType, body))).Single();

        Assert.Equal(body, resource.Body.ToArray());
    }

    [Theory]
    [InlineData("text/css")]
    [InlineData("text/X-arib-ecmascript")]
    [InlineData("application/X-arib-ecmascript")]
    [InlineData("text/X-arib-bml")]
    public void BR_BD_002_StylesheetsScriptsAndDocumentsAreTextTurnedIntoUtf8(string type)
    {
        ModuleResource resource = Opened(EntityWriter.Multipart(Boundary, new EntityPart("a", type, [0xC5, 0xB7]))).Single();

        Assert.True(resource.IsText);
        Assert.Equal("天", Encoding.UTF8.GetString(resource.Body.Span));
    }

    [Fact]
    public void BR_BD_002_TextAlreadyDeclaredAsUtf8IsLeftAsItIs()
    {
        byte[] body = Encoding.UTF8.GetBytes("天");

        ModuleResource resource = Opened(EntityWriter.Multipart(Boundary, new EntityPart("a.css", "text/css; charset=UTF-8", body))).Single();

        Assert.True(resource.IsText);
        Assert.Equal(body, resource.Body.ToArray());
    }

    [Fact]
    public void BR_BD_002_TextInACharacterSetOtherThanEucJpOrUtf8IsHandedOnAsReceivedAndNotAsText()
    {
        byte[] body = [0x93, 0x56];

        ModuleResource resource = Opened(EntityWriter.Multipart(Boundary, new EntityPart("a.css", "text/css; charset=Shift_JIS", body))).Single();

        Assert.False(resource.IsText);
        Assert.Equal(body, resource.Body.ToArray());
    }

    [Theory]
    [InlineData("--carina-part\r\nContent-Location: a\r\n\r\nbody\r\n")]
    [InlineData("Content-Location: a\r\n\r\nbody\r\n")]
    [InlineData("--carina-part\r\nContent-Location: a\r\nbody without the empty line\r\n--carina-part--\r\n")]
    [InlineData("--carina-part")]
    public void BR_BV_001_AMultipartEntityWithoutItsDelimitersOrHeadersIsRejected(string parts)
    {
        byte[] entity = [.. EntityWriter.Ascii($"Content-Type: multipart/mixed; boundary={Boundary}\r\n\r\n"), .. EntityWriter.Ascii(parts)];

        Assert.Equal(CarouselDefect.EntityMalformed, Defect(entity));
    }

    [Fact]
    public void BR_BV_001_AnEmptyBoundaryIsRejected()
    {
        Assert.Equal(CarouselDefect.EntityMalformed, Defect(EntityWriter.Ascii("Content-Type: multipart/mixed; boundary=\"\"\r\n\r\n--\r\n----\r\n")));
    }

    [Fact]
    public void BR_BV_002_InflatingStopsAtTheOriginalSizeAndAModuleThatGoesPastItIsDiscarded()
    {
        byte[] data = new byte[1000];

        Assert.Equal(CarouselDefect.OriginalSizeExceeded, Defect(EntityWriter.Zlib(data), ModuleDescriptorWriter.Compression(999)));
    }

    [Fact]
    public void BR_BV_002_AModuleThatInflatesFarPastItsOriginalSizeIsNotInflatedToTheEnd()
    {
        byte[] compressed = EntityWriter.Zlib(new byte[64 * 1024 * 1024]);
        ModuleInfo info = Info(ModuleDescriptorWriter.Compression(1024));

        long before = GC.GetAllocatedBytesForCurrentThread();
        ModuleContentRead read = ModuleContent.Open(compressed, info, Largest);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(CarouselDefect.OriginalSizeExceeded, Assert.IsType<ModuleContentRead.Rejected>(read).Defect);
        Assert.True(allocated < 4 * 1024 * 1024, $"{allocated} bytes were allocated");
    }

    [Fact]
    public void BR_BV_002_AnOriginalSizeAboveTheLargestModuleIsDiscardedBeforeInflating()
    {
        Assert.Equal(
            CarouselDefect.ModuleTooLarge,
            Defect(EntityWriter.Zlib([0x01]), ModuleDescriptorWriter.Compression(Largest + 1)));
    }

    [Fact]
    public void BR_BV_002_ZlibThatEndsShortOfTheOriginalSizeIsDiscardedOnTheAssumptionThatTheSizeIsExact()
    {
        Assert.Equal(CarouselDefect.InflatedSizeMismatch, Defect(EntityWriter.Zlib(new byte[10]), ModuleDescriptorWriter.Compression(11)));
    }

    [Fact]
    public void BR_BV_002_ZlibThatCannotBeInflatedIsDiscarded()
    {
        byte[] compressed = EntityWriter.Zlib(new byte[100]);
        compressed[0] = 0x00;

        Assert.Equal(CarouselDefect.DecompressionFailed, Defect(compressed, ModuleDescriptorWriter.Compression(100)));
    }

    [Fact]
    public void BR_BV_002_ACompressionTypeOtherThanZeroIsTakenToBeSomethingOtherThanZlibAndDiscarded()
    {
        Assert.Equal(
            CarouselDefect.UnsupportedCompression,
            Defect(EntityWriter.Zlib([0x01]), ModuleDescriptorWriter.Compression(1, compressionType: 0x01)));
    }

    [Fact]
    public void BR_BV_001_NoModuleOfRandomBytesMakesOpeningItThrow()
    {
        var random = new Random(20261013);
        ModuleInfo plain = Info(ModuleDescriptorWriter.Type($"multipart/mixed; boundary={Boundary}"));
        ModuleInfo compressed = Info(ModuleDescriptorWriter.Compression(256));

        for (int round = 0; round < 2000; round++)
        {
            byte[] module = new byte[random.Next(0, 200)];
            random.NextBytes(module);

            _ = ModuleContent.Open(module, plain, Largest);
            _ = ModuleContent.Open(module, compressed, Largest);
            _ = ModuleContent.Open(EntityWriter.Ascii($"--{Boundary}\r\n").Concat(module).ToArray(), plain, Largest);
        }
    }

    private static IReadOnlyList<ModuleResource> Opened(byte[] module, params byte[][] descriptors)
        => Assert.IsType<ModuleContentRead.Opened>(ModuleContent.Open(module, Info(descriptors), Largest)).Resources;

    private static CarouselDefect Defect(byte[] module, params byte[][] descriptors)
        => Assert.IsType<ModuleContentRead.Rejected>(ModuleContent.Open(module, Info(descriptors), Largest)).Defect;

    private static ModuleInfo Info(params byte[][] descriptors)
    {
        DiiWriter writer = new() { Modules = [DiiModule.Of(0, 100, 1, descriptors)] };

        return Assert.IsType<TableRead<DownloadInfoIndication>.Parsed>(
            DownloadInfoIndication.Read(CarriedSection.Of(writer.ToSection()))).Table.Modules.Single();
    }
}
