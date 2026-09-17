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

    /// <remarks>
    /// One statement, so that a player sending where it has got to every few seconds cannot read a
    /// row, be overtaken, and write back over what overtook it, and so that a place is not kept for
    /// a recording that was thrown away while the writing was on its way.
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
