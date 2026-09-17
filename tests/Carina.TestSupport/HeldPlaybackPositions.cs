using Carina.Domain.Auth;
using Carina.Domain.Recordings;
using Carina.Domain.Viewing;

namespace Carina.TestSupport;

public sealed class HeldPlaybackPositions : IPlaybackPositionRepository
{
    public List<PlaybackPosition> Positions { get; } = [];

    public Task<PlaybackPosition?> FindAsync(
        RecordingId recordingId,
        Subject viewer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentNullException.ThrowIfNull(viewer);

        return Task.FromResult(Positions.FirstOrDefault(position =>
            position.RecordingId.Equals(recordingId) && position.Viewer.Equals(viewer)));
    }

    public Task<PlaybackPositionKeep> KeepAsync(PlaybackPosition reached, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reached);

        Positions.RemoveAll(position =>
            position.RecordingId.Equals(reached.RecordingId) && position.Viewer.Equals(reached.Viewer));
        Positions.Add(reached);

        return Task.FromResult(PlaybackPositionKeep.Kept);
    }

    public Task ForgetAsync(RecordingId recordingId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordingId);

        Positions.RemoveAll(position => position.RecordingId.Equals(recordingId));

        return Task.CompletedTask;
    }
}
