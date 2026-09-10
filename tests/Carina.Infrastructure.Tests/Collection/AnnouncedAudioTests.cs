using Carina.Broadcast.Descriptors;
using Carina.Domain.Base;
using Carina.Infrastructure.Collection;

namespace Carina.Infrastructure.Tests.Collection;

public sealed class AnnouncedAudioTests
{
    [Theory]
    [InlineData(0x01, AudioMode.Mono)]
    [InlineData(0x02, AudioMode.DualMono)]
    [InlineData(0x03, AudioMode.Stereo)]
    [InlineData(0x04, AudioMode.Surround)]
    [InlineData(0x09, AudioMode.Surround)]
    [InlineData(0x11, AudioMode.Surround)]
    public void TheAnnouncedComponentTypeNamesHowTheSoundIsCarried(int componentType, AudioMode expected)
    {
        Assert.Equal(expected, AnnouncedAudio.Of([Component(componentType)]));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x12)]
    [InlineData(0x40)]
    [InlineData(0x41)]
    public void AComponentTypeThatNamesNoModeLeavesTheSoundUnanswered(int componentType)
    {
        Assert.Equal(AudioMode.Undetermined, AnnouncedAudio.Of([Component(componentType)]));
    }

    [Fact]
    public void ABroadcastThatAnnouncedNoSoundAtAllLeavesItUnanswered()
    {
        Assert.Equal(AudioMode.Undetermined, AnnouncedAudio.Of([]));
    }

    [Fact]
    public void TheMainComponentIsTheOneReadEvenWhenAnotherWasAnnouncedFirst()
    {
        Assert.Equal(
            AudioMode.DualMono,
            AnnouncedAudio.Of([Component(0x03, main: false), Component(0x02, main: true)]));
    }

    [Fact]
    public void WhenNothingCallsItselfTheMainComponentTheFirstAnnouncedIsRead()
    {
        Assert.Equal(
            AudioMode.Mono,
            AnnouncedAudio.Of([Component(0x01, main: false), Component(0x03, main: false)]));
    }

    [Fact]
    public void AComponentThatIsNotSoundIsPassedOverRatherThanReadAsAMode()
    {
        Assert.Equal(
            AudioMode.Stereo,
            AnnouncedAudio.Of([Component(0x02, streamContent: 1), Component(0x03)]));
    }

    [Fact]
    public void NothingIsReadFromAListThatWasNeverHandedOver()
    {
        Assert.Throws<ArgumentNullException>(() => AnnouncedAudio.Of(null!));
    }

    [Fact]
    public void TwoSoundsAnnouncedSideBySideAreBothCounted()
    {
        Assert.Equal(2, AnnouncedAudio.Sounds([Component(0x03), Component(0x03, main: false)]));
    }

    [Fact]
    public void ASecondSoundIsCountedEvenThoughTheModeReadsTheMainOneAlone()
    {
        IReadOnlyList<AudioComponentDescription> announced = [Component(0x03), Component(0x03, main: false)];

        Assert.Equal(AudioMode.Stereo, AnnouncedAudio.Of(announced));
        Assert.Equal(2, AnnouncedAudio.Sounds(announced));
    }

    [Fact]
    public void AComponentThatIsNotSoundIsNotCountedAmongTheSounds()
    {
        Assert.Equal(1, AnnouncedAudio.Sounds([Component(0x03, streamContent: 1), Component(0x03)]));
    }

    [Fact]
    public void ABroadcastThatAnnouncedNoSoundAtAllCountsNone()
    {
        Assert.Equal(0, AnnouncedAudio.Sounds([]));
    }

    [Fact]
    public void NoCountIsReadFromAListThatWasNeverHandedOver()
    {
        Assert.Throws<ArgumentNullException>(() => AnnouncedAudio.Sounds(null!));
    }

    private static AudioComponentDescription Component(int componentType, bool main = true, int streamContent = 2)
        => new(
            streamContent,
            componentType,
            0x10,
            0x0F,
            0xFF,
            main,
            2,
            7,
            "jpn",
            string.Empty,
            string.Empty);
}
