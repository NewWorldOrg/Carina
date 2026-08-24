using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class DropTimelineTests
{
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
}
