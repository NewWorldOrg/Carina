using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

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

    public async Task<IReadOnlyList<IReadOnlyList<RecordingId>>> FixtureSharersAsync(CancellationToken cancellationToken)
    {
        var heard = await (
                from occurrence in context.Set<CatalogOccurrence>().AsNoTracking()
                join entry in context.Set<CatalogEntry>().AsNoTracking() on occurrence.EntryId equals entry.Id
                where entry.Kind == CatalogKind.ProgrammeFixture
                orderby occurrence.Id
                select new { occurrence.EntryId, occurrence.RecordingId })
            .ToListAsync(cancellationToken);

        return
        [
            .. heard
                .GroupBy(pair => pair.EntryId)
                .Select(group => (IReadOnlyList<RecordingId>)[.. group.Select(pair => pair.RecordingId).Distinct()]),
        ];
    }

    public async Task KeepAsync(CatalogChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        context.AddRange(change.Started);
        context.AddRange(change.Found);
        context.UpdateRange(change.Reestimated);
        context.UpdateRange(change.Overridden);

        await context.SaveChangesAsync(cancellationToken);
    }
}
