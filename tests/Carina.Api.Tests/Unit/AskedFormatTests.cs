using Carina.Api.Playback;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.Unit;

public sealed class AskedFormatTests
{
    [Theory(DisplayName = "a recording played without naming a format is transcoded in H.264, as it always was")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ARecordingPlayedWithoutNamingAFormatIsTranscodedInH264(string? asked)
    {
        AskedFormat format = AskedFormat.Read(asked);

        Assert.Equal(FormatAnswer.Unasked, format.Answer);
        Assert.Equal(VideoCodec.H264, format.Format);
    }

    [Theory]
    [InlineData("h264", VideoCodec.H264)]
    [InlineData("h265", VideoCodec.H265)]
    [InlineData("av1", VideoCodec.Av1)]
    public void AFormatNamedIsTheFormatAskedFor(string asked, VideoCodec format)
    {
        AskedFormat read = AskedFormat.Read(asked);

        Assert.Equal(FormatAnswer.Named, read.Answer);
        Assert.Equal(format, read.Format);
    }

    [Theory(DisplayName = "a format off the list, or spelled otherwise, is not one of these")]
    [InlineData("H265")]
    [InlineData("hevc")]
    [InlineData("vp9")]
    [InlineData("h264,h265")]
    public void AFormatOffTheListIsNotOneOfThese(string asked)
    {
        Assert.Equal(FormatAnswer.NotOneOfThese, AskedFormat.Read(asked).Answer);
    }
}
