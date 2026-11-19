using Carina.Domain.Segments;

namespace Carina.Api.Responder.Segments;

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
