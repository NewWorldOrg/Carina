using Carina.Contracts;
using Carina.Driver.Transport;

namespace Carina.Driver.Tests;

public sealed class PcrTimelineTests
{
    private const int VideoPid = 0x0100;
    private const int OtherServicePid = 0x0200;
    private const long Wrap = 8_589_934_592;
    private const long Second = 90_000;

    [Fact]
    public void AStreamNothingHasBeenReadFromIsNowhere()
    {
        var timeline = new PcrTimeline();

        Assert.False(timeline.Located);
        Assert.Null(timeline.Anchor);
        Assert.Equal(0, timeline.Second);
        Assert.Empty(timeline.Reanchors);
    }

    [Fact]
    public void AStreamThatNeverSaysWhatTimeItIsStaysNowhere()
    {
        var timeline = new PcrTimeline();

        for (int reading = 0; reading < 1000; reading++)
        {
            timeline.Observe(VideoPid, -1, declaredDiscontinuous: false);
        }

        Assert.False(timeline.Located);
        Assert.Null(timeline.Anchor);
    }

    [Fact]
    public void TheFirstReadingIsWhereTheTimelineStarts()
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, 4_500_000, declaredDiscontinuous: false);

        Assert.True(timeline.Located);
        Assert.Equal(4_500_000, timeline.Anchor);
        Assert.Equal(0, timeline.Second);
    }

    [Fact]
    public void TheSecondIsCountedFromTheFirstReadingAndNotFromZero()
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, 4_500_000, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 4_500_000 + (3 * Second), declaredDiscontinuous: false);

        Assert.Equal(3, timeline.Second);
        Assert.Equal(4_500_000, timeline.Anchor);
    }

    [Fact]
    public void ReadingsThatCreepForwardAddUpToTheSecondsBetweenThem()
    {
        var timeline = new PcrTimeline();

        for (int reading = 0; reading <= 50; reading++)
        {
            timeline.Observe(VideoPid, 100 + (reading * Second / 10), declaredDiscontinuous: false);
        }

        Assert.Equal(5, timeline.Second);
        Assert.Empty(timeline.Reanchors);
    }

    [Fact]
    public void AClockThatStopsLeavesTheTimelineWhereItWas()
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, 0, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 7 * Second, declaredDiscontinuous: false);

        for (int reading = 0; reading < 1000; reading++)
        {
            timeline.Observe(VideoPid, -1, declaredDiscontinuous: false);
        }

        Assert.Equal(7, timeline.Second);
        Assert.Empty(timeline.Reanchors);
    }

    [Fact]
    public void TheClockComingBackAroundIsNotABreakInTheStream()
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, Wrap - Second, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 0, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, Second, declaredDiscontinuous: false);

        Assert.Equal(2, timeline.Second);
        Assert.Empty(timeline.Reanchors);
    }

    [Fact]
    public void ReadingsPastTheEndOfTheClockAreNotBelieved()
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, 0, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, Wrap, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, Wrap + Second, declaredDiscontinuous: false);

        Assert.Equal(0, timeline.Anchor);
        Assert.Equal(0, timeline.Second);
        Assert.Empty(timeline.Reanchors);
    }

    [Fact]
    public void AClockThatJumpsBackwardsIsWrittenDownRatherThanFollowed()
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, 100 * Second, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 104 * Second, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 20 * Second, declaredDiscontinuous: false);

        Assert.Equal(4, timeline.Second);
        Assert.Equal([new PcrReanchorDto(4, 104 * Second, 20 * Second)], timeline.Reanchors);
    }

    [Fact]
    public void AClockThatJumpsForwardsOutOfReachIsWrittenDownRatherThanFollowed()
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, 0, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 4 * Second, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 3600 * Second, declaredDiscontinuous: false);

        Assert.Equal(4, timeline.Second);
        Assert.Equal([new PcrReanchorDto(4, 4 * Second, 3600 * Second)], timeline.Reanchors);
    }

    [Fact]
    public void TheTimelineGoesOnFromWhereItWasAfterABreak()
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, 100 * Second, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 104 * Second, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 20 * Second, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 26 * Second, declaredDiscontinuous: false);

        Assert.Equal(10, timeline.Second);
        Assert.Single(timeline.Reanchors);
    }

    [Theory]
    [InlineData(9, 9, 0)]
    [InlineData(11, 0, 1)]
    public void AGapWiderThanAnyBroadcastLeavesItIsABreakRatherThanTime(
        int gap,
        int expectedSecond,
        int expectedReanchors
    )
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, 0, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, gap * Second, declaredDiscontinuous: false);

        Assert.Equal(expectedSecond, timeline.Second);
        Assert.Equal(expectedReanchors, timeline.Reanchors.Count);
    }

    [Fact]
    public void TwoBreaksInTheSameSecondAreOneEntryNamingWhereItEndedUp()
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, 100 * Second, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 20 * Second, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 700 * Second, declaredDiscontinuous: false);

        Assert.Equal([new PcrReanchorDto(0, 100 * Second, 700 * Second)], timeline.Reanchors);
    }

    [Fact]
    public void EachBreakIsWrittenDownAgainstALaterSecondThanTheOneBefore()
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, 0, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 500 * Second, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 502 * Second, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 90 * Second, declaredDiscontinuous: false);

        Assert.Equal([0, 2], timeline.Reanchors.Select(reanchor => reanchor.Second));
    }

    [Fact]
    public void OnlyTheClockOfOneServiceIsFollowedThroughAWholeMultiplex()
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, 0, declaredDiscontinuous: false);
        timeline.Observe(OtherServicePid, 5_000_000_000, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 2 * Second, declaredDiscontinuous: false);
        timeline.Observe(OtherServicePid, 6_000_000_000, declaredDiscontinuous: false);

        Assert.Equal(0, timeline.Anchor);
        Assert.Equal(2, timeline.Second);
        Assert.Empty(timeline.Reanchors);
    }

    [Fact]
    public void AReadingThatRepeatsItselfIsNeitherTimeNorABreak()
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, 3 * Second, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 3 * Second, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 3 * Second, declaredDiscontinuous: false);

        Assert.Equal(0, timeline.Second);
        Assert.Empty(timeline.Reanchors);
    }

    [Fact]
    public void EveryBreakNamesClockReadingsTheStandardCanHold()
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, Wrap - 1, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, 700 * Second, declaredDiscontinuous: false);

        Assert.All(
            timeline.Reanchors,
            reanchor =>
            {
                Assert.InRange(reanchor.Before, 0, Wrap - 1);
                Assert.InRange(reanchor.After, 0, Wrap - 1);
            }
        );
        Assert.Equal(Wrap - 1, timeline.Reanchors[0].Before);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(90_000)]
    [InlineData(5 * 90_000)]
    public void AClockTheBroadcastSaysItBrokeIsWrittenDownHoweverSmallTheJump(long jump)
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, 100 * Second, declaredDiscontinuous: false);
        timeline.Observe(VideoPid, (100 * Second) + jump, declaredDiscontinuous: true);

        Assert.Equal(0, timeline.Second);
        Assert.Equal(
            [new PcrReanchorDto(0, 100 * Second, (100 * Second) + jump)],
            timeline.Reanchors
        );
    }

    [Fact]
    public void ABreakDeclaredOnTheFirstReadingIsStillWhereTheTimelineStarts()
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, 4_500_000, declaredDiscontinuous: true);

        Assert.Equal(4_500_000, timeline.Anchor);
        Assert.Empty(timeline.Reanchors);
    }

    [Fact]
    public void ABreakDeclaredByAServiceWeAreNotFollowingChangesNothing()
    {
        var timeline = new PcrTimeline();

        timeline.Observe(VideoPid, 0, declaredDiscontinuous: false);
        timeline.Observe(OtherServicePid, 5_000_000_000, declaredDiscontinuous: true);
        timeline.Observe(VideoPid, 2 * Second, declaredDiscontinuous: false);

        Assert.Equal(2, timeline.Second);
        Assert.Empty(timeline.Reanchors);
    }

    [Fact]
    public void TheWindowIsAHundredTimesTheLongestGapTheStandardAllowsBetweenReadings()
    {
        Assert.Equal(9_000, PcrTimeline.RepeatedAtLeastEvery);
        Assert.Equal(900_000, PcrTimeline.ContinuousWithin);
    }
}
