using Carina.Domain.Segments;

namespace Carina.Api.Responder.Segments;

/// <summary>
/// A seek as it was taken: when, whether it went forward or came back, how it was made, and the two moments it names
/// in the recording's own time to the hundredth of a second.
/// </summary>
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
