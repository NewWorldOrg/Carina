using Carina.Domain.Playback;

namespace Carina.Domain.Tests.Playback;

public sealed class PlaybackSourceTests
{
    [Fact]
    public void TheTwoThingsARecordingCanBePlayedFromAreNamedOnTheWireAndReadBackByThoseNames()
    {
        Assert.Equal(["artefact", "recording"], PlaybackSources.Names);
        Assert.Equal(
            PlaybackSources.Names,
            PlaybackSources.InOrder.Select(PlaybackSources.NameOf).ToArray());
        Assert.Equal(
            PlaybackSources.InOrder,
            PlaybackSources.Names.Select(name => PlaybackSources.Find(name)!.Value).ToArray());
    }

    [Fact]
    public void EveryThingARecordingCanBePlayedFromHasAName()
    {
        Assert.Equal(Enum.GetValues<PlaybackSource>().Length, PlaybackSources.InOrder.Count);
        Assert.Equal(PlaybackSources.InOrder.Count, PlaybackSources.Names.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Artefact")]
    [InlineData("recordings")]
    [InlineData("1")]
    public void SomethingThatIsNotOneOfTheTwoNamesIsReadAsNeitherOfThem(string? said)
    {
        Assert.Null(PlaybackSources.Find(said));
    }

    [Fact]
    public void SomethingThatIsNotOneOfTheTwoIsNotNamed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackSources.NameOf((PlaybackSource)99));
    }
}
