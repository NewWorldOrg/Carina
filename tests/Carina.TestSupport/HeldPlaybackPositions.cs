using Carina.Domain.Auth;
using Carina.Domain.Recordings;
using Carina.Domain.Viewing;

namespace Carina.TestSupport;

public sealed class HeldPlaybackPositions : IPlaybackPositionRepository
{
    public List<PlaybackPosition> Positions { get; } = [];

    public int ReadsOfOne { get; private set; }

    public int ReadsOfMany { get; private set; }

    public Task<PlaybackPosition?> FindAsync(
        RecordingId recordingId,
        Subject viewer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentNullException.ThrowIfNull(viewer);

        ReadsOfOne++;

        return Task.FromResult(Positions.FirstOrDefault(position =>
            position.RecordingId.Equals(recordingId) && position.Viewer.Equals(viewer)));
    }

    public Task<IReadOnlyDictionary<RecordingId, PlaybackPosition>> FindEachAsync(
        IReadOnlyCollection<RecordingId> recordingIds,
        Subject viewer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordingIds);
        ArgumentNullException.ThrowIfNull(viewer);

        ReadsOfMany++;

        return Task.FromResult<IReadOnlyDictionary<RecordingId, PlaybackPosition>>(Positions
            .Where(position => position.Viewer.Equals(viewer) && recordingIds.Contains(position.RecordingId))
            .ToDictionary(position => position.RecordingId));
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
