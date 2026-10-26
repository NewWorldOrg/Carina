using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class VideoCodecTests
{
    [Fact(DisplayName = "a picture transcoded as it plays is encoded in H.264, H.265 or AV1")]
    public void APictureTranscodedAsItPlaysIsEncodedInH264H265OrAv1()
    {
        Assert.Equal([VideoCodec.H264, VideoCodec.H265, VideoCodec.Av1], Enum.GetValues<VideoCodec>());
        Assert.Equal(Enum.GetValues<VideoCodec>(), VideoCodecs.InOrder);
    }

    [Fact]
    public void EachFormatIsAskedForByTheNameTheQueryUses()
    {
        Assert.Equal(["h264", "h265", "av1"], VideoCodecs.Names);
        Assert.Equal(VideoCodecs.Names, VideoCodecs.InOrder.Select(VideoCodecs.NameOf));
    }

    [Theory]
    [InlineData("h264", VideoCodec.H264)]
    [InlineData("h265", VideoCodec.H265)]
    [InlineData("av1", VideoCodec.Av1)]
    public void EveryNameFindsTheFormatItNames(string name, VideoCodec format)
    {
        Assert.Equal(format, VideoCodecs.Find(name));
    }

    [Theory]
    [InlineData("H265")]
    [InlineData("hevc")]
    [InlineData("AV1")]
    [InlineData(" h264")]
    [InlineData("vp9")]
    [InlineData("")]
    [InlineData(null)]
    public void ANameThatIsNotOnTheListIsNoFormat(string? name)
    {
        Assert.Null(VideoCodecs.Find(name));
    }

    [Fact(DisplayName = "a picture nobody asked a format for is H.264, as it always was")]
    public void APictureNobodyAskedAFormatForIsH264()
    {
        Assert.Equal(VideoCodec.H264, VideoCodecs.Unasked);
    }

    [Fact]
    public void AFormatNobodyNamedHasNoName()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoCodecs.NameOf((VideoCodec)9));
    }
}
