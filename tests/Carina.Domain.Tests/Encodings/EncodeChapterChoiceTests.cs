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
        EncodeChapterChoice choice = EncodeChapterChoice.Of(true, 1, SegmentStanding.Of(null, marked));

        Assert.False(choice.AsksTheReader);
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
        EncodeChapterChoice choice = EncodeChapterChoice.Of(false, 1, SegmentStanding.Of(null, marked));

        Assert.Equal(marked.Segments.Skip(1), choice.Embedded(Artefact));
        Assert.Equal(3, choice.Chapters(Artefact).Count);
        Assert.Equal(TimeSpan.Zero, choice.Chapters(Artefact)[0].Starts);
    }

    [Fact(DisplayName = "BR-CD-010: a detection that found only the programme, or was thrown away, embeds nothing, whether learning is on or off")]
    public void ADetectionWithNoBreaksToKeepEmbedsNothing()
    {
        RecordingDetection plain = Detected([Span(0, 1800, SegmentKind.Programme)]);
        RecordingDetection discarded = Detected([Span(0, 600, SegmentKind.Programme), Span(600, 1800, SegmentKind.Break)]);

        foreach (bool learning in new[] { true, false })
        {
            foreach (RecordingDetection detection in new[] { plain, discarded })
            {
                EncodeChapterChoice choice = EncodeChapterChoice.Of(learning, 1, SegmentStanding.Of(null, detection));

                Assert.False(choice.AsksTheReader);
                Assert.Empty(choice.Embedded(Artefact));
                Assert.Empty(choice.Chapters(Artefact));
            }
        }
    }

    [Fact(DisplayName = "BR-CD-013: with learning off and no detection the reader of the source is asked; with learning on it is not, and nothing is embedded")]
    public void WithoutADetectionTheReaderIsAskedOnlyWhileLearningIsOff()
    {
        EncodeChapterChoice off = EncodeChapterChoice.Of(false, 1, SegmentStanding.None);
        EncodeChapterChoice on = EncodeChapterChoice.Of(true, 1, SegmentStanding.None);

        Assert.True(off.AsksTheReader);
        Assert.Throws<InvalidOperationException>(() => off.Reading(Noon));
        Assert.False(on.AsksTheReader);
        Assert.Empty(on.Chapters(Artefact));
    }

    [Fact(DisplayName = "BR-CD-010: the ledger keeps that the chapters came from the segments, with the version that detected them and the share breaks take")]
    public void TheReadingSaysTheChaptersCameFromTheSegments()
    {
        RecordingDetection marked = Detected([Span(0, 300, SegmentKind.Programme), Span(300, 360, SegmentKind.Break), Span(360, 1800, SegmentKind.Programme)], version: 3);
        RecordingDetection discarded = Detected([Span(0, 600, SegmentKind.Programme), Span(600, 1800, SegmentKind.Break)], version: 3);

        ChapterReading embedded = EncodeChapterChoice.Of(true, 3, SegmentStanding.Of(null, marked)).Reading(Noon);
        ChapterReading thrownAway = EncodeChapterChoice.Of(true, 3, SegmentStanding.Of(null, discarded)).Reading(Noon);
        ChapterReading none = EncodeChapterChoice.Of(true, 3, SegmentStanding.None).Reading(Noon);

        Assert.Equal((ChapterDetectorName.Segments, ChapterVerdict.Marked, 3, Noon), (embedded.Detector, embedded.Verdict, embedded.ModelVersion, embedded.DecidedAt));
        Assert.Equal(marked.CmShare, embedded.BreakShare);
        Assert.Equal((ChapterVerdict.Discarded, 3), (thrownAway.Verdict, thrownAway.ModelVersion));
        Assert.Equal(discarded.CmShare, thrownAway.BreakShare);
        Assert.Equal((ChapterDetectorName.Segments, ChapterVerdict.NotAsked, (int?)null, 0d), (none.Detector, none.Verdict, none.ModelVersion, none.BreakShare));
    }

    [Fact(DisplayName = "BR-CD-010: an answer that stands is embedded in place of the detection, and the ledger keeps when it was given rather than a version")]
    public void AnAnswerThatStandsIsEmbedded()
    {
        RecordingDetection detected = Detected([Span(0, 300, SegmentKind.Programme), Span(300, 360, SegmentKind.Break), Span(360, 1800, SegmentKind.Programme)], version: 3);
        RecordingAnswer answer = Answered(detected.RecordingId, Noon.AddHours(2), (0, 900, SegmentKind.Programme), (900, 990, SegmentKind.Break), (990, 1800, SegmentKind.Programme));

        EncodeChapterChoice choice = EncodeChapterChoice.Of(true, 3, SegmentStanding.Of(answer, detected));
        ChapterReading reading = choice.Reading(Noon.AddHours(3));

        Assert.Equal(answer.Segments, choice.Embedded(Artefact));
        Assert.Equal(TimeSpan.FromSeconds(899.5), choice.Chapters(Artefact)[1].Starts);
        Assert.Equal(
            (ChapterDetectorName.Segments, ChapterVerdict.Marked, (int?)null, (DateTime?)Noon.AddHours(2)),
            (reading.Detector, reading.Verdict, reading.ModelVersion, reading.AnsweredAt));
        Assert.Equal(90d / 1800, reading.BreakShare, 12);
    }

    [Fact(DisplayName = "BR-CD-013: while learning is off an answer is embedded and the reader of the source is not asked; an answer of the programme alone embeds nothing")]
    public void WhileLearningIsOffAnAnswerIsEmbedded()
    {
        RecordingId recording = RecordingId.New();
        RecordingAnswer marked = Answered(recording, Noon, (0, 900, SegmentKind.Programme), (900, 990, SegmentKind.Break), (990, 1800, SegmentKind.Programme));
        RecordingAnswer plain = Answered(recording, Noon, (0, 1800, SegmentKind.Programme));

        EncodeChapterChoice withBreaks = EncodeChapterChoice.Of(false, 1, SegmentStanding.Of(marked, null));
        EncodeChapterChoice programmeAlone = EncodeChapterChoice.Of(false, 1, SegmentStanding.Of(plain, null));

        Assert.False(withBreaks.AsksTheReader);
        Assert.Equal(3, withBreaks.Embedded(Artefact).Count);
        Assert.False(programmeAlone.AsksTheReader);
        Assert.Empty(programmeAlone.Embedded(Artefact));
        Assert.Empty(programmeAlone.Chapters(Artefact));
        Assert.Equal((ChapterVerdict.NothingFound, (DateTime?)Noon), (programmeAlone.Reading(Noon).Verdict, programmeAlone.Reading(Noon).AnsweredAt));
    }

    [Fact]
    public void ARecordingStandsWithTheDetectionByTheVersionInUse()
        => Assert.Throws<ArgumentException>(() => EncodeChapterChoice.Of(true, 2, SegmentStanding.Of(null, Detected([Span(0, 1800, SegmentKind.Programme)]))));

    [Fact(DisplayName = "an artefact's chapters are the programme or a break, an opening and an ending embedded as a break")]
    public void AnArtefactsChaptersAreTheProgrammeOrABreak()
    {
        Assert.Equal(ChapterKind.Programme, EncodeChapterChoice.KindOf(SegmentKind.Programme));
        Assert.Equal(ChapterKind.Break, EncodeChapterChoice.KindOf(SegmentKind.Break));
        Assert.Equal(ChapterKind.Break, EncodeChapterChoice.KindOf(SegmentKind.Opening));
        Assert.Equal(ChapterKind.Break, EncodeChapterChoice.KindOf(SegmentKind.Ending));
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeChapterChoice.KindOf((SegmentKind)9));
    }

    private static RecordingDetection Detected(SegmentSpan[] segments, int version = 1)
        => RecordingDetection.Of(RecordingId.New(), version, SegmentDetection.Judged(segments, HalfAnHour, []), 2, Noon, Noon);

    private static SegmentSpan Span(double starts, double ends, SegmentKind kind)
        => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends), kind, 0.9);

    private static RecordingAnswer Answered(RecordingId recording, DateTime at, params (double Starts, double Ends, SegmentKind Kind)[] stretches)
        => RecordingAnswer.Given(
            recording,
            AnsweredStretches.Read([.. stretches.Select(stretch => new SentStretch(stretch.Starts, stretch.Ends, stretch.Kind))], HalfAnHour).Stretches!,
            true,
            at);
}
