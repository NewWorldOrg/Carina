using System.Reflection;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveSessionKeyTests
{
    private static readonly NetworkId Network = new(32736);

    private static readonly ServiceId Service = new(1024);

    [Fact]
    public void TwoKeysNamingTheSameChannelAndProfileAreOneKey()
    {
        LiveSessionKey one = new(new NetworkId(32736), new ServiceId(1024), LiveProfile.Find("720p30")!);
        LiveSessionKey another = new(Network, Service, LiveProfile.Hd30);

        Assert.Equal(one, another);
        Assert.Equal(one.GetHashCode(), another.GetHashCode());
    }

    [Fact]
    public void TheFrameRateAloneMakesAnotherKey()
    {
        LiveSessionKey everyFrame = new(Network, Service, LiveProfile.Hd30);
        LiveSessionKey everyField = new(Network, Service, LiveProfile.Hd60);

        Assert.Equal(everyFrame.Profile.Size, everyField.Profile.Size);
        Assert.NotEqual(everyFrame, everyField);
    }

    [Fact]
    public void AnotherServiceOnTheSameNetworkIsAnotherKey()
    {
        Assert.NotEqual(
            new LiveSessionKey(Network, Service, LiveProfile.Hd30),
            new LiveSessionKey(Network, new ServiceId(1025), LiveProfile.Hd30));
    }

    [Fact]
    public void TheSameServiceNumberOnAnotherNetworkIsAnotherKey()
    {
        Assert.NotEqual(
            new LiveSessionKey(Network, Service, LiveProfile.Hd30),
            new LiveSessionKey(new NetworkId(4), Service, LiveProfile.Hd30));
    }

    [Fact]
    public void TheSoundAloneMakesAnotherKey()
    {
        Assert.NotEqual(
            new LiveSessionKey(Network, Service, LiveProfile.Hd30),
            new LiveSessionKey(Network, Service, LiveProfile.Hd30, SoundTrack.Secondary));
    }

    [Fact]
    public void AKeyThatNamesNoSoundNamesTheMainOne()
    {
        Assert.Equal(SoundTrack.Main, new LiveSessionKey(Network, Service, LiveProfile.Hd30).Sound);
        Assert.Equal(
            new LiveSessionKey(Network, Service, LiveProfile.Hd30, SoundTrack.Main),
            new LiveSessionKey(Network, Service, LiveProfile.Hd30));
    }

    [Fact]
    public void AKeyReadsAsNetworkServiceProfileAndSound()
    {
        Assert.Equal("32736:1024:720p30:main", new LiveSessionKey(Network, Service, LiveProfile.Hd30).ToString());
        Assert.Equal(
            "32736:1024:720p30:secondary",
            new LiveSessionKey(Network, Service, LiveProfile.Hd30, SoundTrack.Secondary).ToString());
    }

    [Fact(DisplayName = "a key that names no placement takes the whole stream of the sound it names")]
    public void AKeyThatNamesNoPlacementTakesTheWholeStreamOfTheSoundItNames()
    {
        Assert.Equal(SoundPlacement.WholeStream(0), new LiveSessionKey(Network, Service, LiveProfile.Hd30).Placement);
        Assert.Equal(
            SoundPlacement.WholeStream(1),
            new LiveSessionKey(Network, Service, LiveProfile.Hd30, SoundTrack.Secondary).Placement);
    }

    [Fact(DisplayName = "BR-PD-008: the same sound taken from one channel of its stream is another key, and reads as one")]
    public void TheSameSoundTakenFromOneChannelOfItsStreamIsAnotherKey()
    {
        LiveSessionKey whole = new(Network, Service, LiveProfile.Hd30);
        LiveSessionKey left = whole.Taking(SoundPlacement.OneChannelOf(0, SoundChannel.Left));
        LiveSessionKey right = new LiveSessionKey(Network, Service, LiveProfile.Hd30, SoundTrack.Secondary)
            .Taking(SoundPlacement.OneChannelOf(0, SoundChannel.Right));

        Assert.NotEqual(whole, left);
        Assert.Equal(left, whole.Taking(SoundPlacement.OneChannelOf(0, SoundChannel.Left)));
        Assert.Equal(whole, left.Taking(SoundPlacement.WholeStream(0)));
        Assert.Equal(SoundTrack.Main, left.Sound);
        Assert.Equal(SoundPlacement.OneChannelOf(0, SoundChannel.Left), left.Placement);
        Assert.Equal("32736:1024:720p30:main:left", left.ToString());
        Assert.Equal("32736:1024:720p30:secondary:right", right.ToString());
        Assert.Throws<ArgumentNullException>(() => whole.Taking(null!));
    }

    [Fact]
    public void NothingOnAKeyCanBeChangedOnceItIsMade()
    {
        Assert.All(
            typeof(LiveSessionKey).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void AKeyIsNotMadeWithoutEveryPart()
    {
        Assert.Throws<ArgumentNullException>(() => new LiveSessionKey(null!, Service, LiveProfile.Hd30));
        Assert.Throws<ArgumentNullException>(() => new LiveSessionKey(Network, null!, LiveProfile.Hd30));
        Assert.Throws<ArgumentNullException>(() => new LiveSessionKey(Network, Service, null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LiveSessionKey(Network, Service, LiveProfile.Hd30, (SoundTrack)9));
    }
}
