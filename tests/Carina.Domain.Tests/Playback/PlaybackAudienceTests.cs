using Carina.Domain.Encodings;
using Carina.Domain.Playback;

namespace Carina.Domain.Tests.Playback;

public sealed class PlaybackAudienceTests
{
    public static TheoryData<PlaybackAudience> EveryAudience => new()
    {
        PlaybackAudience.BrowserSayingNothing,
        PlaybackAudience.BrowserDecodingH265,
        PlaybackAudience.ExternalPlayer,
    };

    [Theory(DisplayName = "a file read as H.264 is played by everyone, whatever its profile says now")]
    [MemberData(nameof(EveryAudience))]
    public void AFileReadAsH264IsPlayedByEveryone(PlaybackAudience audience)
    {
        Assert.True(audience.Plays(ArtefactCodecReading.Of(EncodeCodec.H264, "avc1"), EncodeCodec.H264));
        Assert.True(audience.Plays(ArtefactCodecReading.Of(EncodeCodec.H264, "avc1"), EncodeCodec.H265));
    }

    [Fact(DisplayName = "a browser that said nothing is not handed H.265, however it is tagged")]
    public void ABrowserThatSaidNothingIsNotHandedH265()
    {
        PlaybackAudience browser = PlaybackAudience.BrowserSayingNothing;

        Assert.False(browser.Plays(ArtefactCodecReading.Of(EncodeCodec.H265, "hvc1"), EncodeCodec.H265));
        Assert.False(browser.Plays(ArtefactCodecReading.Of(EncodeCodec.H265, "hev1"), EncodeCodec.H265));
    }

    [Fact(DisplayName = "a browser that said it decodes H.265 is handed H.265 tagged hvc1, and not H.265 tagged hev1")]
    public void ABrowserThatSaidItDecodesH265IsHandedH265TaggedHvc1Only()
    {
        PlaybackAudience browser = PlaybackAudience.BrowserDecodingH265;

        Assert.True(browser.Plays(ArtefactCodecReading.Of(EncodeCodec.H265, "hvc1"), EncodeCodec.H264));
        Assert.False(browser.Plays(ArtefactCodecReading.Of(EncodeCodec.H265, "hev1"), EncodeCodec.H265));
        Assert.False(browser.Plays(ArtefactCodecReading.Of(EncodeCodec.H265), EncodeCodec.H265));
    }

    [Fact(DisplayName = "an external player is handed H.265 however it is tagged")]
    public void AnExternalPlayerIsHandedH265HoweverItIsTagged()
    {
        PlaybackAudience player = PlaybackAudience.ExternalPlayer;

        Assert.True(player.Plays(ArtefactCodecReading.Of(EncodeCodec.H265, "hvc1"), EncodeCodec.H264));
        Assert.True(player.Plays(ArtefactCodecReading.Of(EncodeCodec.H265, "hev1"), EncodeCodec.H265));
    }

    [Theory(DisplayName = "a file read as neither of the two codecs is handed to no one")]
    [MemberData(nameof(EveryAudience))]
    public void AFileReadAsNeitherIsHandedToNoOne(PlaybackAudience audience)
    {
        Assert.False(audience.Plays(ArtefactCodecReading.Neither("mpeg2video"), EncodeCodec.H264));
    }

    [Theory(DisplayName = "a file that could not be read is judged by its profile, and a browser is not handed it when the profile says H.265")]
    [InlineData(EncodeCodec.H264, true, true, true)]
    [InlineData(EncodeCodec.H265, false, false, true)]
    public void AFileThatCouldNotBeReadIsJudgedByItsProfile(
        EncodeCodec profileSays,
        bool saidNothing,
        bool decodesH265,
        bool external)
    {
        ArtefactCodecReading unread = ArtefactCodecReading.Unread("the programme exited 1");

        Assert.Equal(saidNothing, PlaybackAudience.BrowserSayingNothing.Plays(unread, profileSays));
        Assert.Equal(decodesH265, PlaybackAudience.BrowserDecodingH265.Plays(unread, profileSays));
        Assert.Equal(external, PlaybackAudience.ExternalPlayer.Plays(unread, profileSays));
    }

    [Fact]
    public void ABrowserIsNamedByWhetherItSaidItDecodesH265()
    {
        Assert.Same(PlaybackAudience.BrowserDecodingH265, PlaybackAudience.Browser(decodesH265: true));
        Assert.Same(PlaybackAudience.BrowserSayingNothing, PlaybackAudience.Browser(decodesH265: false));
        Assert.True(PlaybackAudience.BrowserSayingNothing.IsBrowser);
        Assert.False(PlaybackAudience.ExternalPlayer.IsBrowser);
    }

    [Fact]
    public void AProfileCodecOutsideTheTwoOnOfferIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PlaybackAudience.ExternalPlayer.Plays(ArtefactCodecReading.Unread("unread"), (EncodeCodec)99));
    }
}
