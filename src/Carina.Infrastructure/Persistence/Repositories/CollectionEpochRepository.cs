using Carina.Domain.Programmes;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class CollectionEpochRepository(CarinaDbContext context) : ICollectionEpochRepository
{
    public async Task<CollectionEpoch> ReadAsync(DateTime at, CancellationToken cancellationToken)
    {
        CollectionEpoch? held = await context.Set<CollectionEpoch>()
            .SingleOrDefaultAsync(epoch => epoch.Id == CollectionEpoch.TheOnlyRow, cancellationToken);

        if (held is not null)
        {
            return held;
        }

        CollectionEpoch begun = CollectionEpoch.Begin(at);

        await context.Set<CollectionEpoch>().AddAsync(begun, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return begun;
    }

    public async Task SaveAsync(CollectionEpoch epoch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(epoch);

        if (context.Entry(epoch).State is EntityState.Detached)
        {
            context.Set<CollectionEpoch>().Update(epoch);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
