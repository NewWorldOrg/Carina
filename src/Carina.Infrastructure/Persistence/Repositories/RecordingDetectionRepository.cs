using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class RecordingDetectionRepository(CarinaDbContext context) : IRecordingDetectionRepository
{
    public async Task<RecordingDetection?> FindAsync(RecordingId recording, int modelVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return await context.Set<RecordingDetection>()
            .AsNoTracking()
            .FirstOrDefaultAsync(detection => detection.RecordingId == recording && detection.ModelVersion == modelVersion, cancellationToken);
    }

    public async Task<IReadOnlyList<RecordingDetection>> ListAsync(
        IReadOnlyCollection<RecordingId> recordings,
        int modelVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordings);

        RecordingId[] named = [.. recordings];

        return await context.Set<RecordingDetection>()
            .AsNoTracking()
            .Where(detection => detection.ModelVersion == modelVersion && named.Contains(detection.RecordingId))
            .OrderBy(detection => detection.RecordingId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Inserts the detection, or writes it over the one kept for the same recording and version, in a single
    /// statement.
    /// </summary>
    public async Task KeepAsync(RecordingDetection detection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detection);

        Guid recording = detection.RecordingId.Value;
        int version = detection.ModelVersion;
        string state = detection.State.ToString();
        string segments = SegmentJson.Written(detection.Segments);
        double cmShare = detection.CmShare;
        string snaps = SegmentJson.Written(detection.Snaps);
        int extraction = detection.ExtractionVersionNumber;
        DateTime catalogUntil = detection.CatalogUntil;
        DateTime detectedAt = detection.DetectedAt;

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO segment_detection (recording_id, model_version, state, segments, cm_share, snaps, extraction_version, catalog_until, detected_at)
            VALUES ({recording}, {version}, {state}, {segments}::jsonb, {cmShare}, {snaps}::jsonb, {extraction}, {catalogUntil}, {detectedAt})
            ON CONFLICT (recording_id, model_version)
            DO UPDATE SET
                state = EXCLUDED.state,
                segments = EXCLUDED.segments,
                cm_share = EXCLUDED.cm_share,
                snaps = EXCLUDED.snaps,
                extraction_version = EXCLUDED.extraction_version,
                catalog_until = EXCLUDED.catalog_until,
                detected_at = EXCLUDED.detected_at
            """,
            cancellationToken);
    }
}
