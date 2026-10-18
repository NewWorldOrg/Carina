using Carina.Api.Responder.Playback;
using Carina.Api.Responder.Segments;
using Carina.Domain.Encodings;
using Carina.Domain.Segments;

namespace Carina.Api.Tests.Unit;

public sealed class SegmentKindsAnsweredTests
{
    [Fact(DisplayName = "BR-CD-009: an opening and an ending detected are handed to playback as a break, skippable as they were, until the plan names them")]
    public void AnOpeningAndAnEndingArePlayedAsABreak()
    {
        PlaybackChapter[] chapters =
        [
            new(Span(0, 60, SegmentKind.Programme), false),
            new(Span(60, 150, SegmentKind.Opening), true),
            new(Span(150, 1700, SegmentKind.Programme), false),
            new(Span(1700, 1790, SegmentKind.Ending), true),
            new(Span(1790, 1800, SegmentKind.Programme), false),
        ];

        IReadOnlyList<PlaybackChapterResponder> handed = PlaybackChapterResponder.Of(chapters);

        Assert.Equal(
            [(0.0, 60.0, ChapterKind.Programme, false), (60.0, 150.0, ChapterKind.Break, true), (150.0, 1700.0, ChapterKind.Programme, false), (1700.0, 1790.0, ChapterKind.Break, true), (1790.0, 1800.0, ChapterKind.Programme, false)],
            handed.Select(chapter => (chapter.StartsAtSec, chapter.EndsAtSec, chapter.Kind, chapter.Skippable)));
    }

    [Fact(DisplayName = "BR-CD-008: the figures of the version in use answer the seconds of breaks found, and keep those of openings and endings back until the status names them")]
    public void TheStatusAnswersTheBreaksAlone()
    {
        SegmentMetrics metrics = new(
            0.9,
            0.8,
            1.0,
            2.0,
            [new KindMetrics(SegmentKind.Break, 0.95, 0.97), new KindMetrics(SegmentKind.Opening, 0.5, 0.6), new KindMetrics(SegmentKind.Ending, null, null)],
            null,
            0);

        SegmentKindMetricsResponder kind = Assert.Single(SegmentMetricsResponder.Of(metrics).Kinds);

        Assert.Equal(new SegmentKindMetricsResponder(SegmentKind.Break, 0.95, 0.97), kind);
    }

    private static SegmentSpan Span(double from, double to, SegmentKind kind) => new(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to), kind, 0.9);
}
