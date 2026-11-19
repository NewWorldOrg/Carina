using Carina.Domain.Auth;
using Carina.Domain.Recordings;

namespace Carina.Domain.Viewing;

public enum PlaybackPositionKeep
{
    Kept = 1,

    NoSuchRecording = 2,
}

public interface IPlaybackPositionRepository
{
    Task<PlaybackPosition?> FindAsync(
        RecordingId recordingId,
        Subject viewer,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<RecordingId, PlaybackPosition>> FindEachAsync(
        IReadOnlyCollection<RecordingId> recordingIds,
        Subject viewer,
        CancellationToken cancellationToken);

    Task<PlaybackPositionKeep> KeepAsync(PlaybackPosition reached, CancellationToken cancellationToken);

    Task ForgetAsync(RecordingId recordingId, CancellationToken cancellationToken);
}
