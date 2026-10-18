using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class CatalogCountsReader(CarinaDbContext context) : ICatalogCountsReader
{
    public async Task<CatalogCounts> ReadAsync(CancellationToken cancellationToken)
        => CatalogCounts.Of(await context.Set<CatalogEntry>()
            .AsNoTracking()
            .GroupBy(entry => entry.Kind)
            .Select(group => new { Kind = group.Key, Count = group.Count() })
            .ToDictionaryAsync(counted => counted.Kind, counted => counted.Count, cancellationToken));
}
