using Carina.Api.Services;
using Carina.Domain.Segments;

namespace Carina.Api.Responder.Segments;

/// <summary>
/// A stretch of a recording in the recording's own time, to the hundredth of a second, what it is, and how likely
/// that is, as the weighted mean of what it was put together from.
/// </summary>
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

/// <summary>
/// The stretches of a recording in its own time: how long the recording was written for; where its stretches come
/// from and how they stand; the version of the model that detected them, whether they are confirmed and when they
/// were answered, detected and how much of the programme breaks took; the stretches, none when the sequence was
/// thrown away; and the boundary candidates' seconds a boundary is drawn to.
/// </summary>
public sealed record RecordingSegmentsResponder(
    double LengthSec,
    SegmentSource Source,
    SegmentStandingState State,
    int? ModelVersion,
    bool Confirmed,
    DateTime? AnsweredAt,
    DateTime? DetectedAt,
    double? CmShare,
    IReadOnlyList<RecordingSegmentResponder> Segments,
    IReadOnlyList<double> Snaps)
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
            detection?.ModelVersion,
            false,
            null,
            detection?.DetectedAt,
            detection is null ? null : Math.Round(detection.CmShare, 4, MidpointRounding.AwayFromZero),
            [.. standing.Played.Select(RecordingSegmentResponder.Of)],
            [.. (detection?.Snaps ?? []).Select(RecordingSegmentResponder.Seconds)]);
    }
}
