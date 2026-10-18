using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentStandingTests
{
    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    [Fact(DisplayName = "BR-CS-002: a recording the version in use has not detected stands with nothing to play")]
    public void NotDetectedStandsWithNothing()
    {
        SegmentStanding standing = SegmentStanding.Of(null, null);

        Assert.Equal((SegmentSource.None, SegmentStandingState.None), (standing.Source, standing.State));
        Assert.Null(standing.Detection);
        Assert.Empty(standing.Played);
    }

    [Fact(DisplayName = "BR-CS-002: a detection that marked breaks, or found only the programme, is played as it was detected")]
    public void ADetectionIsPlayedAsDetected()
    {
        RecordingDetection marked = Detected([Span(0, 300, SegmentKind.Programme), Span(300, 360, SegmentKind.Break), Span(360, 1800, SegmentKind.Programme)]);
        RecordingDetection plain = Detected([Span(0, 1800, SegmentKind.Programme)]);

        Assert.Equal((SegmentSource.Detected, SegmentStandingState.Marked), (SegmentStanding.Of(null, marked).Source, SegmentStanding.Of(null, marked).State));
        Assert.Equal(marked.Segments, SegmentStanding.Of(null, marked).Played);
        Assert.Equal(SegmentStandingState.NothingFound, SegmentStanding.Of(null, plain).State);
        Assert.Equal(plain.Segments, SegmentStanding.Of(null, plain).Played);
    }

    [Fact(DisplayName = "BR-CS-002: a sequence thrown away is not played, though it stands as detected")]
    public void ASequenceThrownAwayIsNotPlayed()
    {
        RecordingDetection discarded = Detected([Span(0, 600, SegmentKind.Programme), Span(600, 1800, SegmentKind.Break)]);

        SegmentStanding standing = SegmentStanding.Of(null, discarded);

        Assert.Equal((SegmentSource.Detected, SegmentStandingState.Discarded), (standing.Source, standing.State));
        Assert.Empty(standing.Played);
        Assert.Same(discarded, standing.Detection);
    }

    [Fact(DisplayName = "BR-CS-002: an answer that stands is played in place of the detection, which is kept beside it, and names no version")]
    public void AnAnswerThatStandsIsPlayedInPlaceOfTheDetection()
    {
        RecordingId recording = RecordingId.New();
        RecordingDetection detection = Detected([Span(0, 300, SegmentKind.Programme), Span(300, 360, SegmentKind.Break), Span(360, 1800, SegmentKind.Programme)], recording);
        RecordingAnswer answer = Answered(recording, (0, 900, SegmentKind.Programme), (900, 990, SegmentKind.Break), (990, 1800, SegmentKind.Programme));

        SegmentStanding standing = SegmentStanding.Of(answer, detection);

        Assert.Equal((SegmentSource.Answered, SegmentStandingState.Answered), (standing.Source, standing.State));
        Assert.Equal(answer.Segments, standing.Played);
        Assert.Same(answer, standing.Answer);
        Assert.Same(detection, standing.Detection);
        Assert.Null(standing.ModelVersion);
        Assert.True(standing.Marks);
        Assert.Equal(90d / 1800, standing.BreakShare, 12);
    }

    [Fact(DisplayName = "BR-CS-002: an answer stands over a detection thrown away, and over no detection at all")]
    public void AnAnswerStandsOverADetectionThrownAwayOrNone()
    {
        RecordingId recording = RecordingId.New();
        RecordingDetection discarded = Detected([Span(0, 600, SegmentKind.Programme), Span(600, 1800, SegmentKind.Break)], recording);
        RecordingAnswer answer = Answered(recording, (0, 1800, SegmentKind.Programme));

        SegmentStanding overDiscarded = SegmentStanding.Of(answer, discarded);
        SegmentStanding overNothing = SegmentStanding.Of(answer, null);

        Assert.Equal(answer.Segments, overDiscarded.Played);
        Assert.Equal(answer.Segments, overNothing.Played);
        Assert.Equal(SegmentSource.Answered, overNothing.Source);
        Assert.False(overNothing.Marks);
        Assert.Equal(0, overNothing.BreakShare);
    }

    [Fact(DisplayName = "BR-CS-002: a withdrawn answer gives way to the detection again, or to nothing")]
    public void AWithdrawnAnswerGivesWayToTheDetection()
    {
        RecordingId recording = RecordingId.New();
        RecordingDetection detection = Detected([Span(0, 300, SegmentKind.Programme), Span(300, 360, SegmentKind.Break), Span(360, 1800, SegmentKind.Programme)], recording, version: 4);
        RecordingAnswer given = Answered(recording, (0, 1800, SegmentKind.Programme));
        RecordingAnswer withdrawn = RecordingAnswer.Rehydrate(recording, given.Segments, given.Length, false, null, Noon, 2, Noon.AddMinutes(5));

        SegmentStanding detected = SegmentStanding.Of(withdrawn, detection);

        Assert.Equal((SegmentSource.Detected, SegmentStandingState.Marked, (int?)4), (detected.Source, detected.State, detected.ModelVersion));
        Assert.Null(detected.Answer);
        Assert.Equal(detection.Segments, detected.Played);
        Assert.Same(SegmentStanding.None, SegmentStanding.Of(withdrawn, null));
    }

    [Fact]
    public void ARecordingStandsWithItsOwnAnswerAndDetection()
        => Assert.Throws<ArgumentException>(() => SegmentStanding.Of(
            Answered(RecordingId.New(), (0, 1800, SegmentKind.Programme)),
            Detected([Span(0, 1800, SegmentKind.Programme)])));

    private static RecordingDetection Detected(SegmentSpan[] segments, RecordingId? recording = null, int version = 1)
        => RecordingDetection.Of(recording ?? RecordingId.New(), version, SegmentDetection.Judged(segments, HalfAnHour, []), 2, Noon, Noon);

    private static RecordingAnswer Answered(RecordingId recording, params (double Starts, double Ends, SegmentKind Kind)[] stretches)
        => RecordingAnswer.Given(
            recording,
            AnsweredStretches.Read([.. stretches.Select(stretch => new SentStretch(stretch.Starts, stretch.Ends, stretch.Kind))], HalfAnHour).Stretches!,
            false,
            Noon);

    private static SegmentSpan Span(double starts, double ends, SegmentKind kind)
        => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends), kind, 0.9);
}
