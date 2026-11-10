using Carina.Domain.Notifications;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class NotificationDeliveryRepository(CarinaDbContext context) : INotificationDeliveryRepository
{
    public async Task<NotificationDelivery?> FindAsync(NotificationDeliveryId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<NotificationDelivery>()
            .SingleOrDefaultAsync(delivery => delivery.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationDelivery>> ListOfAsync(NoticeId notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return await context.Set<NotificationDelivery>()
            .Where(delivery => delivery.NoticeId == notice)
            .OrderBy(delivery => delivery.QueuedAt)
            .ThenBy(delivery => delivery.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        context.Update(delivery);

        await context.SaveChangesAsync(cancellationToken);
    }
}
