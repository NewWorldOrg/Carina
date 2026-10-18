using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads the records of the learning data and the data itself for matching against the catalog. Which
/// records wait to be matched is <see cref="LearningExtraction.AwaitsMatching"/> written out in
/// <see cref="Awaiting"/>.
/// </summary>
public sealed class CatalogWorklist(CarinaDbContext context) : ICatalogWorklist
{
    public static IQueryable<LearningExtraction> Awaiting(CarinaDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Read(context).Where(record => record.MatchedAt == null || record.MatchedAt < record.UpdatedAt);
    }

    public async Task<LearningExtraction?> NextAsync(CancellationToken cancellationToken)
        => await Awaiting(context)
            .OrderByDescending(record => record.Programme.RecordingStartedAt)
            .ThenBy(record => record.RecordingId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LearningExtraction>> AroundAsync(
        RecordingId heard,
        DateTime startedAt,
        TimeSpan within,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(heard);

        DateTime from = startedAt - within;
        DateTime to = startedAt + within;

        return await Read(context)
            .Where(record => record.RecordingId != heard
                             && record.Programme.RecordingStartedAt >= from
                             && record.Programme.RecordingStartedAt <= to)
            .OrderBy(record => record.Programme.RecordingStartedAt)
            .ThenBy(record => record.RecordingId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<RecordingId, ProgrammeCopy>> ProgrammesAsync(
        IReadOnlyCollection<RecordingId> recordings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordings);

        RecordingId[] named = [.. recordings];

        return await context.Set<LearningExtraction>()
            .AsNoTracking()
            .Where(record => named.Contains(record.RecordingId))
            .ToDictionaryAsync(record => record.RecordingId, record => record.Programme, cancellationToken);
    }

    public async Task<SoundTrack> SoundAsync(RecordingId recording, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        List<LearningDataBlock> blocks = await context.Set<LearningDataBlock>()
            .AsNoTracking()
            .Where(block => block.RecordingId == recording
                            && (block.Kind == LearningDataKind.SoundFingerprints || block.Kind == LearningDataKind.Loudness))
            .ToListAsync(cancellationToken);

        return SoundTrack.Of(blocks.Select(Part).OfType<LearningDataPart>());
    }

    private static IQueryable<LearningExtraction> Read(CarinaDbContext context)
        => context.Set<LearningExtraction>()
            .AsNoTracking()
            .Where(record => record.State == LearningExtractionState.Done || record.State == LearningExtractionState.Partial);

    private static LearningDataPart? Part(LearningDataBlock block) => block.TryRead(out LearningDataPart? part) ? part : null;
}
