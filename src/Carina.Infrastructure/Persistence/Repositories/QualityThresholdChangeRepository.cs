using Carina.Domain.Quality;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class QualityThresholdChangeRepository(CarinaDbContext context) : IQualityThresholdChangeRepository
{
    public async Task AddAsync(QualityThresholdChange change, CancellationToken cancellationToken)
    {
        context.Add(change);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QualityThresholdChange>> ListAsync(CancellationToken cancellationToken)
        => await context.Set<QualityThresholdChange>()
            .AsNoTracking()
            .OrderByDescending(change => change.ChangedAt)
            .ThenBy(change => change.Id)
            .ToListAsync(cancellationToken);
}
