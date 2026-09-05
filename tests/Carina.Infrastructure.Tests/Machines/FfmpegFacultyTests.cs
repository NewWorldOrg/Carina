using Carina.Domain.Machines;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Tests.Machines;

public sealed class FfmpegFacultyTests
{
    /// <summary>
    /// The shape ffmpeg 6.1.6 actually prints, read off the container on 2026-09-05. The flag
    /// column and the row of dashes above the entries are what the reading hangs on.
    /// </summary>
    private const string Encoders = """
        Encoders:
         V..... = Video
         A..... = Audio
         S..... = Subtitle
         .F.... = Frame-level multithreading
         ..S... = Slice-level multithreading
         ...X.. = Codec is experimental
         ....B. = Supports draw_horiz_band
         .....D = Supports direct rendering method 1
         ------
         V....D av1_vaapi            AV1 (VAAPI) (codec av1)
         V....D libx264              libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (codec h264)
         V....D libx264rgb           libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 RGB (codec h264)
         V....D h264_vaapi           H.264/AVC (VAAPI) (codec h264)
         V....D hevc_vaapi           H.265/HEVC (VAAPI) (codec hevc)
         A....D aac                  AAC (Advanced Audio Coding)

        """;

    private const string Decoders = """
        Decoders:
         V..... = Video
         ------
         S..... libaribcaption       ARIB STD-B24 caption decoder (codec arib_caption)
         S..... cc_dec               Closed Caption (EIA-608 / CEA-708) (codec eia_608)

        """;

    [Fact]
    public void EveryNameBelowTheDashesIsRead()
        => Assert.Equal(
            ["av1_vaapi", "libx264", "libx264rgb", "h264_vaapi", "hevc_vaapi", "aac"],
            FfmpegFaculties.Listed(Encoders));

    [Fact]
    public void NothingAboveTheDashesIsMistakenForANameBelowThem()
        => Assert.DoesNotContain("=", FfmpegFaculties.Listed(Encoders), StringComparer.Ordinal);

    [Fact]
    public void AListingWithNoDashesInItNamesNothing()
        => Assert.Empty(FfmpegFaculties.Listed("Encoders:\n V..... libx264   libx264\n"));

    [Fact]
    public void AListingThatIsNotThereNamesNothing()
        => Assert.Empty(FfmpegFaculties.Listed(string.Empty));

    [Fact(DisplayName = "BR-EV-004: this build has no libx265, so H.265 is on the card and nowhere else")]
    public void ThisBuildHasNoLibx265SoH265IsOnTheCardAndNowhereElse()
        => Assert.Equal(
            [
                Faculty.EncodeH264OnTheProcessor,
                Faculty.EncodeH264OnTheCard,
                Faculty.EncodeH265OnTheCard,
                Faculty.DecodeAribCaptions,
            ],
            FfmpegFaculties.Of(FfmpegFaculties.Listed(Encoders), FfmpegFaculties.Listed(Decoders), cardEncodesH264: true, cardEncodesH265: true));

    [Fact(DisplayName = "BR-EV-004: a card that encoded an H.264 frame and refused an H.265 one has H.264 on the card alone, whatever the build lists")]
    public void ACardThatRefusedAnH265FrameHasH264OnTheCardAlone()
        => Assert.Equal(
            [Faculty.EncodeH264OnTheProcessor, Faculty.EncodeH264OnTheCard, Faculty.DecodeAribCaptions],
            FfmpegFaculties.Of(FfmpegFaculties.Listed(Encoders), FfmpegFaculties.Listed(Decoders), cardEncodesH264: true, cardEncodesH265: false));

    [Fact(DisplayName = "BR-EV-004: a build that lists an encoder for the card is not a card that can be reached")]
    public void ABuildThatListsAnEncoderForTheCardIsNotACardThatCanBeReached()
        => Assert.Equal(
            [Faculty.EncodeH264OnTheProcessor, Faculty.DecodeAribCaptions],
            FfmpegFaculties.Of(FfmpegFaculties.Listed(Encoders), FfmpegFaculties.Listed(Decoders), cardEncodesH264: false, cardEncodesH265: false));

    [Fact]
    public void ABuildWithLibx265HasH265OnTheProcessorToo()
        => Assert.Contains(
            Faculty.EncodeH265OnTheProcessor,
            FfmpegFaculties.Of([FfmpegFaculties.H265OnTheProcessor], [], cardEncodesH264: false, cardEncodesH265: false));

    [Fact]
    public void ABuildWithoutTheCaptionDecoderCannotDecodeCaptions()
        => Assert.DoesNotContain(
            Faculty.DecodeAribCaptions,
            FfmpegFaculties.Of(FfmpegFaculties.Listed(Encoders), ["cc_dec"], cardEncodesH264: false, cardEncodesH265: false));

    [Fact]
    public void AProgrammeThatSaidNothingLeavesThisMachineAbleToDoNothing()
        => Assert.Empty(FfmpegFaculties.Of([], [], cardEncodesH264: true, cardEncodesH265: true));

    [Fact]
    public void TheNamesTheReadingLooksForAreTheOnesTheCommandsUse()
    {
        Assert.Equal("libx264", FfmpegFaculties.H264OnTheProcessor);
        Assert.Equal("libx265", FfmpegFaculties.H265OnTheProcessor);
        Assert.Equal("h264_vaapi", FfmpegFaculties.H264OnTheCard);
        Assert.Equal("hevc_vaapi", FfmpegFaculties.H265OnTheCard);
        Assert.Equal("libaribcaption", FfmpegFaculties.AribCaptions);
    }
}
