using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Reservations;
using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeSoundTests
{
    [Fact(DisplayName = "BR-PD-008: a broadcast that put two languages on one sound is baked from the channel the main language sits on")]
    public void TwoLanguagesOnOneSoundAreBakedFromTheChannelTheMainOneSitsOn()
    {
        EncodeSound sound = EncodeSound.Of(AudioMode.DualMono, 1);

        Assert.Equal(SoundPlacement.OneChannelOf(0, SoundChannel.Left), sound.OneChannel);
    }

    [Fact]
    public void WhatIsBakedIsWhereTheMainSoundIsPlayedFromRatherThanARuleOfItsOwn()
        => Assert.Equal(
            SoundArrangement.Of(new AnnouncedSound(AudioMode.DualMono, 1)).Placement(SoundTrack.Main),
            EncodeSound.Of(AudioMode.DualMono, 1).OneChannel);

    [Theory]
    [InlineData(AudioMode.Undetermined, ProgrammeSnapshot.SoundsUnannounced)]
    [InlineData(AudioMode.Mono, 1)]
    [InlineData(AudioMode.Stereo, 1)]
    [InlineData(AudioMode.Surround, 1)]
    [InlineData(AudioMode.DualMono, 2)]
    [InlineData(AudioMode.Stereo, 2)]
    public void ASoundOnAStreamOfItsOwnIsLeftWhereItIs(AudioMode audio, int sounds)
    {
        Assert.Null(EncodeSound.Of(audio, sounds).OneChannel);
        Assert.Same(EncodeSound.EveryStreamAsItStands, EncodeSound.Of(audio, sounds));
    }
}
