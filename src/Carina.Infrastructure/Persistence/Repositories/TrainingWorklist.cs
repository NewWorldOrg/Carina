using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads the recordings a round of learning could read from the records of the learning data, beside whether each
/// has an answer standing and whether it was confirmed, a stretch of the catalog or a skip event, in one statement.
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
                Confirmed = context.Set<RecordingAnswer>().Any(answer => answer.RecordingId == record.RecordingId && answer.WithdrawnAt == null && answer.Confirmed),
                Answered = context.Set<RecordingAnswer>().Any(answer => answer.RecordingId == record.RecordingId && answer.WithdrawnAt == null),
                Heard = context.Set<CatalogOccurrence>().Any(occurrence => occurrence.RecordingId == record.RecordingId)
                        || context.Set<SkipEvent>().Any(skip => skip.RecordingId == record.RecordingId),
            })
            .Where(record => record.Answered || record.Heard)
            .ToListAsync(cancellationToken);

        return
        [
            .. read.Select(record => new LearningCandidate(
                record.RecordingId,
                record.RecordingStartedAt,
                record.ReadThrough,
                Ground(record.Confirmed, record.Answered))),
        ];
    }

    private static LearningGround Ground(bool confirmed, bool answered)
        => (confirmed, answered) switch
        {
            (true, _) => LearningGround.Confirmed,
            (false, true) => LearningGround.Answered,
            _ => LearningGround.Other,
        };
}
