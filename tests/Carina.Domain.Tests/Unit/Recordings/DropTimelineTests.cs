using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Unit.Recordings;

[Trait("Category", "Unit")]
public sealed class DropTimelineTests
{
    private const long Anchor = 900_000;

    private static readonly TimeSpan TenMinutesIn = TimeSpan.FromMinutes(10);

    [Fact]
    public void ATimelineNothingLocatedCarriesNoPositionAtAll()
    {
        DropTimeline unlocated = DropTimeline.Unlocated;

        Assert.False(unlocated.Located);
        Assert.Null(unlocated.AnchorPcr);
        Assert.Empty(unlocated.Buckets);
        Assert.Empty(unlocated.Reanchors);
    }

    [Fact]
    public void LocatingNothingIsNotTheSameAsLocatingNowhere()
    {
        DropTimeline located = DropTimeline.AnchoredAt(900_000);

        Assert.True(located.Located);
        Assert.Empty(located.Buckets);
        Assert.Equal(0, located.Continuity);
        Assert.NotEqual(DropTimeline.Unlocated, located);
    }

    [Fact]
    public void APositionWithoutAnAnchorCannotBeMappedBackSoItIsRefused()
    {
        Assert.Equal(
            "anchorPcr",
            Assert.Throws<ArgumentException>(
                () => DropTimeline.Rehydrate(null, [new DropBucket(12, 3, 0)], [])).ParamName);
        Assert.Equal(
            "anchorPcr",
            Assert.Throws<ArgumentException>(
                () => DropTimeline.Rehydrate(null, [], [new PcrReanchor(12, 1, 2)])).ParamName);
    }

    [Fact]
    public void ATimelineReadsForwardsAndNamesEachSecondOnce()
    {
        Assert.Equal(
            "buckets",
            Assert.Throws<ArgumentException>(() => DropTimeline.Rehydrate(
                0,
                [new DropBucket(12, 1, 0), new DropBucket(12, 1, 0)],
                [])).ParamName);
        Assert.Equal(
            "buckets",
            Assert.Throws<ArgumentException>(() => DropTimeline.Rehydrate(
                0,
                [new DropBucket(13, 1, 0), new DropBucket(12, 1, 0)],
                [])).ParamName);
        Assert.Equal(
            "reanchors",
            Assert.Throws<ArgumentException>(() => DropTimeline.Rehydrate(
                0,
                [],
                [new PcrReanchor(9, 1, 2), new PcrReanchor(9, 1, 2)])).ParamName);
    }

    [Fact]
    public void ATimelineStartsAtTheBeginningOfTheStreamOrLater()
    {
        Assert.Equal(
            "buckets",
            Assert.Throws<ArgumentException>(
                () => DropTimeline.Rehydrate(0, [new DropBucket(-1, 1, 0)], [])).ParamName);
        Assert.Equal(
            "reanchors",
            Assert.Throws<ArgumentException>(
                () => DropTimeline.Rehydrate(0, [], [new PcrReanchor(-1, 1, 0)])).ParamName);
        Assert.Equal(0, Assert.Single(DropTimeline.Rehydrate(0, [new DropBucket(0, 1, 0)], []).Buckets).Second);
    }

    [Fact]
    public void ATimelineNamesOnlyTheSecondsWhereSomethingHappened()
        => Assert.Equal(
            "buckets",
            Assert.Throws<ArgumentException>(
                () => DropTimeline.Rehydrate(0, [new DropBucket(12, 0, 0)], [])).ParamName);

