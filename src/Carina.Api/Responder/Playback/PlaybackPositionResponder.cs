using Carina.Domain.Viewing;

namespace Carina.Api.Responder.Playback;

public sealed record PlaybackPositionResponder(string RecordingId, double PositionSec, DateTime UpdatedAt)
{
    public static PlaybackPositionResponder Of(PlaybackPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        return new PlaybackPositionResponder(
            position.RecordingId.Wire,
            position.Position.TotalSeconds,
            position.UpdatedAt);
    }
}
