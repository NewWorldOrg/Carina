using Carina.Api.Common;
using Carina.Domain.Auth;
using Carina.Domain.Recordings;
using Carina.Domain.Viewing;

namespace Carina.Api.Services;

public enum PlaybackPositionRefusal
{
    NotASecondIntoTheRecording = 1,

    NoSuchRecording = 2,

    StillBeingWritten = 3,
}

public sealed class PlaybackPositionService(
    IRecordingDirectory recordings,
    IPlaybackPositionRepository positions,
    TimeProvider clock)
{
    public const string ThePositionsThereAre =
        "Watching gets to a whole or fractional number of seconds into a recording, counted from its beginning.";

    public async Task<ServiceResult<PlaybackPosition, PlaybackPositionRefusal>> KeepAsync(
        RecordingId id,
        Subject viewer,
        double? seconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(viewer);

        if (Read(seconds) is not { } reached)
        {
            return Refused(ThePositionsThereAre, PlaybackPositionRefusal.NotASecondIntoTheRecording);
        }

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return Refused($"There is no recording {id.Wire}.", PlaybackPositionRefusal.NoSuchRecording);
        }

        if (recording.IsInFlight)
        {
            return Refused(
                $"Recording {id.Wire} is still being written, so there is no watching of it to remember.",
                PlaybackPositionRefusal.StillBeingWritten);
        }

        PlaybackPosition position = PlaybackPosition.Reached(
            id,
            viewer,
            reached,
            clock.GetUtcNow().UtcDateTime);

        return await positions.KeepAsync(position, cancellationToken) is PlaybackPositionKeep.Kept
            ? ServiceResult<PlaybackPosition, PlaybackPositionRefusal>.Success(position)
            : Refused(
                $"Recording {id.Wire} was thrown away while where it had been watched to was being kept.",
                PlaybackPositionRefusal.NoSuchRecording);
    }

    private static TimeSpan? Read(double? seconds)
        => seconds is { } named
           && double.IsFinite(named)
           && named >= 0
           && named <= TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(named)
            : null;

    private static ServiceResult<PlaybackPosition, PlaybackPositionRefusal> Refused(
        string said,
        PlaybackPositionRefusal refusal)
        => ServiceResult<PlaybackPosition, PlaybackPositionRefusal>.Failure(said, refusal);
}
