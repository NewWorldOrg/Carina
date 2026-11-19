using Carina.Domain.Segments;

namespace Carina.Api.Responder.Segments;

public sealed record SeekEventResponder(DateTime HappenedAt, SeekAction Event, SeekMeans Means, double FromSec, double ToSec)
{
    public static SeekEventResponder Of(SeekEvent seek)
    {
        ArgumentNullException.ThrowIfNull(seek);

        return new SeekEventResponder(
            seek.HappenedAt,
            seek.Event,
            seek.Means,
            RecordingSegmentResponder.Seconds(seek.From),
            RecordingSegmentResponder.Seconds(seek.To));
    }
}
