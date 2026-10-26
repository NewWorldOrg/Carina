using Carina.Api.Playback;
using Carina.Domain.Playback;

using Microsoft.Extensions.Primitives;

namespace Carina.Api.Tests.Unit;

public sealed class AskedDecodingTests
{
    [Fact(DisplayName = "a browser naming no decoding is one that said nothing")]
    public void ABrowserNamingNoDecodingIsOneThatSaidNothing()
    {
        AskedDecoding asked = AskedDecoding.Read(StringValues.Empty);

        Assert.Equal(DecodingAnswer.Unasked, asked.Answer);
        Assert.Same(PlaybackAudience.BrowserSayingNothing, asked.Audience);
    }

    [Fact(DisplayName = "an empty decoding is read as none named")]
    public void AnEmptyDecodingIsReadAsNoneNamed()
    {
        Assert.Equal(DecodingAnswer.Unasked, AskedDecoding.Read(new StringValues(["", " "])).Answer);
    }

    [Theory(DisplayName = "a browser naming h265 among what it decodes is one that decodes H.265")]
    [InlineData("h265")]
    [InlineData("h264", "h265")]
    [InlineData("h265", "h265")]
    public void ABrowserNamingH265IsOneThatDecodesH265(params string[] named)
    {
        AskedDecoding asked = AskedDecoding.Read(new StringValues(named));

        Assert.Equal(DecodingAnswer.Named, asked.Answer);
        Assert.Same(PlaybackAudience.BrowserDecodingH265, asked.Audience);
    }

    [Fact(DisplayName = "a browser naming h264 alone decodes what one naming nothing does")]
    public void ABrowserNamingH264AloneDecodesWhatOneNamingNothingDoes()
    {
        AskedDecoding asked = AskedDecoding.Read(new StringValues("h264"));

        Assert.Equal(DecodingAnswer.Named, asked.Answer);
        Assert.Same(PlaybackAudience.BrowserSayingNothing, asked.Audience);
    }

    [Theory(DisplayName = "a browser naming av1 among what it decodes is taken at its word, and that alone hands it no artefact it did not have")]
    [InlineData("av1")]
    [InlineData("h264", "av1")]
    public void ABrowserNamingAv1IsTakenAtItsWord(params string[] named)
    {
        AskedDecoding asked = AskedDecoding.Read(new StringValues(named));

        Assert.Equal(DecodingAnswer.Named, asked.Answer);
        Assert.Same(PlaybackAudience.BrowserSayingNothing, asked.Audience);
    }

    [Theory(DisplayName = "a browser naming h265 and av1 is one that decodes H.265")]
    [InlineData("h265", "av1")]
    [InlineData("av1", "h265")]
    [InlineData("h264", "h265", "av1")]
    public void ABrowserNamingH265AndAv1IsOneThatDecodesH265(params string[] named)
    {
        AskedDecoding asked = AskedDecoding.Read(new StringValues(named));

        Assert.Equal(DecodingAnswer.Named, asked.Answer);
        Assert.Same(PlaybackAudience.BrowserDecodingH265, asked.Audience);
    }

    [Fact]
    public void TheDecodingsThereAreAreTheFormatsThereAre()
    {
        Assert.Equal(["h264", "h265", "av1"], AskedDecoding.Names);
    }

    [Theory(DisplayName = "a decoding outside the three, or spelled otherwise, is not one of these")]
    [InlineData("vp9")]
    [InlineData("AV1")]
    [InlineData("H265")]
    [InlineData("h265", "hevc")]
    [InlineData("h264,h265")]
    public void ADecodingOutsideTheThreeIsNotOneOfThese(params string[] named)
    {
        Assert.Equal(DecodingAnswer.NotOneOfThese, AskedDecoding.Read(new StringValues(named)).Answer);
    }
}
