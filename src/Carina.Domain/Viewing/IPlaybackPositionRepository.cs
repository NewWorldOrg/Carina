using Carina.Domain.Auth;
using Carina.Domain.Recordings;

namespace Carina.Domain.Viewing;

public enum PlaybackPositionKeep
{
    Kept = 1,

    NoSuchRecording = 2,
}

/// <summary>
/// Where each viewer's place in each recording is kept, so that a recording opened on one device
/// carries on where it was left on another. One row stands per viewer per recording: a player that
/// sends where it has got to every few seconds moves that row rather than writing a history of it,
/// and the last thing written is what the next player is told. It names the recording by value and
/// holds no key into the ledger, so a place is only kept while the ledger still holds
/// the recording it is a place in, and throwing that recording away forgets it.
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
