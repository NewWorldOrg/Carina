using Carina.Domain.Encodings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class PlaybackFileClockTests
{
    private static readonly TimeSpan HeadSkip = TimeSpan.FromSeconds(0.5);

    [Fact(DisplayName = "BR-CD-001: the recording's own file plays a stretch at the recording's own time")]
    public void TheRecordingsOwnFilePlaysTheRecordingsOwnTime()
    {
        SegmentSpan stretch = Span(912.3, 1002.3);

        Assert.Equal(stretch, PlaybackFileClock.AsItIs.Placed(stretch));
    }

    [Fact(DisplayName = "BR-CD-001: an artefact plays a stretch at the recording's own time less the head the job that made it skipped")]
    public void AnArtefactPlaysTheRecordingsTimeLessTheHeadSkipped()
    {
        PlaybackFileClock artefact = PlaybackFileClock.Of(new EncodeTimeline(TimeSpan.FromSeconds(3.2), HeadSkip, TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(1799.5)));

        Assert.Equal(Span(911.8, 1001.8), artefact.Placed(Span(912.3, 1002.3)));
    }

    [Fact(DisplayName = "BR-CD-001: where the source's clock began does not move a stretch, whether before zero, at zero or just short of the clock coming around")]
    public void WhereTheSourcesClockBeganDoesNotMoveAStretch()
    {
        TimeSpan[] beginnings = [TimeSpan.FromSeconds(-2.5), TimeSpan.Zero, EncodeTimeline.OneTurnOfTheClock - TimeSpan.FromSeconds(0.7)];

        Assert.All(
            beginnings,
            begins => Assert.Equal(
                Span(599.5, 659.5),
                PlaybackFileClock.Of(new EncodeTimeline(begins, HeadSkip, TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(1799.5))).Placed(Span(600, 660))));
    }

    [Fact(DisplayName = "BR-CD-001: a stretch reaching back before the artefact's zero starts at zero, and one wholly before it is not played")]
    public void BeforeZeroIsBroughtToZeroOrLeftOut()
    {
        PlaybackFileClock artefact = PlaybackFileClock.Of(new EncodeTimeline(TimeSpan.Zero, HeadSkip, TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(1799.5)));

        Assert.Equal(Span(0, 9.5), artefact.Placed(Span(0, 10)));
        Assert.Equal(Span(0, 0.1), artefact.Placed(Span(0.2, 0.6)));
        Assert.Null(artefact.Placed(Span(0, 0.5)));
        Assert.Null(artefact.Placed(Span(0, 0.3)));
    }

    [Fact(DisplayName = "BR-CD-001: a stretch reaching past the artefact's length ends with it, and one wholly past it is not played")]
    public void PastTheLengthIsCutOrLeftOut()
    {
        PlaybackFileClock artefact = PlaybackFileClock.Of(new EncodeTimeline(TimeSpan.Zero, HeadSkip, TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(1799.4)));

        Assert.Equal(Span(1794.5, 1799.4), artefact.Placed(Span(1795, 1800)));
        Assert.Null(artefact.Placed(Span(1799.9, 1800)));
    }

    [Fact(DisplayName = "an artefact whose length was not measured cuts nothing at its end")]
    public void AnUnmeasuredArtefactCutsNothing()
    {
        PlaybackFileClock artefact = PlaybackFileClock.Of(new EncodeTimeline(TimeSpan.Zero, HeadSkip, null, null));

        Assert.Equal(Span(1794.5, 1799.5), artefact.Placed(Span(1795, 1800)));
    }

    [Fact(DisplayName = "a stretch keeps its kind and its probability where it is played")]
    public void AStretchKeepsItsKindAndProbability()
    {
        PlaybackFileClock artefact = PlaybackFileClock.Of(new EncodeTimeline(TimeSpan.Zero, HeadSkip, null, null));

        SegmentSpan placed = artefact.Placed(new SegmentSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40), SegmentKind.Break, 0.81))!;

        Assert.Equal((SegmentKind.Break, (double?)0.81), (placed.Kind, placed.Probability));
    }

    private static SegmentSpan Span(double starts, double ends)
        => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends), SegmentKind.Programme, 0.9);
}
