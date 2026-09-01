using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class PlaybackTargetTests
{
    [Fact]
    public void ARecordingAndALiveChannelOfTheSameNameAreDifferentThingsToWatch()
    {
        PlaybackTarget recording = PlaybackTarget.Recording("7");
        PlaybackTarget live = PlaybackTarget.LiveChannel("7");

        Assert.NotEqual(recording, live);
        Assert.NotEqual(recording.Value, live.Value);
    }

    [Fact]
    public void TwoNamesOfTheSameRecordingAreTheSameTarget()
    {
        Assert.Equal(PlaybackTarget.Recording("7"), PlaybackTarget.Recording("7"));
        Assert.Equal(
            PlaybackTarget.Recording("7").GetHashCode(),
            PlaybackTarget.Recording("7").GetHashCode());
    }

    [Fact]
    public void TwoRecordingsAreNotTheSameTarget()
    {
        Assert.NotEqual(PlaybackTarget.Recording("7"), PlaybackTarget.Recording("8"));
    }

    [Fact]
    public void ATargetCarriesWhichKindOfThingItNames()
    {
        Assert.Equal(PlaybackTargetKind.Recording, PlaybackTarget.Recording("7").Kind);
        Assert.Equal(PlaybackTargetKind.LiveChannel, PlaybackTarget.LiveChannel("7").Kind);
        Assert.Equal("7", PlaybackTarget.Recording("7").Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" 7")]
    [InlineData("7 ")]
    public void ATargetNamesSomethingAndIsNotPadded(string name)
    {
        Assert.Throws<ArgumentException>(() => PlaybackTarget.Recording(name));
    }

    [Fact]
    public void ATargetCarriesNoSeparatorBecauseTheTwoHalvesWouldStopBeingOneAnswer()
    {
        Assert.Throws<ArgumentException>(() => PlaybackTarget.Recording("live-channel/7"));
    }

    [Fact]
    public void ATargetCarriesNoControlCharacters()
    {
        Assert.Throws<ArgumentException>(() => PlaybackTarget.Recording("7\n8"));
    }

    [Fact]
    public void ATargetIsBoundedInLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PlaybackTarget.Recording(new string('7', PlaybackTarget.LongestName + 1)));
    }

    [Fact]
    public void ATargetRefusesToBeNamedByNothing()
    {
        Assert.Throws<ArgumentNullException>(() => PlaybackTarget.Recording(null!));
    }
}
