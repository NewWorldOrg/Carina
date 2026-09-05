using Carina.Domain.Playback;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Playback;

public sealed class PlaybackSeekingTests
{
    [Fact]
    public void AnEncodedRecordingIsMovedAboutByAskingForAnotherRangeOfTheSameFile()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            new PlaybackSubject(RecordingOutcome.Complete, OnDisk("a1b2c3.m2ts", 4_000_000), [Written("a1b2c3.mp4", 900_000)]));

        Assert.Equal(PlaybackRoute.Direct, plan.Route);
        Assert.Equal(PlaybackSeeking.ByRange, plan.Seeking);
    }

    [Fact]
    public void ARecordingNothingHasEncodedIsMovedAboutByStartingATranscoderAgain()
    {
        PlaybackPlan plan = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, OnDisk("a1b2c3.m2ts", 4_000_000)));

        Assert.Equal(PlaybackRoute.OnTheFly, plan.Route);
        Assert.Equal(PlaybackSeeking.ByStartingAgain, plan.Seeking);
    }

    [Fact]
    public void ARecordingThatPlaysAtAllSaysHowItIsMovedAboutAndOneThatDoesNotSaysNothing()
    {
        PlaybackPlan nothing = PlaybackPlan.For(
            PlaybackSubject.NothingHasBeenEncodedYet(RecordingOutcome.Complete, OnDisk("a1b2c3.m2ts", 0)));

        Assert.False(nothing.PlaysAtAll);
        Assert.Null(nothing.Seeking);
    }

    [Fact]
    public void EveryRouteThatPlaysAnythingNamesADifferentWayOfMovingAbout()
    {
        PlaybackSeeking?[] ways = [.. Enum.GetValues<PlaybackRoute>().Select(PlaybackSeekings.Of)];

        Assert.Equal(Enum.GetValues<PlaybackSeeking>().Length, ways.OfType<PlaybackSeeking>().Distinct().Count());
    }

    [Fact]
    public void ARouteThatIsNotOneOfTheThreeIsNotReadAsOneThatIs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackSeekings.Of((PlaybackRoute)99));
    }

    private static PlaybackFileSearch OnDisk(string name, long bytes) => PlaybackFileSearch.Of(Written(name, bytes));

    private static PlaybackFile Written(string name, long bytes)
        => new(new OutputRoot("bulk"), new RecordingFileName(name), bytes);
}
