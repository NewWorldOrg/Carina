using Carina.Domain.Notifications;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class NotificationDestinationRepository(CarinaDbContext context) : INotificationDestinationRepository
{
    public async Task<NotificationDestination?> FindAsync(
        NotificationDestinationId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<NotificationDestination>()
            .SingleOrDefaultAsync(destination => destination.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationDestination>> ListAsync(CancellationToken cancellationToken)
        => await context.Set<NotificationDestination>()
            .OrderBy(destination => destination.CreatedAt)
            .ThenBy(destination => destination.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(NotificationDestination destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        context.Add(destination);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(NotificationDestination destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        context.Update(destination);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(NotificationDestination destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        context.Remove(destination);

        await context.SaveChangesAsync(cancellationToken);
    }
}
