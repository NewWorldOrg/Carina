using Carina.Domain.Channels;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class BroadcastServiceRepository(CarinaDbContext context) : IBroadcastServiceRepository
{
    public async Task<BroadcastService?> FindAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);

        return await context.Set<BroadcastService>()
            .FirstOrDefaultAsync(
                service => service.NetworkId == networkId && service.ServiceId == serviceId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<BroadcastService>> ListAsync(CancellationToken cancellationToken)
        => await context.Set<BroadcastService>()
            .OrderBy(service => service.NetworkId)
            .ThenBy(service => service.ServiceId)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(BroadcastService service, CancellationToken cancellationToken)
    {
        context.Add(service);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(BroadcastService service, CancellationToken cancellationToken)
    {
        context.Update(service);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);

        return await context.Set<BroadcastService>()
            .Where(service => service.NetworkId == networkId && service.ServiceId == serviceId)
            .ExecuteDeleteAsync(cancellationToken) > 0;
    }
}
