using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class CatalogEvidenceReader(CarinaDbContext context) : ICatalogEvidenceReader
{
    private static readonly LearningDataKind[] Seams = [LearningDataKind.Loudness, LearningDataKind.FrameLights];

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

        (RecordingId Recording, TimeSpan ReadThrough, CatalogHeard[] Heard)[] recordings =
        [
            .. read
                .GroupBy(stretch => stretch.RecordingId)
                .Select(recording => (
                    recording.Key,
                    recording.First().ReadThrough,
                    (CatalogHeard[])
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
                    ])),
        ];
        List<CatalogTruth> truths = [];

        foreach ((RecordingId recording, TimeSpan readThrough, CatalogHeard[] heard) in recordings)
        {
            truths.Add(CatalogTruth.Of(heard, SegmentEvidence.Of(await PartsAsync(recording, cancellationToken), readThrough)));
        }

        return EvaluationEvidence.Of(truths);
    }

    private async Task<IReadOnlyList<LearningDataPart>> PartsAsync(RecordingId recording, CancellationToken cancellationToken)
    {
        List<LearningDataBlock> blocks = await context.Set<LearningDataBlock>()
            .AsNoTracking()
            .Where(block => block.RecordingId == recording && Seams.Contains(block.Kind))
            .ToListAsync(cancellationToken);

        return [.. blocks.Select(block => block.TryRead(out LearningDataPart? part) ? part : null).OfType<LearningDataPart>()];
    }
}
