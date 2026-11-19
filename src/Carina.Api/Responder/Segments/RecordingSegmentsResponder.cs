using Carina.Api.Services;
using Carina.Domain.Segments;

namespace Carina.Api.Responder.Segments;

public sealed record RecordingSegmentResponder(double StartsAtSec, double EndsAtSec, SegmentKind Kind, double? Probability)
{
    public static RecordingSegmentResponder Of(SegmentSpan stretch)
    {
        ArgumentNullException.ThrowIfNull(stretch);

        return new RecordingSegmentResponder(
            Seconds(stretch.Starts),
            Seconds(stretch.Ends),
            stretch.Kind,
            stretch.Probability is { } probability ? Math.Round(probability, 2, MidpointRounding.AwayFromZero) : null);
    }

    public static double Seconds(TimeSpan at) => Math.Round(at.TotalSeconds, 2, MidpointRounding.AwayFromZero);
}

public sealed record RecordingSegmentsResponder(
    double LengthSec,
    SegmentSource Source,
    SegmentStandingState State,
    int? ModelVersion,
    DateTime? DetectedAt,
    double? CmShare,
    IReadOnlyList<RecordingSegmentResponder> Segments)
{
    public static RecordingSegmentsResponder Of(RecordingSegments segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        SegmentStanding standing = segments.Standing;
        RecordingDetection? detection = standing.Detection;

        return new RecordingSegmentsResponder(
            RecordingSegmentResponder.Seconds(segments.Length),
            standing.Source,
            standing.State,
            standing.ModelVersion,
            detection?.DetectedAt,
            detection is null ? null : Math.Round(standing.BreakShare, 4, MidpointRounding.AwayFromZero),
            [.. standing.Played.Select(RecordingSegmentResponder.Of)]);
    }
}
