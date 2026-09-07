using Carina.Domain.Quality;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class QualityThresholdRepository(CarinaDbContext context) : IQualityThresholdRepository
{
    public async Task<IReadOnlyList<QualityThreshold>> ListAsync(CancellationToken cancellationToken)
        => await context.Set<QualityThreshold>()
            .AsNoTracking()
            .OrderBy(threshold => threshold.Key)
            .ToListAsync(cancellationToken);

    public async Task<QualityThreshold?> FindAsync(QualityThresholdKey key, CancellationToken cancellationToken)
        => await context.Set<QualityThreshold>()
            .AsNoTracking()
            .SingleOrDefaultAsync(threshold => threshold.Key == key, cancellationToken);

    public async Task SaveAsync(QualityThreshold threshold, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(threshold);

        bool held = await context.Set<QualityThreshold>()
            .AsNoTracking()
            .AnyAsync(row => row.Key == threshold.Key, cancellationToken);

        if (held)
        {
            context.Update(threshold);
        }
        else
        {
            context.Add(threshold);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
