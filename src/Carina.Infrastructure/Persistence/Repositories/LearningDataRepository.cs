using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class LearningDataRepository(CarinaDbContext context) : ILearningDataRepository
{
    public async Task KeepAsync(LearningDataBlock block, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(block);

        Guid recording = block.RecordingId.Value;
        short kind = (short)block.Kind;
        int chunk = block.Chunk;
        byte[] bytes = block.Bytes;
        int number = block.Version.Number;
        string origin = block.Version.Origin.ToString();
        DateTime at = block.WrittenAt;

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO segment_learning_data (recording_id, kind, chunk, bytes, version_number, version_origin, written_at)
            VALUES ({recording}, {kind}, {chunk}, {bytes}, {number}, {origin}, {at})
            ON CONFLICT (recording_id, kind, chunk)
            DO UPDATE SET
                bytes = EXCLUDED.bytes,
                version_number = EXCLUDED.version_number,
                version_origin = EXCLUDED.version_origin,
                written_at = EXCLUDED.written_at
            """,
            cancellationToken);
    }

    public async Task<LearningDataBlock?> FindAsync(
        RecordingId recordingId,
        LearningDataKind kind,
        int chunk,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordingId);

        return await context.Set<LearningDataBlock>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                block => block.RecordingId == recordingId && block.Kind == kind && block.Chunk == chunk,
                cancellationToken);
    }

    public async Task<int> CountAsync(RecordingId recordingId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordingId);

        return await context.Set<LearningDataBlock>()
            .CountAsync(block => block.RecordingId == recordingId, cancellationToken);
    }

    public async Task<long> SizeAsync(CancellationToken cancellationToken)
        => await context.Database
            .SqlQuery<long>($"SELECT COALESCE(sum(octet_length(bytes)), 0)::bigint AS \"Value\" FROM segment_learning_data")
            .SingleAsync(cancellationToken);
}
