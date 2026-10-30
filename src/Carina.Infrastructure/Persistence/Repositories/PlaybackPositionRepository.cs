using Carina.Domain.Auth;
using Carina.Domain.Recordings;
using Carina.Domain.Viewing;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class PlaybackPositionRepository(CarinaDbContext context) : IPlaybackPositionRepository
{
    public async Task<PlaybackPosition?> FindAsync(
        RecordingId recordingId,
        Subject viewer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentNullException.ThrowIfNull(viewer);

        return await context.Set<PlaybackPosition>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                position => position.RecordingId == recordingId && position.Viewer == viewer,
                cancellationToken);
    }

    public async Task<IReadOnlyDictionary<RecordingId, PlaybackPosition>> FindEachAsync(
        IReadOnlyCollection<RecordingId> recordingIds,
        Subject viewer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordingIds);
        ArgumentNullException.ThrowIfNull(viewer);

        if (recordingIds.Count == 0)
        {
            return new Dictionary<RecordingId, PlaybackPosition>();
        }

        List<PlaybackPosition> found = await context.Set<PlaybackPosition>()
            .AsNoTracking()
            .Where(position => position.Viewer == viewer && recordingIds.Contains(position.RecordingId))
            .ToListAsync(cancellationToken);

        return found.ToDictionary(position => position.RecordingId);
    }

    /// <remarks>
    /// Written in one statement, which keeps nothing for a recording that no longer exists.
    /// </remarks>
    public async Task<PlaybackPositionKeep> KeepAsync(
        PlaybackPosition reached,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reached);

        Guid recording = reached.RecordingId.Value;
        string viewer = reached.Viewer.Value;
        long positionMs = reached.PositionMs;
        DateTime at = reached.UpdatedAt;

        int kept = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO playback_position (recording_id, subject, position_ms, updated_at)
            SELECT {recording}, {viewer}, {positionMs}, {at}
            WHERE EXISTS (SELECT 1 FROM recording WHERE id = {recording})
            ON CONFLICT (recording_id, subject)
            DO UPDATE SET position_ms = EXCLUDED.position_ms, updated_at = EXCLUDED.updated_at
            """,
            cancellationToken);

        return kept > 0 ? PlaybackPositionKeep.Kept : PlaybackPositionKeep.NoSuchRecording;
    }

    public async Task ForgetAsync(RecordingId recordingId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordingId);

        await context.Set<PlaybackPosition>()
            .Where(position => position.RecordingId == recordingId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
