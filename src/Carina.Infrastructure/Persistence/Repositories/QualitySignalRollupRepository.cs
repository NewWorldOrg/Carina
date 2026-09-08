using Carina.Domain.Quality;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class QualitySignalRollupRepository(CarinaDbContext context) : IQualitySignalRollupRepository
{
    public async Task SaveAsync(IReadOnlyList<QualitySignalRollup> rollups, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rollups);

        if (rollups.Count is 0)
        {
            return;
        }

        foreach (QualitySignalRollup rollup in rollups)
        {
            bool held = await context.Set<QualitySignalRollup>()
                .AsNoTracking()
                .AnyAsync(
                    row => row.Granularity == rollup.Granularity
                           && row.WindowStart == rollup.WindowStart
                           && row.Tuner == rollup.Tuner
                           && row.Network == rollup.Network
                           && row.Service == rollup.Service,
                    cancellationToken);

            if (held)
            {
                context.Update(rollup);
            }
            else
            {
                context.Add(rollup);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QualitySignalRollup>> ListAsync(
        QualityWindow granularity,
        DateTime from,
        DateTime until,
        CancellationToken cancellationToken)
        => await context.Set<QualitySignalRollup>()
            .AsNoTracking()
            .Where(rollup => rollup.Granularity == granularity
                             && rollup.WindowStart >= from
                             && rollup.WindowStart < until)
            .OrderBy(rollup => rollup.WindowStart)
            .ToListAsync(cancellationToken);

    public async Task<DateTime?> LatestWindowStartAsync(QualityWindow granularity, CancellationToken cancellationToken)
        => await context.Set<QualitySignalRollup>()
            .AsNoTracking()
            .Where(rollup => rollup.Granularity == granularity)
            .OrderByDescending(rollup => rollup.WindowStart)
            .Select(rollup => (DateTime?)rollup.WindowStart)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> ForgetStartedBeforeAsync(
        QualityWindow granularity,
        DateTime cutoff,
        CancellationToken cancellationToken)
        => await context.Set<QualitySignalRollup>()
            .Where(rollup => rollup.Granularity == granularity && rollup.WindowStart < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
}
