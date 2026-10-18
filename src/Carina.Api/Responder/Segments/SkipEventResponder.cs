using Carina.Domain.Segments;

namespace Carina.Api.Responder.Segments;

/// <summary>
/// A skip event as it was taken: when, what the viewer did, the kind of the stretch the button offered, the two
/// moments it names in the recording's own time to the hundredth of a second, and the version whose detection
/// offered the button, none when an answer did.
/// </summary>
public sealed record SkipEventResponder(DateTime HappenedAt, ViewerActionKind Event, SegmentKind Kind, double FromSec, double ToSec, int? ModelVersion)
{
    public static SkipEventResponder Of(SkipEvent skip)
    {
        ArgumentNullException.ThrowIfNull(skip);

        return new SkipEventResponder(
            skip.HappenedAt,
            skip.Event,
            skip.Kind,
            RecordingSegmentResponder.Seconds(skip.From),
            RecordingSegmentResponder.Seconds(skip.To),
            skip.ModelVersion);
    }
}
