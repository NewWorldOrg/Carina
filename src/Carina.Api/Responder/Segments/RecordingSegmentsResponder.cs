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
/// from and how they stand; the version of the model that detected them, when it detected them and how much of the
/// programme breaks took; and the stretches, none when the sequence was thrown away.
/// </summary>
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
