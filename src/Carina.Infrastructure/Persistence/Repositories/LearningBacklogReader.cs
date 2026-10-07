using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads the recordings that have ended for the reading of their learning data. Which records wait to be
/// read is <see cref="LearningExtraction.AwaitsReading"/> written out in the query.
/// </summary>
public sealed class LearningBacklogReader(CarinaDbContext context) : ILearningBacklog
{
    public async Task<IReadOnlyList<BackloggedRecording>> AwaitingAsync(
        IReadOnlyList<OutputRoot> withinReach,
        int atMost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(withinReach);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(atMost);

        OutputRoot[] reachable = [.. withinReach];
        int number = ExtractionVersion.Current.Number;
        ExtractionOrigin origin = ExtractionVersion.Current.Origin;

        return await (
                from recording in context.Set<Recording>().AsNoTracking()
                where recording.Outcome != null && reachable.Contains(recording.OutputRoot)
                join kept in context.Set<LearningExtraction>().AsNoTracking()
                    on recording.Id equals kept.RecordingId into records
                from record in records.DefaultIfEmpty()
                where record == null
                      || record.State == LearningExtractionState.Waiting
                      || (record.State == LearningExtractionState.Failed && record.Failures <= LearningExtraction.MostRetries)
                      || ((record.State == LearningExtractionState.Done || record.State == LearningExtractionState.Partial)
                          && (record.Version!.Number != number || record.Version.Origin != origin))
                      || (record.State == LearningExtractionState.Partial && record.UpdatedAt < recording.StoppedAtActual)
                orderby recording.StartedAtActual descending, recording.Id
                select new BackloggedRecording(recording, record))
            .Take(atMost)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LearningExtraction>> UncaptionedAsync(int atMost, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(atMost);

        return await (
                from record in context.Set<LearningExtraction>().AsNoTracking()
                join recording in context.Set<Recording>().AsNoTracking() on record.RecordingId equals recording.Id
                where (record.State == LearningExtractionState.Done || record.State == LearningExtractionState.Partial)
                      && record.ReadThrough > TimeSpan.Zero
                      && recording.CaptionState == CaptionState.Ready
                      && recording.CaptionsMadeAt != null
                      && !context.Set<LearningDataBlock>().Any(block => block.RecordingId == record.RecordingId
                                                                        && block.Kind == LearningDataKind.CaptionPresence
                                                                        && block.Chunk == 0
                                                                        && block.WrittenAt >= recording.CaptionsMadeAt
                                                                        && block.WrittenAt >= record.UpdatedAt)
                orderby recording.StartedAtActual descending, record.RecordingId
                select record)
            .Take(atMost)
            .ToListAsync(cancellationToken);
    }
}
