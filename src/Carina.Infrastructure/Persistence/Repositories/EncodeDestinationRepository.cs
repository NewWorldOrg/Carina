using Carina.Domain.Encodings;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class EncodeDestinationRepository(CarinaDbContext context) : IEncodeDestinationRepository
{
    public async Task<EncodeDestination?> FindAsync(EncodeDestinationId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<EncodeDestination>()
            .SingleOrDefaultAsync(destination => destination.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<EncodeDestination>> ListAsync(CancellationToken cancellationToken)
        => await context.Set<EncodeDestination>()
            .OrderBy(destination => destination.DefinedAt)
            .ThenBy(destination => destination.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(EncodeDestination destination, CancellationToken cancellationToken)
    {
        context.Add(destination);

        await context.SaveChangesAsync(cancellationToken);
    }
}
