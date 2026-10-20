using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads the stretches of the adverts the catalog is sure of, each beside what its entry is and the length of
/// learning data read from its recording, without the fingerprints, and counts what the catalog is sure of in each
/// recording in the domain.
/// </summary>
public sealed class CatalogEvidenceReader(CarinaDbContext context) : ICatalogEvidenceReader
{
    public async Task<EvaluationEvidence> ReadAsync(CancellationToken cancellationToken)
    {
        var read = await (
                from occurrence in context.Set<CatalogOccurrence>().AsNoTracking()
                join entry in context.Set<CatalogEntry>().AsNoTracking() on occurrence.EntryId equals entry.Id
                join record in context.Set<LearningExtraction>().AsNoTracking() on occurrence.RecordingId equals record.RecordingId
                where entry.Kind == CatalogKind.Cm
                      && entry.Confidence >= CatalogTruth.SureOfBreaks
                      && (record.State == LearningExtractionState.Done || record.State == LearningExtractionState.Partial)
                      && record.ReadThrough > TimeSpan.Zero
                select new
                {
                    occurrence.RecordingId,
                    occurrence.StartsAt,
                    occurrence.EndsAt,
                    occurrence.KindOverride,
                    entry.Id,
                    entry.Kind,
                    entry.Confidence,
                    entry.OccurrenceCount,
                    entry.SeriesCount,
                    entry.StationCount,
                    record.ReadThrough,
                })
            .ToListAsync(cancellationToken);

        return EvaluationEvidence.Of(
        [
            .. read
                .GroupBy(stretch => stretch.RecordingId)
                .Select(recording => CatalogTruth.Of(
                    [
                        .. recording.Select(stretch => new CatalogHeard(
                            stretch.StartsAt,
                            stretch.EndsAt,
                            CatalogHeard.KindOf(stretch.KindOverride, stretch.Kind),
                            stretch.OccurrenceCount,
                            stretch.SeriesCount,
                            stretch.StationCount,
                            stretch.Confidence,
                            stretch.Id)),
                    ],
                    recording.First().ReadThrough)),
        ]);
    }
}
