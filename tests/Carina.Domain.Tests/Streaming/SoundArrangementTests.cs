using Carina.Domain.Base;
using Carina.Domain.Reservations;
using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class SoundArrangementTests
{
    [Fact]
    public void ARecordingThatAnnouncedNothingIsAskedOfTheStreamInstead()
    {
        Assert.True(new AnnouncedSound(AudioMode.Undetermined, ProgrammeSnapshot.SoundsUnannounced).SaidNothing);
        Assert.False(new AnnouncedSound(AudioMode.DualMono, ProgrammeSnapshot.SoundsUnannounced).SaidNothing);
        Assert.False(new AnnouncedSound(AudioMode.Undetermined, 1).SaidNothing);
    }

    [Theory]
    [InlineData(AudioMode.Undetermined, 0)]
    [InlineData(AudioMode.Mono, 1)]
    [InlineData(AudioMode.Stereo, 1)]
    [InlineData(AudioMode.Surround, 1)]
    public void ABroadcastThatAnnouncedTheOneSoundOffersTheWholeOfTheFirstStreamAndNothingElse(
        AudioMode audio,
        int sounds)
    {
        SoundArrangement arrangement = SoundArrangement.Of(new AnnouncedSound(audio, sounds));

        Assert.Equal([SoundTrack.Main], arrangement.Tracks);
        Assert.Equal(SoundPlacement.WholeStream(0), arrangement.Placement(SoundTrack.Main));
        Assert.False(arrangement.Holds(SoundTrack.Secondary));
    }

    [Theory]
    [InlineData(ProgrammeSnapshot.SoundsUnannounced)]
    [InlineData(1)]
    public void ABroadcastThatAnnouncedTwoLanguagesOnOneStreamOffersEachOfItsChannels(int sounds)
    {
        SoundArrangement arrangement = SoundArrangement.Of(new AnnouncedSound(AudioMode.DualMono, sounds));

        Assert.Equal([SoundTrack.Main, SoundTrack.Secondary], arrangement.Tracks);
        Assert.Equal(
            SoundPlacement.OneChannelOf(0, SoundChannel.Left),
            arrangement.Placement(SoundTrack.Main));
        Assert.Equal(
            SoundPlacement.OneChannelOf(0, SoundChannel.Right),
            arrangement.Placement(SoundTrack.Secondary));
    }

    [Theory]
    [InlineData(AudioMode.Stereo, 2)]
    [InlineData(AudioMode.Mono, 2)]
    [InlineData(AudioMode.DualMono, 2)]
    [InlineData(AudioMode.Undetermined, 2)]
    [InlineData(AudioMode.Stereo, 3)]
    public void ABroadcastThatAnnouncedItsSoundsOnStreamsOfTheirOwnOffersThemByTheirPlaceInTheProgramme(
        AudioMode audio,
        int sounds)
    {
        SoundArrangement arrangement = SoundArrangement.Of(new AnnouncedSound(audio, sounds));

        Assert.Equal([SoundTrack.Main, SoundTrack.Secondary], arrangement.Tracks);
        Assert.Equal(SoundPlacement.WholeStream(0), arrangement.Placement(SoundTrack.Main));
        Assert.Equal(SoundPlacement.WholeStream(1), arrangement.Placement(SoundTrack.Secondary));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 2)]
    public void ARecordingAskedOfItsOwnStreamOffersAWholeStreamForEachSoundTheStreamCarries(int carried, int offered)
    {
        SoundArrangement arrangement = SoundArrangement.Of(CarriedSounds.Counted(carried));

        Assert.Equal(SoundTracks.OutOf(offered), arrangement.Tracks);
        Assert.All(
            arrangement.Tracks,
            track => Assert.Equal(
                SoundPlacement.WholeStream(SoundTracks.Ordinal(track)),
                arrangement.Placement(track)));
    }

    [Fact]
    public void AStreamWhoseSoundsCouldNotBeReadOffersNoneOfThem()
    {
        SoundArrangement arrangement = SoundArrangement.Of(CarriedSounds.Unread("the programme said nothing"));

        Assert.Empty(arrangement.Tracks);
        Assert.False(arrangement.Holds(SoundTrack.Main));
    }

    [Fact]
    public void ASoundTheArrangementDoesNotOfferHasNowhereToBeTakenFrom()
    {
        SoundArrangement one = SoundArrangement.Of(new AnnouncedSound(AudioMode.Stereo, 1));

        Assert.Throws<ArgumentOutOfRangeException>(() => one.Placement(SoundTrack.Secondary));
        Assert.Throws<ArgumentOutOfRangeException>(() => one.Placement((SoundTrack)9));
    }

    [Fact]
    public void EveryArrangementIsAskedOfSomethingRatherThanOfNothing()
    {
        Assert.Throws<ArgumentNullException>(() => SoundArrangement.Of(null!));
    }

    [Fact]
    public void AWholeStreamIsTakenAsItIsAndOneChannelOfItIsNamedByTheSideItIsOn()
    {
        SoundPlacement whole = SoundPlacement.WholeStream(1);
        SoundPlacement left = SoundPlacement.OneChannelOf(0, SoundChannel.Left);

        Assert.True(whole.IsWholeStream);
        Assert.Null(whole.Channel);
        Assert.Equal(1, whole.Ordinal);
        Assert.False(left.IsWholeStream);
        Assert.Equal(SoundChannel.Left, left.Channel);
        Assert.Equal(0, left.Ordinal);
    }

    [Fact]
    public void OnlyTheWholeOfTheFirstStreamIsTheOneEveryPlayableRecordingHas()
    {
        Assert.True(SoundPlacement.WholeStream(0).IsAllOfTheFirstStream);
        Assert.False(SoundPlacement.WholeStream(1).IsAllOfTheFirstStream);
        Assert.False(SoundPlacement.OneChannelOf(0, SoundChannel.Left).IsAllOfTheFirstStream);
        Assert.False(SoundPlacement.OneChannelOf(0, SoundChannel.Right).IsAllOfTheFirstStream);
    }

    [Fact]
    public void ASoundIsTakenFromAStreamTheProgrammeCouldHaveAndFromASideAChannelCouldBeOn()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SoundPlacement.WholeStream(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SoundPlacement.OneChannelOf(-1, SoundChannel.Left));
        Assert.Throws<ArgumentOutOfRangeException>(() => SoundPlacement.OneChannelOf(0, (SoundChannel)9));
    }
}
