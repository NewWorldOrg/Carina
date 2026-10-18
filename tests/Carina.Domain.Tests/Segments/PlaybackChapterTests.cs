using Carina.Domain.Encodings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class PlaybackChapterTests
{
    private const double Threshold = SegmentSettingsStanding.DefaultSkipThreshold;

    [Fact(DisplayName = "BR-CD-009: a break is offered to be skipped when it is at least as likely as the threshold and something follows it")]
    public void ALikelyBreakWithSomethingAfterItCanBeSkipped()
    {
        IReadOnlyList<PlaybackChapter> chapters = PlaybackChapter.Of(
            [Span(0, 300, SegmentKind.Programme, 0.98), Span(300, 360, SegmentKind.Break, 0.70), Span(360, 1800, SegmentKind.Programme, 0.99)],
            PlaybackFileClock.AsItIs,
            Threshold);

        Assert.Equal([false, true, false], chapters.Select(chapter => chapter.Skippable));
    }

    [Fact(DisplayName = "BR-CD-009: a break less likely than the threshold is marked but not offered to be skipped")]
    public void AnUnlikelyBreakIsNotOfferedToBeSkipped()
    {
        IReadOnlyList<PlaybackChapter> chapters = PlaybackChapter.Of(
            [Span(0, 300, SegmentKind.Programme, 0.98), Span(300, 360, SegmentKind.Break, 0.69), Span(360, 1800, SegmentKind.Programme, 0.99)],
            PlaybackFileClock.AsItIs,
            Threshold);

        Assert.Equal(SegmentKind.Break, chapters[1].Stretch.Kind);
        Assert.False(chapters[1].Skippable);
    }

    [Fact(DisplayName = "BR-CD-009: a break the recording ends on is not offered to be skipped, there being nothing to skip to")]
    public void ABreakAtTheEndIsNotOfferedToBeSkipped()
    {
        IReadOnlyList<PlaybackChapter> chapters = PlaybackChapter.Of(
            [Span(0, 1740, SegmentKind.Programme, 0.98), Span(1740, 1800, SegmentKind.Break, 0.99)],
            PlaybackFileClock.AsItIs,
            Threshold);

        Assert.False(chapters[^1].Skippable);
    }

    [Fact(DisplayName = "BR-CD-009: a stretch given by an answer is offered to be skipped whatever its probability, when something follows it")]
    public void AnAnsweredBreakIsOfferedToBeSkipped()
    {
        IReadOnlyList<PlaybackChapter> chapters = PlaybackChapter.Of(
            [Span(0, 300, SegmentKind.Programme, null), Span(300, 360, SegmentKind.Break, null), Span(360, 1800, SegmentKind.Programme, null)],
            PlaybackFileClock.AsItIs,
            0.95);

        Assert.Equal([false, true, false], chapters.Select(chapter => chapter.Skippable));
    }

    [Fact(DisplayName = "BR-CD-001: chapters are placed on the artefact's clock, and a stretch the artefact does not play is left out")]
    public void ChaptersArePlacedOnTheArtefactsClock()
    {
        PlaybackFileClock artefact = PlaybackFileClock.Of(new EncodeTimeline(TimeSpan.Zero, TimeSpan.FromSeconds(0.5), TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(1799.5)));

        IReadOnlyList<PlaybackChapter> chapters = PlaybackChapter.Of(
            [Span(0, 0.4, SegmentKind.Break, 0.9), Span(0.4, 300, SegmentKind.Programme, 0.9), Span(300, 360, SegmentKind.Break, 0.9), Span(360, 1800, SegmentKind.Programme, 0.9)],
            artefact,
            Threshold);

        Assert.Equal(
            [(0d, 299.5d), (299.5d, 359.5d), (359.5d, 1799.5d)],
            chapters.Select(chapter => (chapter.Stretch.Starts.TotalSeconds, chapter.Stretch.Ends.TotalSeconds)));
    }

    [Fact(DisplayName = "a threshold is a probability")]
    public void AThresholdIsAProbability()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackChapter.Of([], PlaybackFileClock.AsItIs, 1.2));
    }

    private static SegmentSpan Span(double starts, double ends, SegmentKind kind, double? probability)
        => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends), kind, probability);
}
