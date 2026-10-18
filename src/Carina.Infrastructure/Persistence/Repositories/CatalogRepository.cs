using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class CatalogRepository(CarinaDbContext context) : ICatalogRepository
{
    public async Task<IReadOnlyList<CatalogEntry>> ListEntriesAsync(CancellationToken cancellationToken)
        => await context.Set<CatalogEntry>()
            .AsNoTracking()
            .OrderBy(entry => entry.FirstSeenAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CatalogEntry>> EntriesAsync(
        IReadOnlyCollection<CatalogEntryId> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        CatalogEntryId[] named = [.. entries];

        return await context.Set<CatalogEntry>()
            .AsNoTracking()
            .Where(entry => named.Contains(entry.Id))
            .OrderBy(entry => entry.FirstSeenAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogOccurrence>> OccurrencesOfAsync(
        IReadOnlyCollection<CatalogEntryId> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        CatalogEntryId[] named = [.. entries];

        return await context.Set<CatalogOccurrence>()
            .AsNoTracking()
            .Where(occurrence => named.Contains(occurrence.EntryId))
            .OrderBy(occurrence => occurrence.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogOccurrence>> OccurrencesInAsync(
        IReadOnlyCollection<RecordingId> recordings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordings);

        RecordingId[] named = [.. recordings];

        return await context.Set<CatalogOccurrence>()
            .AsNoTracking()
            .Where(occurrence => named.Contains(occurrence.RecordingId))
            .OrderBy(occurrence => occurrence.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyList<RecordingId>>> SoundSharersAsync(CancellationToken cancellationToken)
    {
        CatalogKind[] linking = [CatalogKind.Opening, CatalogKind.Ending, CatalogKind.ProgrammeFixture];
        var heard = await (
                from occurrence in context.Set<CatalogOccurrence>().AsNoTracking()
                join entry in context.Set<CatalogEntry>().AsNoTracking() on occurrence.EntryId equals entry.Id
                where linking.Contains(entry.Kind)
                orderby occurrence.Id
                select new { entry.Kind, entry.Length, Occurrence = occurrence })
            .ToListAsync(cancellationToken);

        return
        [
            .. heard
                .Where(pair => CatalogKinds.LinksSeries(pair.Kind, pair.Length, pair.Occurrence))
                .GroupBy(pair => pair.Occurrence.EntryId)
                .Select(group => (IReadOnlyList<RecordingId>)[.. group.Select(pair => pair.Occurrence.RecordingId).Distinct()]),
        ];
    }

    public async Task<CatalogEntry?> NextToReestimateAsync(CancellationToken cancellationToken)
        => await context.Set<CatalogEntry>()
            .AsNoTracking()
            .Where(entry => entry.KindsVersion < CatalogKinds.Version)
            .OrderBy(entry => entry.FirstSeenAt)
            .ThenBy(entry => entry.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task KeepAsync(CatalogChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        context.AddRange(change.Started);
        context.AddRange(change.Found);
        context.UpdateRange(change.Reestimated);
        context.UpdateRange(change.Overridden);

        await context.SaveChangesAsync(cancellationToken);

        if (change.Relinking.Count > 0)
        {
            Guid[] relinking = [.. change.Relinking.Select(entry => entry.Value)];

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE segment_catalog_entry
                SET kinds_version = {CatalogKinds.Unsettled}
                WHERE NOT (id = ANY ({relinking}))
                  AND id IN (
                      SELECT heard.entry_id
                      FROM segment_catalog_occurrence heard
                      JOIN segment_catalog_occurrence linking ON linking.recording_id = heard.recording_id
                      WHERE linking.entry_id = ANY ({relinking}))
                """,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
