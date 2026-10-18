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

    private static RecordingDetection Detected(SegmentSpan[] segments)
        => RecordingDetection.Of(RecordingId.New(), 1, SegmentDetection.Judged(segments, HalfAnHour, []), 2, Noon, Noon);

    private static SegmentSpan Span(double starts, double ends, SegmentKind kind)
        => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends), kind, 0.9);
}
