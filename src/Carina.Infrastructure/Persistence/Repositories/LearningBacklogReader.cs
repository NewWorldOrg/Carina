using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads the recordings that have ended for the reading of their learning data. Which records wait to be
/// read is <see cref="LearningExtraction.AwaitsReading"/> written out in <see cref="Awaiting"/>, the one
/// query both the reading and the count of what waits are made from.
/// </summary>
public sealed class LearningBacklogReader(CarinaDbContext context) : ILearningBacklog
{
    /// <summary>
    /// The recordings that have ended other than in failure, which leaves no file to read, and whose
    /// learning data waits to be read from their files with <see cref="ExtractionVersion.Current"/>: those
    /// with no record, and those whose record waits.
    /// </summary>
    public static IQueryable<Recording> Awaiting(CarinaDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int number = ExtractionVersion.Current.Number;
        ExtractionOrigin origin = ExtractionVersion.Current.Origin;
        IQueryable<LearningExtraction> records = context.Set<LearningExtraction>().AsNoTracking();

        return context.Set<Recording>()
            .AsNoTracking()
            .Where(recording => recording.Outcome != null
                                && recording.Outcome != RecordingOutcome.Failed
                                && (!records.Any(record => record.RecordingId == recording.Id)
                                    || records.Any(record => record.RecordingId == recording.Id
                                                             && (record.State == LearningExtractionState.Waiting
                                                                 || (record.State == LearningExtractionState.Failed
                                                                     && record.Failures <= LearningExtraction.MostRetries)
                                                                 || ((record.State == LearningExtractionState.Done
                                                                      || record.State == LearningExtractionState.Partial)
                                                                     && (record.Version!.Number != number || record.Version.Origin != origin))
                                                                 || (record.State == LearningExtractionState.Partial
                                                                     && record.UpdatedAt < recording.StoppedAtActual)))));
    }

    public async Task<IReadOnlyList<BackloggedRecording>> AwaitingAsync(
        IReadOnlyList<OutputRoot> withinReach,
        int skip,
        int atMost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(withinReach);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(atMost);

        OutputRoot[] reachable = [.. withinReach];
        List<Recording> recordings = await Awaiting(context)
            .Where(recording => reachable.Contains(recording.OutputRoot))
            .OrderByDescending(recording => recording.StartedAtActual)
            .ThenBy(recording => recording.Id)
            .Skip(skip)
            .Take(atMost)
            .ToListAsync(cancellationToken);
        RecordingId[] ids = [.. recordings.Select(recording => recording.Id)];
        Dictionary<RecordingId, LearningExtraction> records = await context.Set<LearningExtraction>()
            .AsNoTracking()
            .Where(record => ids.Contains(record.RecordingId))
            .ToDictionaryAsync(record => record.RecordingId, cancellationToken);

        return [.. recordings.Select(recording => new BackloggedRecording(recording, records.GetValueOrDefault(recording.Id)))];
    }

    public async Task<IReadOnlyList<LearningExtraction>> UncaptionedAsync(int skip, int atMost, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
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
            .Skip(skip)
            .Take(atMost)
            .ToListAsync(cancellationToken);
    }
}
