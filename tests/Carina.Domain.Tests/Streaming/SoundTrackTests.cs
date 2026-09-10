using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class SoundTrackTests
{
    [Fact]
    public void TheMainSoundIsTheFirstOneTheBroadcastCarries()
    {
        Assert.Equal(0, SoundTracks.Ordinal(SoundTrack.Main));
    }

    [Fact]
    public void TheSecondarySoundIsTheOneAfterIt()
    {
        Assert.Equal(1, SoundTracks.Ordinal(SoundTrack.Secondary));
    }

    [Fact]
    public void EverySoundIsNamedAndEveryNameReadsBackAsTheSoundItNames()
    {
        Assert.Equal(["main", "secondary"], SoundTracks.Names);

        Assert.All(
            SoundTracks.InOrder,
            track => Assert.Equal(track, SoundTracks.Find(SoundTracks.NameOf(track))));
    }

    [Theory]
    [InlineData("Main")]
    [InlineData("MAIN")]
    [InlineData("sub")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElseNamesNoSound(string? said)
    {
        Assert.Null(SoundTracks.Find(said));
    }

    [Fact]
    public void ASoundThisApplicationDoesNotKnowIsNotNamedOrNumbered()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SoundTracks.NameOf((SoundTrack)7));
        Assert.Throws<ArgumentOutOfRangeException>(() => SoundTracks.Ordinal((SoundTrack)7));
    }

    [Fact]
    public void ABroadcastCarryingOneSoundOffersOnlyTheMainOne()
    {
        Assert.Equal([SoundTrack.Main], SoundTracks.OutOf(1));
    }

    [Fact]
    public void ABroadcastCarryingTwoSoundsOffersBoth()
    {
        Assert.Equal([SoundTrack.Main, SoundTrack.Secondary], SoundTracks.OutOf(2));
    }

    [Fact]
    public void ABroadcastCarryingMoreThanTwoOffersTheTwoThatCanBeAskedFor()
    {
        Assert.Equal([SoundTrack.Main, SoundTrack.Secondary], SoundTracks.OutOf(4));
    }

    [Fact]
    public void ABroadcastCarryingNoSoundOffersNone()
    {
        Assert.Empty(SoundTracks.OutOf(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SoundTracks.OutOf(-1));
    }
}
