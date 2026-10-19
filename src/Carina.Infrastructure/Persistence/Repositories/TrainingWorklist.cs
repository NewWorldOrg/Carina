using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads the recordings a round of learning could read from the records of the learning data, beside whether the
/// catalog hears an advert in each, in one statement.
/// </summary>
public sealed class TrainingWorklist(CarinaDbContext context) : ITrainingWorklist
{
    public async Task<IReadOnlyList<LearningCandidate>> CandidatesAsync(CancellationToken cancellationToken)
    {
        var read = await context.Set<LearningExtraction>()
            .AsNoTracking()
            .Where(record => (record.State == LearningExtractionState.Done || record.State == LearningExtractionState.Partial)
                             && record.ReadThrough > TimeSpan.Zero)
            .Select(record => new
            {
                record.RecordingId,
                record.Programme.RecordingStartedAt,
                record.ReadThrough,
                Adverts = context.Set<CatalogOccurrence>()
                    .Any(occurrence => occurrence.RecordingId == record.RecordingId
                                       && context.Set<CatalogEntry>().Any(entry => entry.Id == occurrence.EntryId && entry.Kind == CatalogKind.Cm)),
                Heard = context.Set<CatalogOccurrence>().Any(occurrence => occurrence.RecordingId == record.RecordingId)
                        || context.Set<SkipEvent>().Any(skip => skip.RecordingId == record.RecordingId),
            })
            .Where(record => record.Heard)
            .ToListAsync(cancellationToken);

        return
        [
            .. read.Select(record => new LearningCandidate(
                record.RecordingId,
                record.RecordingStartedAt,
                record.ReadThrough,
                record.Adverts ? LearningGround.CatalogAdverts : LearningGround.Other)),
        ];
    }
}
