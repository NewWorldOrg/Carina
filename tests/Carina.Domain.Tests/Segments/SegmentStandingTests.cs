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
        SegmentStanding standing = SegmentStanding.Of(null);

        Assert.Equal((SegmentSource.None, SegmentStandingState.None), (standing.Source, standing.State));
        Assert.Null(standing.Detection);
        Assert.Empty(standing.Played);
    }

    [Fact(DisplayName = "BR-CS-002: a detection that marked breaks, or found only the programme, is played as it was detected")]
    public void ADetectionIsPlayedAsDetected()
    {
        RecordingDetection marked = Detected([Span(0, 300, SegmentKind.Programme), Span(300, 360, SegmentKind.Break), Span(360, 1800, SegmentKind.Programme)]);
        RecordingDetection plain = Detected([Span(0, 1800, SegmentKind.Programme)]);

        Assert.Equal((SegmentSource.Detected, SegmentStandingState.Marked), (SegmentStanding.Of(marked).Source, SegmentStanding.Of(marked).State));
        Assert.Equal(marked.Segments, SegmentStanding.Of(marked).Played);
        Assert.Equal(SegmentStandingState.NothingFound, SegmentStanding.Of(plain).State);
        Assert.Equal(plain.Segments, SegmentStanding.Of(plain).Played);
    }

    [Fact(DisplayName = "BR-CS-002: a sequence thrown away is not played, though it stands as detected")]
    public void ASequenceThrownAwayIsNotPlayed()
    {
        RecordingDetection discarded = Detected([Span(0, 600, SegmentKind.Programme), Span(600, 1800, SegmentKind.Break)]);

        SegmentStanding standing = SegmentStanding.Of(discarded);

        Assert.Equal((SegmentSource.Detected, SegmentStandingState.Discarded), (standing.Source, standing.State));
        Assert.Empty(standing.Played);
        Assert.Same(discarded, standing.Detection);
    }

    [Fact(DisplayName = "BR-CS-002: a detection says the version that made it, whether it marks anything besides the programme, and the share of breaks it found")]
    public void ADetectionSaysItsVersionAndShare()
    {
        RecordingDetection marked = Detected([Span(0, 300, SegmentKind.Programme), Span(300, 360, SegmentKind.Break), Span(360, 1800, SegmentKind.Programme)], version: 4);
        RecordingDetection plain = Detected([Span(0, 1800, SegmentKind.Programme)]);

        Assert.Equal(((int?)4, true, 60d / 1800), (SegmentStanding.Of(marked).ModelVersion, SegmentStanding.Of(marked).Marks, SegmentStanding.Of(marked).BreakShare));
        Assert.Equal(((int?)1, false, 0d), (SegmentStanding.Of(plain).ModelVersion, SegmentStanding.Of(plain).Marks, SegmentStanding.Of(plain).BreakShare));
        Assert.Equal(((int?)null, false, 0d), (SegmentStanding.None.ModelVersion, SegmentStanding.None.Marks, SegmentStanding.None.BreakShare));
    }

    private static RecordingDetection Detected(SegmentSpan[] segments, RecordingId? recording = null, int version = 1)
        => RecordingDetection.Of(recording ?? RecordingId.New(), version, SegmentDetection.Judged(segments, HalfAnHour, []), 2, Noon, Noon);

    private static SegmentSpan Span(double starts, double ends, SegmentKind kind)
        => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends), kind, 0.9);
}
