using Carina.Domain.Encodings;
using Carina.Domain.Playback;

namespace Carina.Domain.Tests.Playback;

public sealed class ArtefactCodecReadingTests
{
    [Theory]
    [InlineData(EncodeCodec.H264)]
    [InlineData(EncodeCodec.H265)]
    public void AFileReadAsH264IsPlayedByEveryBrowserWhateverItsProfileSaysNow(EncodeCodec profileSays)
    {
        Assert.True(ArtefactCodecReading.Of(EncodeCodec.H264).EveryBrowserPlaysIt(profileSays));
    }

    [Theory]
    [InlineData(EncodeCodec.H264)]
    [InlineData(EncodeCodec.H265)]
    public void AFileReadAsH265IsNotPlayedByEveryBrowserWhateverItsProfileSaysNow(EncodeCodec profileSays)
    {
        Assert.False(ArtefactCodecReading.Of(EncodeCodec.H265).EveryBrowserPlaysIt(profileSays));
    }

    [Fact]
    public void AFileReadAsNeitherOfTheTwoIsNotPlayedByEveryBrowserEvenWhenItsProfileSaysH264()
    {
        ArtefactCodecReading reading = ArtefactCodecReading.Neither("mpeg2video");

        Assert.True(reading.Read);
        Assert.Null(reading.Codec);
        Assert.Contains("mpeg2video", reading.Note, StringComparison.Ordinal);
        Assert.False(reading.EveryBrowserPlaysIt(EncodeCodec.H264));
    }

    [Theory]
    [InlineData(EncodeCodec.H264, true)]
    [InlineData(EncodeCodec.H265, false)]
    public void AFileThatCouldNotBeReadIsJudgedByWhatItsProfileSays(EncodeCodec profileSays, bool plays)
    {
        ArtefactCodecReading reading = ArtefactCodecReading.Unread("the programme exited 1");

        Assert.False(reading.Read);
        Assert.Null(reading.Codec);
        Assert.Equal(plays, reading.EveryBrowserPlaysIt(profileSays));
    }

    [Fact]
    public void ACodecOutsideTheTwoOnOfferIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArtefactCodecReading.Of((EncodeCodec)99));
    }
}
