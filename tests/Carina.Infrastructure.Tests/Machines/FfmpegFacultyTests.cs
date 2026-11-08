using Carina.Domain.Machines;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Tests.Machines;

public sealed class FfmpegFacultyTests
{
    /// <summary>
    /// The encoder listing as ffmpeg 6.1.6 prints it, with the flag column and the row of dashes above
    /// the entries.
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

    private const string NvencEncoders = """
        Encoders:
         V..... = Video
         ------
         V....D libx264              libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (codec h264)
         V....D h264_vaapi           H.264/AVC (VAAPI) (codec h264)
         V....D h264_nvenc           NVIDIA NVENC H.264 encoder (codec h264)
         V....D hevc_nvenc           NVIDIA NVENC hevc encoder (codec hevc)
         V....D av1_nvenc            NVIDIA NVENC av1 encoder (codec av1)

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

    [Fact(DisplayName = "this build has no libx265, so H.265 is on the card and nowhere else")]
    public void ThisBuildHasNoLibx265SoH265IsOnTheCardAndNowhereElse()
        => Assert.Equal(
            [
                Faculty.EncodeH264OnTheProcessor,
                Faculty.EncodeH264OnTheCard,
                Faculty.EncodeH265OnTheCard,
                Faculty.DecodeAribCaptions,
            ],
            FfmpegFaculties.Of(FfmpegFaculties.Listed(Encoders), FfmpegFaculties.Listed(Decoders), CardKind.Vaapi, cardEncodesH264: true, cardEncodesH265: true, cardEncodesAv1: false));

    [Fact(DisplayName = "a card that encoded an H.264 frame and refused an H.265 one has H.264 on the card alone, whatever the build lists")]
    public void ACardThatRefusedAnH265FrameHasH264OnTheCardAlone()
        => Assert.Equal(
            [Faculty.EncodeH264OnTheProcessor, Faculty.EncodeH264OnTheCard, Faculty.DecodeAribCaptions],
            FfmpegFaculties.Of(FfmpegFaculties.Listed(Encoders), FfmpegFaculties.Listed(Decoders), CardKind.Vaapi, cardEncodesH264: true, cardEncodesH265: false, cardEncodesAv1: false));

    [Fact(DisplayName = "a build that lists an encoder for the card is not a card that can be reached")]
    public void ABuildThatListsAnEncoderForTheCardIsNotACardThatCanBeReached()
        => Assert.Equal(
            [Faculty.EncodeH264OnTheProcessor, Faculty.DecodeAribCaptions],
            FfmpegFaculties.Of(FfmpegFaculties.Listed(Encoders), FfmpegFaculties.Listed(Decoders), CardKind.Vaapi, cardEncodesH264: false, cardEncodesH265: false, cardEncodesAv1: false));

    [Fact(DisplayName = "a card that encoded an AV1 frame has AV1 on it, where the build lists av1_vaapi")]
    public void ACardThatEncodedAnAv1FrameHasAv1OnIt()
    {
        Assert.Contains(
            Faculty.EncodeAv1OnTheCard,
            FfmpegFaculties.Of(FfmpegFaculties.Listed(Encoders), FfmpegFaculties.Listed(Decoders), CardKind.Vaapi, cardEncodesH264: true, cardEncodesH265: true, cardEncodesAv1: true));
        Assert.DoesNotContain(
            Faculty.EncodeAv1OnTheCard,
            FfmpegFaculties.Of([FfmpegFaculties.H264OnVaapi], [], CardKind.Vaapi, cardEncodesH264: true, cardEncodesH265: true, cardEncodesAv1: true));
    }

    [Fact(DisplayName = "a card that refused an AV1 frame has no AV1 on it, whatever the build lists")]
    public void ACardThatRefusedAnAv1FrameHasNoAv1OnIt()
        => Assert.Equal(
            [
                Faculty.EncodeH264OnTheProcessor,
                Faculty.EncodeH264OnTheCard,
                Faculty.EncodeH265OnTheCard,
                Faculty.DecodeAribCaptions,
            ],
            FfmpegFaculties.Of(FfmpegFaculties.Listed(Encoders), FfmpegFaculties.Listed(Decoders), CardKind.Vaapi, cardEncodesH264: true, cardEncodesH265: true, cardEncodesAv1: false));

    [Fact]
    public void ABuildWithLibx265HasH265OnTheProcessorToo()
        => Assert.Contains(
            Faculty.EncodeH265OnTheProcessor,
            FfmpegFaculties.Of([FfmpegFaculties.H265OnTheProcessor], [], CardKind.Vaapi, cardEncodesH264: false, cardEncodesH265: false, cardEncodesAv1: false));

    [Fact]
    public void ABuildWithoutTheCaptionDecoderCannotDecodeCaptions()
        => Assert.DoesNotContain(
            Faculty.DecodeAribCaptions,
            FfmpegFaculties.Of(FfmpegFaculties.Listed(Encoders), ["cc_dec"], CardKind.Vaapi, cardEncodesH264: false, cardEncodesH265: false, cardEncodesAv1: false));

    [Fact]
    public void AProgrammeThatSaidNothingLeavesThisMachineAbleToDoNothing()
        => Assert.Empty(FfmpegFaculties.Of([], [], CardKind.Vaapi, cardEncodesH264: true, cardEncodesH265: true, cardEncodesAv1: true));

    [Fact]
    public void TheNamesTheReadingLooksForAreTheOnesTheCommandsUse()
    {
        Assert.Equal("libx264", FfmpegFaculties.H264OnTheProcessor);
        Assert.Equal("libx265", FfmpegFaculties.H265OnTheProcessor);
        Assert.Equal("h264_vaapi", FfmpegFaculties.H264OnVaapi);
        Assert.Equal("hevc_vaapi", FfmpegFaculties.H265OnVaapi);
        Assert.Equal("av1_vaapi", FfmpegFaculties.Av1OnVaapi);
        Assert.Equal("h264_nvenc", FfmpegFaculties.H264OnNvenc);
        Assert.Equal("hevc_nvenc", FfmpegFaculties.H265OnNvenc);
        Assert.Equal("av1_nvenc", FfmpegFaculties.Av1OnNvenc);
        Assert.Equal("libaribcaption", FfmpegFaculties.AribCaptions);
    }

    [Fact(DisplayName = "each kind of card has its own three encoder names")]
    public void EachKindOfCardHasItsOwnThreeEncoderNames()
    {
        Assert.Equal(["h264_vaapi", "hevc_vaapi", "av1_vaapi"], (string[])[FfmpegFaculties.H264On(CardKind.Vaapi), FfmpegFaculties.H265On(CardKind.Vaapi), FfmpegFaculties.Av1On(CardKind.Vaapi)]);
        Assert.Equal(["h264_nvenc", "hevc_nvenc", "av1_nvenc"], (string[])[FfmpegFaculties.H264On(CardKind.Nvenc), FfmpegFaculties.H265On(CardKind.Nvenc), FfmpegFaculties.Av1On(CardKind.Nvenc)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegFaculties.H264On((CardKind)9));
    }

    [Fact(DisplayName = "an NVIDIA card's faculties are read against the NVENC names, so a build with VAAPI alone has nothing on it")]
    public void AnNvidiaCardsFacultiesAreReadAgainstTheNvencNames()
    {
        Assert.Equal(
            [Faculty.EncodeH264OnTheProcessor, Faculty.EncodeH264OnTheCard, Faculty.EncodeH265OnTheCard, Faculty.DecodeAribCaptions],
            FfmpegFaculties.Of(FfmpegFaculties.Listed(NvencEncoders), FfmpegFaculties.Listed(Decoders), CardKind.Nvenc, cardEncodesH264: true, cardEncodesH265: true, cardEncodesAv1: false));
        Assert.Contains(
            Faculty.EncodeAv1OnTheCard,
            FfmpegFaculties.Of(FfmpegFaculties.Listed(NvencEncoders), FfmpegFaculties.Listed(Decoders), CardKind.Nvenc, cardEncodesH264: true, cardEncodesH265: true, cardEncodesAv1: true));
        Assert.Equal(
            [Faculty.EncodeH264OnTheProcessor, Faculty.DecodeAribCaptions],
            FfmpegFaculties.Of(FfmpegFaculties.Listed(Encoders), FfmpegFaculties.Listed(Decoders), CardKind.Nvenc, cardEncodesH264: true, cardEncodesH265: true, cardEncodesAv1: true));
    }
}
