using Carina.Domain.Auth;
using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Viewing;

public sealed class PlaybackPosition
{
    private PlaybackPosition()
    {
    }

    public RecordingId RecordingId { get; private set; } = null!;

    public Subject Viewer { get; private set; } = null!;

    public long PositionMs { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public TimeSpan Position => TimeSpan.FromMilliseconds(PositionMs);

    public static PlaybackPosition Reached(
        RecordingId recordingId,
        Subject viewer,
        TimeSpan position,
        DateTime at)
    {
        if (position < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                position,
                "Watching gets to a point inside a recording, so the point it got to is not before the beginning.");
        }

        return Rehydrate(recordingId, viewer, (long)position.TotalMilliseconds, at);
    }

    public static PlaybackPosition Rehydrate(
        RecordingId recordingId,
        Subject viewer,
        long positionMs,
        DateTime updatedAt)
    {
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentNullException.ThrowIfNull(viewer);

        if (positionMs < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(positionMs),
                positionMs,
                "Watching gets to a point inside a recording, so the point it got to is not before the beginning.");
        }

        return new PlaybackPosition
        {
            RecordingId = recordingId,
            Viewer = viewer,
            PositionMs = positionMs,
            UpdatedAt = UtcTimes.Required(updatedAt, nameof(updatedAt)),
        };
    }
}
