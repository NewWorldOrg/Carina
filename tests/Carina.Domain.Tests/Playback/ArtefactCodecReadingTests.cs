using Carina.Domain.Encodings;
using Carina.Domain.Playback;

namespace Carina.Domain.Tests.Playback;

public sealed class ArtefactCodecReadingTests
{
    [Fact(DisplayName = "a file read as H.265 tagged hvc1 says it is tagged the way Safari plays")]
    public void AFileReadAsH265TaggedHvc1IsTaggedTheWaySafariPlays()
    {
        ArtefactCodecReading reading = ArtefactCodecReading.Of(EncodeCodec.H265, "hvc1");

        Assert.True(reading.Read);
        Assert.Equal(EncodeCodec.H265, reading.Codec);
        Assert.Equal("hvc1", reading.Tag);
        Assert.True(reading.TaggedTheWaySafariPlays);
        Assert.Contains("hvc1", reading.Note, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "a file tagged anything but hvc1, or not tagged at all, is not tagged the way Safari plays")]
    [InlineData("hev1")]
    [InlineData("HVC1")]
    [InlineData("")]
    [InlineData(null)]
    public void AFileTaggedAnythingButHvc1IsNotTaggedTheWaySafariPlays(string? tag)
    {
        ArtefactCodecReading reading = ArtefactCodecReading.Of(EncodeCodec.H265, tag);

        Assert.False(reading.TaggedTheWaySafariPlays);
    }

    [Fact]
    public void AFileReadAsNeitherOfTheTwoNamesWhatWasRead()
    {
        ArtefactCodecReading reading = ArtefactCodecReading.Neither("mpeg2video");

        Assert.True(reading.Read);
        Assert.Null(reading.Codec);
        Assert.Contains("mpeg2video", reading.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatCouldNotBeReadNamesNoCodecAndNoTag()
    {
        ArtefactCodecReading reading = ArtefactCodecReading.Unread("the programme exited 1");

        Assert.False(reading.Read);
        Assert.Null(reading.Codec);
        Assert.Null(reading.Tag);
    }

    [Fact]
    public void ACodecOutsideTheTwoOnOfferIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArtefactCodecReading.Of((EncodeCodec)99));
    }
}