    [Fact]
    public void ASecondCannotLoseANegativeNumberOfPackets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DropTimeline.Rehydrate(0, [new DropBucket(12, -1, 0)], []));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DropTimeline.Rehydrate(0, [new DropBucket(12, 0, -1)], []));
    }

    [Fact]
    public void ALostPacketAndAScrambledOneAreCountedApart()
    {
        DropTimeline timeline = DropTimeline.Rehydrate(
            0,
            [new DropBucket(12, 3, 0), new DropBucket(40, 0, 188)],
            []);

        Assert.Equal(3, timeline.Continuity);
        Assert.Equal(188, timeline.Scrambled);
    }

    [Fact]
    public void TheClockIsTheThirtyThreeBitsTheStandardGivesIt()
    {
        Assert.Equal(8_589_934_592, DropTimeline.PcrWrapsAt);
        Assert.Equal(8_589_934_591, DropTimeline.AnchoredAt(8_589_934_591).AnchorPcr);
        Assert.Throws<ArgumentOutOfRangeException>(() => DropTimeline.AnchoredAt(8_589_934_592));
        Assert.Equal(
            4_294_967_296,
            DropTimeline.AnchoredAt(4_294_967_296).AnchorPcr);
    }

    [Fact]
    public void TheAnchorLivesInsideTheClockItReads()
    {
        Assert.Equal(
            DropTimeline.PcrWrapsAt - 1,
            DropTimeline.AnchoredAt(DropTimeline.PcrWrapsAt - 1).AnchorPcr);
        Assert.Equal(0L, DropTimeline.AnchoredAt(0).AnchorPcr);
        Assert.Throws<ArgumentOutOfRangeException>(() => DropTimeline.AnchoredAt(DropTimeline.PcrWrapsAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => DropTimeline.AnchoredAt(-1));
    }

    [Fact]
    public void AClockThatStartedAgainIsRecordedAsAReanchorRatherThanGoingBackwards()
    {
        DropTimeline timeline = DropTimeline.Rehydrate(
            DropTimeline.PcrWrapsAt - 90_000,
            [new DropBucket(1, 2, 0), new DropBucket(4, 1, 0)],
            [new PcrReanchor(2, DropTimeline.PcrWrapsAt - 1, 0)]);

        Assert.Equal([1, 4], timeline.Buckets.Select(bucket => bucket.Second));
        PcrReanchor wrapped = Assert.Single(timeline.Reanchors);
        Assert.Equal(DropTimeline.PcrWrapsAt - 1, wrapped.Before);
        Assert.Equal(0, wrapped.After);
    }

    [Fact]
    public void AReanchorOutsideTheClockIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DropTimeline.Rehydrate(0, [], [new PcrReanchor(2, DropTimeline.PcrWrapsAt, 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DropTimeline.Rehydrate(0, [], [new PcrReanchor(2, 0, -1)]));
    }

    [Fact(DisplayName = "a later session's seconds are placed as far along as its clock began after the recording's")]
    public void ALaterSessionsSecondsArePlacedAsFarAlongAsItsClockBegan()
    {
        DropTimeline before = DropTimeline.Rehydrate(Anchor, [new DropBucket(12, 3, 0)], []);
        DropTimeline later = DropTimeline.Rehydrate(
            Anchor + (603 * DropTimeline.TicksPerSecond),
            [new DropBucket(5, 2, 1)],
            [new PcrReanchor(7, 1_000, 2_000)]);

        DropTimeline whole = before.Then(later, TenMinutesIn);

        Assert.Equal(Anchor, whole.AnchorPcr);
        Assert.Equal([new DropBucket(12, 3, 0), new DropBucket(608, 2, 1)], whole.Buckets);
        Assert.Equal([new PcrReanchor(610, 1_000, 2_000)], whole.Reanchors);
    }

    [Fact(DisplayName = "the clock coming around between two sessions is followed through")]
    public void TheClockComingAroundBetweenTwoSessionsIsFollowedThrough()
    {
        long nearTheEnd = DropTimeline.PcrWrapsAt - (100 * DropTimeline.TicksPerSecond);
        DropTimeline before = DropTimeline.AnchoredAt(nearTheEnd);
        DropTimeline later = DropTimeline.Rehydrate(500 * DropTimeline.TicksPerSecond, [new DropBucket(0, 1, 0)], []);

        DropTimeline whole = before.Then(later, TenMinutesIn);

        Assert.Equal([new DropBucket(600, 1, 0)], whole.Buckets);
        Assert.Empty(whole.Reanchors);
    }

    [Fact(DisplayName = "after the clock was re-anchored, a later session is placed from where it was re-anchored")]
    public void AfterTheClockWasReanchoredALaterSessionIsPlacedFromThere()
    {
        DropTimeline before = DropTimeline.Rehydrate(Anchor, [], [new PcrReanchor(300, 27_900_000, 5_000_000)]);
        DropTimeline later = DropTimeline.Rehydrate(
            5_000_000 + (300 * DropTimeline.TicksPerSecond),
            [new DropBucket(4, 1, 0)],
            []);

        DropTimeline whole = before.Then(later, TenMinutesIn);

        Assert.Equal([new DropBucket(604, 1, 0)], whole.Buckets);
        Assert.Equal([new PcrReanchor(300, 27_900_000, 5_000_000)], whole.Reanchors);
    }

    [Fact(DisplayName = "a later session following another clock is placed by when it opened, and the jump is written down")]
    public void ALaterSessionFollowingAnotherClockIsPlacedByWhenItOpened()
    {
        DropTimeline before = DropTimeline.Rehydrate(Anchor, [new DropBucket(12, 3, 0)], []);
        DropTimeline later = DropTimeline.Rehydrate(4_000_000_000, [new DropBucket(5, 2, 0)], []);

        DropTimeline whole = before.Then(later, TenMinutesIn);

        Assert.Equal([new DropBucket(12, 3, 0), new DropBucket(605, 2, 0)], whole.Buckets);
        Assert.Equal(
            [new PcrReanchor(600, Anchor + (600 * DropTimeline.TicksPerSecond), 4_000_000_000)],
            whole.Reanchors);
    }

    [Theory(DisplayName = "the clock is believed while it agrees with when the session opened to within the tolerance, and not beyond it")]
    [InlineData(630, true)]
    [InlineData(570, true)]
    [InlineData(631, false)]
    [InlineData(569, false)]
    public void TheClockIsBelievedWhileItAgreesWithWhenTheSessionOpened(int secondsByTheClock, bool believed)
    {
        DropTimeline later = DropTimeline.Rehydrate(
            Anchor + (secondsByTheClock * DropTimeline.TicksPerSecond),
            [new DropBucket(0, 1, 0)],
            []);

        DropTimeline whole = DropTimeline.AnchoredAt(Anchor).Then(later, TenMinutesIn);

        Assert.Equal(believed ? secondsByTheClock : 600, Assert.Single(whole.Buckets).Second);
        Assert.Equal(believed, whole.Reanchors.Count is 0);
    }

    [Fact(DisplayName = "a session placed by when it opened is never placed before a second the timeline already names")]
    public void ASessionPlacedByWhenItOpenedIsNeverPlacedBeforeASecondAlreadyNamed()
    {
        DropTimeline before = DropTimeline.Rehydrate(Anchor, [new DropBucket(700, 3, 0)], []);
        DropTimeline later = DropTimeline.Rehydrate(4_000_000_000, [new DropBucket(0, 2, 0)], []);

        DropTimeline whole = before.Then(later, TenMinutesIn);

        Assert.Equal([new DropBucket(700, 5, 0)], whole.Buckets);
        Assert.Equal(700, Assert.Single(whole.Reanchors).Second);
    }

    [Fact(DisplayName = "losses two sessions place in the same second are one second's losses")]
    public void LossesTwoSessionsPlaceInTheSameSecondAreOneSecondsLosses()
    {
        DropTimeline before = DropTimeline.Rehydrate(Anchor, [new DropBucket(600, 3, 1)], []);
        DropTimeline later = DropTimeline.Rehydrate(
            Anchor + (600 * DropTimeline.TicksPerSecond),
            [new DropBucket(0, 2, 4)],
            []);

        Assert.Equal([new DropBucket(600, 5, 5)], before.Then(later, TenMinutesIn).Buckets);
    }

    [Fact(DisplayName = "the first clock any session heard is the recording's clock")]
    public void TheFirstClockAnySessionHeardIsTheRecordingsClock()
    {
        DropTimeline later = DropTimeline.Rehydrate(Anchor, [new DropBucket(5, 2, 0)], []);

        DropTimeline whole = DropTimeline.Unlocated.Then(later, TenMinutesIn);

        Assert.Equal(Anchor, whole.AnchorPcr);
        Assert.Equal([new DropBucket(5, 2, 0)], whole.Buckets);
    }

    [Fact(DisplayName = "a later session that heard no clock leaves the timeline as it was")]
    public void ALaterSessionThatHeardNoClockLeavesTheTimelineAsItWas()
    {
        DropTimeline before = DropTimeline.Rehydrate(Anchor, [new DropBucket(12, 3, 0)], []);

        DropTimeline whole = before.Then(DropTimeline.Unlocated, TenMinutesIn);

        Assert.Equal(Anchor, whole.AnchorPcr);
        Assert.Equal([new DropBucket(12, 3, 0)], whole.Buckets);
    }
}
