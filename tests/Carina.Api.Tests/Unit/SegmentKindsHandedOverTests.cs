using Carina.Api.Responder.Playback;
using Carina.Api.Responder.Segments;
using Carina.Domain.Encodings;
using Carina.Domain.Segments;

namespace Carina.Api.Tests.Unit;

public sealed class SegmentKindsHandedOverTests
{
    [Fact(DisplayName = "BR-CD-009: an opening and an ending detected are handed to playback by their own kinds, each skippable as it was")]
    public void AnOpeningAndAnEndingArePlayedByTheirOwnKinds()
    {
        PlaybackChapter[] chapters =
        [
            new(Span(0, 60, SegmentKind.Programme), false),
            new(Span(60, 150, SegmentKind.Opening), true),
            new(Span(150, 900, SegmentKind.Programme), false),
            new(Span(900, 960, SegmentKind.Break), true),
            new(Span(960, 1700, SegmentKind.Programme), false),
            new(Span(1700, 1790, SegmentKind.Ending), false),
            new(Span(1790, 1800, SegmentKind.Programme), false),
        ];

        IReadOnlyList<PlaybackChapterResponder> handed = PlaybackChapterResponder.Of(chapters);

        Assert.Equal(
            [
                (0.0, 60.0, ChapterKind.Programme, false),
                (60.0, 150.0, ChapterKind.Opening, true),
                (150.0, 900.0, ChapterKind.Programme, false),
                (900.0, 960.0, ChapterKind.Break, true),
                (960.0, 1700.0, ChapterKind.Programme, false),
                (1700.0, 1790.0, ChapterKind.Ending, false),
                (1790.0, 1800.0, ChapterKind.Programme, false),
            ],
            handed.Select(chapter => (chapter.StartsAtSec, chapter.EndsAtSec, chapter.Kind, chapter.Skippable)));
    }

    [Fact(DisplayName = "BR-CD-008: the figures of the version in use answer the seconds of breaks, openings and endings found, each kind in a row of its own")]
    public void TheStatusAnswersEveryKindMeasured()
    {
        SegmentMetrics metrics = new(
            0.9,
            0.8,
            [new KindMetrics(SegmentKind.Break, 0.95), new KindMetrics(SegmentKind.Opening, 0.5), new KindMetrics(SegmentKind.Ending, null)],
            null,
            0);

        Assert.Equal(
            [
                new SegmentKindMetricsResponder(SegmentKind.Break, 0.95),
                new SegmentKindMetricsResponder(SegmentKind.Opening, 0.5),
                new SegmentKindMetricsResponder(SegmentKind.Ending, null),
            ],
            SegmentMetricsResponder.Of(metrics).Kinds);
    }

    [Fact(DisplayName = "BR-CD-008: a version evaluated before openings and endings were measured answers the row for breaks alone")]
    public void AVersionEvaluatedBeforeOpeningsAndEndingsWereMeasuredAnswersBreaksAlone()
    {
        SegmentMetrics metrics = new(0.9, 0.8, [new KindMetrics(SegmentKind.Break, 0.95)], null, 0);

        Assert.Equal([new SegmentKindMetricsResponder(SegmentKind.Break, 0.95)], SegmentMetricsResponder.Of(metrics).Kinds);
    }

    private static SegmentSpan Span(double from, double to, SegmentKind kind) => new(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to), kind, 0.9);
}
