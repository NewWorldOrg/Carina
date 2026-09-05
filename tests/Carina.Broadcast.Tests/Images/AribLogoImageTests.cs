using Carina.Broadcast.Images;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Images;

public sealed class AribLogoImageTests
{
    [Theory]
    [InlineData(64, 36)]
    [InlineData(48, 24)]
    [InlineData(1, 1)]
    public void TheSizeOfALogoIsReadFromThePictureItselfRatherThanFromWhatItsKindImplies(int width, int height)
    {
        AribLogoImage image = Read(new LogoPngWriter { Width = width, Height = height });

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
    }

    [Fact]
    public void APictureBroadcastWithoutItsColoursComesBackWithTheColoursTheStandardFixes()
    {
        byte[] asBroadcast = new LogoPngWriter { Width = 64, Height = 36 }.ToBytes();
        AribLogoImage image = Read(new LogoPngWriter { Width = 64, Height = 36 });
        byte[] complete = image.Bytes.ToArray();

        Assert.Equal(
            asBroadcast.Length + (AribLogoPalette.Colours * 4) + 24,
            complete.Length);
        Assert.Equal("PLTE", Latin(complete, AribLogoImage.AfterHeader + 4, 4));
        Assert.Equal(
            "tRNS",
            Latin(complete, AribLogoImage.AfterHeader + 12 + (AribLogoPalette.Colours * 3) + 4, 4));
        Assert.Equal(asBroadcast[..AribLogoImage.AfterHeader], complete[..AribLogoImage.AfterHeader]);
    }

    [Fact]
    public void EveryColourTheStandardFixesIsThereWithTheOpacityBesideIt()
    {
        Assert.Equal(AribLogoPalette.Colours * 3, AribLogoPalette.Rgb.Length);
        Assert.Equal(AribLogoPalette.Colours, AribLogoPalette.Opacities.Length);
        Assert.Equal(0xFF, (int)AribLogoPalette.Opacities.Span[0]);
        Assert.Equal(0x00, (int)AribLogoPalette.Opacities.Span[8]);
        Assert.Equal(0x80, (int)AribLogoPalette.Opacities.Span[AribLogoPalette.Colours - 1]);
    }

    [Fact]
    public void APictureThatAlreadyCarriesItsColoursIsLeftExactlyAsItArrived()
    {
        byte[] already = new LogoPngWriter { Width = 48, Height = 24, CarriesThePalette = true }.ToBytes();

        Assert.True(AribLogoImage.TryRead(already, out AribLogoImage? image));
        Assert.Equal(already, image.Bytes.ToArray());
    }

    [Fact]
    public void APictureThatIsNotAPaletteAtAllIsLeftAloneRatherThanGivenAPaletteItCannotUse()
    {
        byte[] greyscale = new LogoPngWriter { Width = 8, Height = 8, ColourType = 0 }.ToBytes();

        Assert.True(AribLogoImage.TryRead(greyscale, out AribLogoImage? image));
        Assert.Equal(greyscale, image.Bytes.ToArray());
    }

    [Fact]
    public void BytesThatAreNotAPngAtAllAreRefused()
    {
        Assert.False(AribLogoImage.TryRead(new byte[64], out AribLogoImage? _));
    }

    [Fact]
    public void APngCutShortOfItsOwnHeaderIsRefusedRatherThanHalfRead()
    {
        byte[] whole = new LogoPngWriter { Width = 64, Height = 36 }.ToBytes();

        Assert.False(AribLogoImage.TryRead(whole[..(AribLogoImage.AfterHeader - 1)], out AribLogoImage? _));
    }

    [Fact]
    public void AHeaderWhoseChecksumDoesNotAddUpIsRefusedRatherThanTrusted()
    {
        byte[] corrupt = new LogoPngWriter
        {
            Width = 64,
            Height = 36,
            CorruptHeaderChecksum = true,
        }.ToBytes();

        Assert.False(AribLogoImage.TryRead(corrupt, out AribLogoImage? _));
    }

    [Fact]
    public void APictureNoPixelsWideOrTallIsRefused()
    {
        Assert.False(AribLogoImage.TryRead(
            new LogoPngWriter { Width = 0, Height = 36 }.ToBytes(),
            out AribLogoImage? _));
        Assert.False(AribLogoImage.TryRead(
            new LogoPngWriter { Width = 64, Height = 0 }.ToBytes(),
            out AribLogoImage? _));
    }

    private static AribLogoImage Read(LogoPngWriter writer)
    {
        Assert.True(AribLogoImage.TryRead(writer.ToBytes(), out AribLogoImage? image));

        return image;
    }

    private static string Latin(byte[] bytes, int at, int length)
        => string.Concat(bytes.Skip(at).Take(length).Select(octet => (char)octet));
}
