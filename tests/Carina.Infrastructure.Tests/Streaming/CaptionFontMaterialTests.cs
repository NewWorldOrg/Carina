using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

/// <summary>
/// Reads the face the image installs for the captions. The decoder scales a glyph's em to the cell a
/// caption gives each character, so a face whose every advance is the whole em or half of it keeps a
/// full-width character to its cell and a half-width one to half of it, and the synthetic broadcasts'
/// captions are drawn with the face itself rather than falling through to the one behind it.
/// </summary>
[Trait("Category", "Material")]
public sealed class CaptionFontMaterialTests
{
    public const string Installed = "/usr/local/share/fonts/denpa-font/denpa-font.ttf";

    private const string Kanji = "合成字幕";

    private const string Ascii = "CARINA";

    private const string FullWidthAscii = "ＣＡＲＩＮＡ";

    private const string AribSymbols = "①㈱☎♪";

    private static readonly TrueTypeFace Face = TrueTypeFace.Read(
        Environment.GetEnvironmentVariable("CARINA_CAPTION_FONT") ?? Installed);

    [Fact]
    public void TheInstalledFaceIsTheOneTheLiveBuilderNamesFirst()
    {
        Assert.Equal(FfmpegLiveInvocation.Font.Split(',')[0], Face.FamilyName);
    }

    [Fact]
    public void EveryAdvanceIsTheWholeEmOrHalfOfIt()
    {
        Assert.Equal(1024, Face.UnitsPerEm);
        Assert.Equal(Face.GlyphCount, Face.Advances.Count);
        Assert.All(Face.Advances, advance => Assert.Contains(advance, new[] { 512, 1024 }));
    }

    [Fact]
    public void TheCaptionsOfTheSyntheticBroadcastsAreDrawnWithTheFaceItselfAtTheWidthTheirCellsGiveThem()
    {
        Assert.All(Kanji, character => Assert.Equal(1024, Face.AdvanceOf(character)));
        Assert.All(Ascii, character => Assert.Equal(512, Face.AdvanceOf(character)));
        Assert.All(FullWidthAscii, character => Assert.Equal(1024, Face.AdvanceOf(character)));
    }

    [Fact]
    public void TheSymbolsBroadcastsAddToTheCharacterSetAreInTheFaceAtTheWholeEm()
    {
        Assert.All(AribSymbols, character => Assert.Equal(1024, Face.AdvanceOf(character)));
    }
}
