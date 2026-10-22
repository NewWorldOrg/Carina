using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeChapterChoiceTests
{
    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan HeadSkip = TimeSpan.FromSeconds(0.5);

    private static readonly PlaybackFileClock Artefact = PlaybackFileClock.Of(new EncodeTimeline(TimeSpan.FromSeconds(3.2), HeadSkip, HalfAnHour, null));

    [Fact(DisplayName = "BR-CD-010: a detection that marked breaks is embedded in the recording's own time, and kept by the ledger on the artefact's clock")]
    public void ADetectionThatMarkedBreaksIsEmbedded()
    {
        RecordingDetection marked = Detected([Span(0, 300, SegmentKind.Programme), Span(300, 360, SegmentKind.Break), Span(360, 1800, SegmentKind.Programme)]);
        EncodeChapterChoice choice = EncodeChapterChoice.Of(1, SegmentStanding.Of(marked));

        Assert.Equal(marked.Segments, choice.Embedded(Artefact));
        Assert.Equal(
            [
                new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(299.5), ChapterKind.Programme),
                new ChapterSegment(TimeSpan.FromSeconds(299.5), TimeSpan.FromSeconds(359.5), ChapterKind.Break),
                new ChapterSegment(TimeSpan.FromSeconds(359.5), TimeSpan.FromSeconds(1799.5), ChapterKind.Programme),
            ],
            choice.Chapters(Artefact));
    }

    [Fact(DisplayName = "BR-CD-010: a stretch the artefact does not play is not embedded")]
    public void AStretchTheArtefactDoesNotPlayIsNotEmbedded()
    {
        RecordingDetection marked = Detected([Span(0, 0.4, SegmentKind.Break), Span(0.4, 300, SegmentKind.Programme), Span(300, 360, SegmentKind.Break), Span(360, 1800, SegmentKind.Programme)]);
        EncodeChapterChoice choice = EncodeChapterChoice.Of(1, SegmentStanding.Of(marked));

        Assert.Equal(marked.Segments.Skip(1), choice.Embedded(Artefact));
        Assert.Equal(3, choice.Chapters(Artefact).Count);
        Assert.Equal(TimeSpan.Zero, choice.Chapters(Artefact)[0].Starts);
    }

    [Fact(DisplayName = "BR-CD-010: a detection that found only the programme, or was thrown away, embeds nothing")]
    public void ADetectionWithNoBreaksToKeepEmbedsNothing()
    {
        RecordingDetection plain = Detected([Span(0, 1800, SegmentKind.Programme)]);
        RecordingDetection discarded = Detected([Span(0, 600, SegmentKind.Programme), Span(600, 1800, SegmentKind.Break)]);

        foreach (RecordingDetection detection in new[] { plain, discarded })
        {
            EncodeChapterChoice choice = EncodeChapterChoice.Of(1, SegmentStanding.Of(detection));

            Assert.Empty(choice.Embedded(Artefact));
            Assert.Empty(choice.Chapters(Artefact));
        }
    }

    [Fact(DisplayName = "BR-CD-010: with no detection by the version in use nothing is embedded, and the ledger keeps that nobody was asked")]
    public void WithoutADetectionNothingIsEmbedded()
    {
        EncodeChapterChoice choice = EncodeChapterChoice.Of(1, SegmentStanding.None);
        ChapterReading reading = choice.Reading(Noon);

        Assert.Empty(choice.Embedded(Artefact));
        Assert.Empty(choice.Chapters(Artefact));
        Assert.Equal((ChapterDetectorName.Segments, ChapterVerdict.NotAsked, (int?)null), (reading.Detector, reading.Verdict, reading.ModelVersion));
    }

    [Fact(DisplayName = "BR-CD-010: the ledger keeps that the chapters came from the segments, with the version that detected them and the share breaks take")]
    public void TheReadingSaysTheChaptersCameFromTheSegments()
    {
        RecordingDetection marked = Detected([Span(0, 300, SegmentKind.Programme), Span(300, 360, SegmentKind.Break), Span(360, 1800, SegmentKind.Programme)], version: 3);
        RecordingDetection discarded = Detected([Span(0, 600, SegmentKind.Programme), Span(600, 1800, SegmentKind.Break)], version: 3);

        ChapterReading embedded = EncodeChapterChoice.Of(3, SegmentStanding.Of(marked)).Reading(Noon);
        ChapterReading thrownAway = EncodeChapterChoice.Of(3, SegmentStanding.Of(discarded)).Reading(Noon);
        ChapterReading none = EncodeChapterChoice.Of(3, SegmentStanding.None).Reading(Noon);

        Assert.Equal((ChapterDetectorName.Segments, ChapterVerdict.Marked, 3, Noon), (embedded.Detector, embedded.Verdict, embedded.ModelVersion, embedded.DecidedAt));
        Assert.Equal(marked.CmShare, embedded.BreakShare);
        Assert.Equal((ChapterVerdict.Discarded, 3), (thrownAway.Verdict, thrownAway.ModelVersion));
        Assert.Equal(discarded.CmShare, thrownAway.BreakShare);
        Assert.Equal((ChapterDetectorName.Segments, ChapterVerdict.NotAsked, (int?)null, 0d), (none.Detector, none.Verdict, none.ModelVersion, none.BreakShare));
    }

    [Fact]
    public void ARecordingStandsWithTheDetectionByTheVersionInUse()
        => Assert.Throws<ArgumentException>(() => EncodeChapterChoice.Of(2, SegmentStanding.Of(Detected([Span(0, 1800, SegmentKind.Programme)]))));

    [Fact(DisplayName = "BR-CD-010: a stretch is embedded as a chapter of its own kind: the programme, a break, an opening or an ending")]
    public void AStretchIsEmbeddedAsAChapterOfItsOwnKind()
    {
        Assert.Equal(ChapterKind.Programme, EncodeChapterChoice.KindOf(SegmentKind.Programme));
        Assert.Equal(ChapterKind.Break, EncodeChapterChoice.KindOf(SegmentKind.Break));
        Assert.Equal(ChapterKind.Opening, EncodeChapterChoice.KindOf(SegmentKind.Opening));
        Assert.Equal(ChapterKind.Ending, EncodeChapterChoice.KindOf(SegmentKind.Ending));
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeChapterChoice.KindOf((SegmentKind)9));
    }

    [Fact(DisplayName = "BR-CD-010: a detection that found an opening and an ending is kept by the ledger with chapters of those kinds, on the artefact's clock")]
    public void ADetectionThatFoundAnOpeningAndAnEndingKeepsThoseKinds()
    {
        RecordingDetection marked = Detected(
        [
            Span(0, 60, SegmentKind.Programme),
            Span(60, 150, SegmentKind.Opening),
            Span(150, 900, SegmentKind.Programme),
            Span(900, 960, SegmentKind.Break),
            Span(960, 1710, SegmentKind.Programme),
            Span(1710, 1800, SegmentKind.Ending),
        ]);
        EncodeChapterChoice choice = EncodeChapterChoice.Of(1, SegmentStanding.Of(marked));

        Assert.Equal(
            [ChapterKind.Programme, ChapterKind.Opening, ChapterKind.Programme, ChapterKind.Break, ChapterKind.Programme, ChapterKind.Ending],
            choice.Chapters(Artefact).Select(chapter => chapter.Kind));
        Assert.Equal(TimeSpan.FromSeconds(59.5), choice.Chapters(Artefact)[1].Starts);
    }

    private static RecordingDetection Detected(SegmentSpan[] segments, int version = 1)
        => RecordingDetection.Of(RecordingId.New(), version, SegmentDetection.Judged(segments, HalfAnHour, []), 2, Noon, Noon);

    private static SegmentSpan Span(double starts, double ends, SegmentKind kind)
        => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends), kind, 0.9);
}
