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
