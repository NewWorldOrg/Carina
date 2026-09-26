using Carina.Domain.Auth;
using Carina.Domain.Recordings;

namespace Carina.Domain.Viewing;

public enum PlaybackPositionKeep
{
    Kept = 1,

    NoSuchRecording = 2,
}

/// <summary>
/// Where each viewer's place in each recording is kept. One row stands per viewer per recording and
/// is moved by each report; the last one written is what the next player is told. It names the
/// recording by value, and a place is kept only while the ledger still holds the recording.
/// </summary>
public interface IPlaybackPositionRepository
{
    Task<PlaybackPosition?> FindAsync(
        RecordingId recordingId,
        Subject viewer,
        CancellationToken cancellationToken);

    Task<PlaybackPositionKeep> KeepAsync(PlaybackPosition reached, CancellationToken cancellationToken);

    Task ForgetAsync(RecordingId recordingId, CancellationToken cancellationToken);
}
