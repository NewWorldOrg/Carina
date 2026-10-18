using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads the records of the learning data and the data itself for detecting segments. Which records wait to
/// be detected is <see cref="RecordingDetection.IsAwaited"/> written out in <see cref="Awaiting"/>, for the
/// recordings still kept.
/// </summary>
public sealed class DetectionWorklist(CarinaDbContext context) : IDetectionWorklist
{
    public static IQueryable<LearningExtraction> Awaiting(CarinaDbContext context, int modelVersion)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Read(context)
            .Where(record => record.ReadThrough > TimeSpan.Zero
                             && record.MatchedAt != null
                             && record.MatchedAt >= record.UpdatedAt
                             && !context.Set<RecordingDetection>().Any(detection => detection.RecordingId == record.RecordingId
                                                                                   && detection.ModelVersion == modelVersion
                                                                                   && detection.DetectedAt >= record.MatchedAt));
    }

    public async Task<LearningExtraction?> NextAsync(
        int modelVersion,
        IReadOnlyCollection<RecordingId> passedOver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passedOver);

        RecordingId[] skipped = [.. passedOver];

        return await Awaiting(context, modelVersion)
            .Where(record => context.Set<Recording>().Any(recording => recording.Id == record.RecordingId)
                             && !skipped.Contains(record.RecordingId))
            .OrderByDescending(record => record.Programme.RecordingStartedAt)
            .ThenBy(record => record.RecordingId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LearningExtraction>> EarlierOnTheStationAsync(
        SeriesMember judged,
        int most,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(judged);
        ArgumentOutOfRangeException.ThrowIfNegative(most);

        return await Read(context)
            .Where(record => record.Programme.NetworkId == judged.NetworkId
                             && record.Programme.ServiceId == judged.ServiceId
                             && record.Programme.RecordingStartedAt < judged.StartedAt
                             && record.RecordingId != judged.RecordingId)
            .OrderByDescending(record => record.Programme.RecordingStartedAt)
            .ThenBy(record => record.RecordingId)
            .Take(most)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LearningExtraction>> RecordsAsync(
        IReadOnlyCollection<RecordingId> recordings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordings);

        RecordingId[] named = [.. recordings];

        return await Read(context)
            .Where(record => named.Contains(record.RecordingId))
            .OrderBy(record => record.Programme.RecordingStartedAt)
            .ThenBy(record => record.RecordingId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LearningDataPart>> PartsAsync(
        RecordingId recording,
        IReadOnlyCollection<LearningDataKind> kinds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(kinds);

        LearningDataKind[] wanted = [.. kinds];
        List<LearningDataBlock> blocks = await context.Set<LearningDataBlock>()
            .AsNoTracking()
            .Where(block => block.RecordingId == recording && wanted.Contains(block.Kind))
            .OrderBy(block => block.Kind)
            .ThenBy(block => block.Chunk)
            .ToListAsync(cancellationToken);

        return [.. blocks.Select(Part).OfType<LearningDataPart>()];
    }

    private static IQueryable<LearningExtraction> Read(CarinaDbContext context)
        => context.Set<LearningExtraction>()
            .AsNoTracking()
            .Where(record => record.State == LearningExtractionState.Done || record.State == LearningExtractionState.Partial);

    private static LearningDataPart? Part(LearningDataBlock block) => block.TryRead(out LearningDataPart? part) ? part : null;
}
