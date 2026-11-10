using Carina.Domain.Notifications;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads the programmes asked to be told of as copies and keeps none of them tracked, so a row a failed write
/// left changed is not written by a later save.
/// </summary>
public sealed class ProgrammeWatchRepository(CarinaDbContext context) : IProgrammeWatchRepository
{
    public async Task<ProgrammeWatch?> FindAsync(ProgrammeWatchId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<ProgrammeWatch>()
            .AsNoTracking()
            .SingleOrDefaultAsync(watch => watch.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ProgrammeWatch>> ListFollowedAsync(DateTime at, CancellationToken cancellationToken)
        => await context.Set<ProgrammeWatch>()
            .AsNoTracking()
            .Where(watch => watch.State == ProgrammeWatchState.Waiting
                || (watch.State == ProgrammeWatchState.Notified && watch.EndsAt > at))
            .OrderBy(watch => watch.StartsAt)
            .ThenBy(watch => watch.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ProgrammeWatch watch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(watch);

        context.Add(watch);

        await WriteAsync(watch, cancellationToken);
    }

    public async Task SaveAsync(ProgrammeWatch watch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(watch);

        context.Update(watch);

        await WriteAsync(watch, cancellationToken);
    }

    private async Task WriteAsync(ProgrammeWatch watch, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            context.Entry(watch).State = EntityState.Detached;
        }
    }
}
