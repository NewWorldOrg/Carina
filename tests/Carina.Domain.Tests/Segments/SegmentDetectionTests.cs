using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentDetectionTests
{
    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    [Fact(DisplayName = "a sequence with breaks is marked, with the share of the length the breaks take")]
    public void ASequenceWithBreaksIsMarked()
    {
        SegmentDetection detection = SegmentDetection.Judged(Tiled(300, 60, 1440), HalfAnHour, []);

        Assert.Equal(SegmentDetectionState.Marked, detection.State);
        Assert.Equal(60d / 1800, detection.CmShare, 12);
        Assert.Equal(3, detection.Segments.Count);
    }

    [Fact(DisplayName = "a sequence of programme alone is nothing found, with no share taken by breaks")]
    public void ProgrammeAloneIsNothingFound()
    {
        SegmentDetection detection = SegmentDetection.Judged(Tiled(1800), HalfAnHour, []);

        Assert.Equal(SegmentDetectionState.NothingFound, detection.State);
        Assert.Equal(0, detection.CmShare);
        Assert.Equal(SegmentKind.Programme, Assert.Single(detection.Segments).Kind);
    }

    [Fact(DisplayName = "breaks taking more than half the length are discarded, keeping the sequence and the share")]
    public void MoreThanHalfIsDiscarded()
    {
        SegmentDetection detection = SegmentDetection.Judged(Tiled(899.99, 900.01), HalfAnHour, []);

        Assert.Equal(SegmentDetectionState.Discarded, detection.State);
        Assert.Equal(900.01 / 1800, detection.CmShare, 12);
        Assert.Equal(2, detection.Segments.Count);
    }

    [Fact(DisplayName = "breaks taking exactly half the length are not discarded")]
    public void ExactlyHalfIsKept()
    {
        SegmentDetection detection = SegmentDetection.Judged(Tiled(900, 900), HalfAnHour, []);

        Assert.Equal(SegmentDetectionState.Marked, detection.State);
        Assert.Equal(0.5, detection.CmShare);
    }

    [Fact(DisplayName = "more than 80 stretches an hour are discarded, and 80 an hour are kept")]
    public void MoreThanEightyStretchesAnHourAreDiscarded()
    {
        SegmentDetection forty = SegmentDetection.Judged(Tiled([.. Enumerable.Repeat(45.0, 40)]), HalfAnHour, []);
        SegmentDetection fortyOne = SegmentDetection.Judged(Tiled([.. Enumerable.Repeat(45.0, 39), 22.5, 22.5]), HalfAnHour, []);
        SegmentDetection anHour = SegmentDetection.Judged(Tiled([.. Enumerable.Repeat(45.0, 80)]), TimeSpan.FromHours(1), []);

        Assert.Equal(SegmentDetectionState.Marked, forty.State);
        Assert.Equal(SegmentDetectionState.Discarded, fortyOne.State);
        Assert.Equal(SegmentDetectionState.Marked, anHour.State);
        Assert.Equal(41, fortyOne.Segments.Count);
    }

    [Fact(DisplayName = "the snaps are kept to a hundredth of a second")]
    public void TheSnapsAreKeptToAHundredth()
        => Assert.Equal(
            [TimeSpan.FromSeconds(300.01), TimeSpan.FromSeconds(912.28)],
            SegmentDetection.Judged(Tiled(1800), HalfAnHour, [TimeSpan.FromSeconds(300.006), TimeSpan.FromSeconds(912.2812)]).Snaps);

    [Fact(DisplayName = "a sequence that leaves a gap, overlaps, misses either end or puts two stretches of one kind side by side is refused")]
    public void ABrokenSequenceIsRefused()
    {
        Assert.Throws<ArgumentException>(() => SegmentDetection.Judged([], HalfAnHour, []));
        Assert.Throws<ArgumentException>(() => SegmentDetection.Judged(
            [Span(0, 300, SegmentKind.Programme), Span(310, 1800, SegmentKind.Break)],
            HalfAnHour,
            []));
        Assert.Throws<ArgumentException>(() => SegmentDetection.Judged([Span(1, 1800, SegmentKind.Programme)], HalfAnHour, []));
        Assert.Throws<ArgumentException>(() => SegmentDetection.Judged([Span(0, 1700, SegmentKind.Programme)], HalfAnHour, []));
        Assert.Throws<ArgumentException>(() => SegmentDetection.Judged(
            [Span(0, 300, SegmentKind.Programme), Span(300, 1800, SegmentKind.Programme)],
            HalfAnHour,
            []));
    }

    private static SegmentSpan[] Tiled(params double[] lengths)
    {
        List<SegmentSpan> spans = [];
        TimeSpan reached = TimeSpan.Zero;

        foreach (double length in lengths)
        {
            TimeSpan ends = reached + TimeSpan.FromSeconds(length);

            spans.Add(new SegmentSpan(reached, ends, spans.Count % 2 is 0 ? SegmentKind.Programme : SegmentKind.Break, 0.9));
            reached = ends;
        }

        return [.. spans];
    }

    private static SegmentSpan Span(double starts, double ends, SegmentKind kind)
        => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends), kind, 0.9);
}
