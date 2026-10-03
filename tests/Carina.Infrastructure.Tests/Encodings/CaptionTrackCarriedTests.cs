using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class CaptionTrackCarriedTests
{
    private const string Artefact = """
        {
            "programs": [],
            "streams": [
                { "codec_name": "h264", "codec_type": "video", "disposition": { "default": 1 }, "tags": { "language": "und" } },
                { "codec_name": "aac", "codec_type": "audio", "disposition": { "default": 1 }, "tags": { "language": "und" } },
                { "codec_name": "bin_data", "codec_type": "data", "disposition": { "default": 0 } }
            ],
            "format": { "duration": "2097.295000" }
        }
        """;

    private const string Captioned = """
        {
            "streams": [
                { "codec_name": "h264", "codec_type": "video", "disposition": { "default": 1 } },
                { "codec_name": "aac", "codec_type": "audio", "disposition": { "default": 1 } },
                { "codec_name": "mov_text", "codec_type": "subtitle", "disposition": { "default": 0 }, "tags": { "language": "jpn" } },
                { "codec_name": "bin_data", "codec_type": "data", "disposition": { "default": 0 } }
            ],
            "format": { "duration": "2097.300000" }
        }
        """;

    [Fact]
    public void BrEd2019WhatFfprobeSaysIsReadAsTheTracksAFileCarriesAndHowLongItLasts()
    {
        CaptionTrackCarried read = Assert.IsType<CaptionTrackCarried>(CaptionTrackCarried.Read(Captioned));

        Assert.Equal((1, 1), (read.Pictures, read.Sounds));
        Assert.Equal([new CarriedSubtitle("mov_text", "jpn", false)], read.Subtitles);
        Assert.Equal(TimeSpan.FromSeconds(2097.3), read.Length);
    }

    [Fact]
    public void BrEd2019TheArtefactWithOneJapaneseTextTrackOffByDefaultPutInIsWhatWasAskedFor()
    {
        Assert.Null(CaptionTrackCarried.Differs(Read(Artefact), Read(Captioned)));
    }

    [Fact]
    public void BrEd2019AFileThatLostATrackGainedTheWrongOneOrDoesNotLastAsLongIsNotWhatWasAskedFor()
    {
        CaptionTrackCarried source = Read(Artefact);
        CaptionTrackCarried made = Read(Captioned);

        Assert.NotNull(CaptionTrackCarried.Differs(source, made with { Sounds = 0 }));
        Assert.NotNull(CaptionTrackCarried.Differs(source, made with { Pictures = 2 }));
        Assert.NotNull(CaptionTrackCarried.Differs(source, made with { Subtitles = [] }));
        Assert.NotNull(CaptionTrackCarried.Differs(source, made with { Subtitles = [.. made.Subtitles, .. made.Subtitles] }));
        Assert.NotNull(CaptionTrackCarried.Differs(source, made with { Subtitles = [new CarriedSubtitle("webvtt", "jpn", false)] }));
        Assert.NotNull(CaptionTrackCarried.Differs(source, made with { Subtitles = [new CarriedSubtitle("mov_text", "und", false)] }));
        Assert.NotNull(CaptionTrackCarried.Differs(source, made with { Subtitles = [new CarriedSubtitle("mov_text", "jpn", true)] }));
        Assert.NotNull(CaptionTrackCarried.Differs(source, made with { Length = TimeSpan.FromSeconds(2099) }));
        Assert.NotNull(CaptionTrackCarried.Differs(source, made with { Length = null }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{ \"format\": {} }")]
    public void WhatIsNotWhatFfprobeSaysIsNotRead(string said)
    {
        Assert.Null(CaptionTrackCarried.Read(said));
    }

    private static CaptionTrackCarried Read(string said) => Assert.IsType<CaptionTrackCarried>(CaptionTrackCarried.Read(said));
}
