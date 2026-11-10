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

    public async Task<IReadOnlyList<NoticeOnItsWay>> WaitingAsync(CancellationToken cancellationToken)
        => await context.Set<NotificationDelivery>()
            .Where(delivery => delivery.State == NotificationDeliveryState.Queued
                || delivery.State == NotificationDeliveryState.Failed)
            .Join(
                context.Set<Notice>(),
                delivery => delivery.NoticeId,
                notice => notice.Id,
                (delivery, notice) => new { delivery, notice })
            .OrderBy(pair => pair.notice.OccurredAt)
            .ThenBy(pair => pair.delivery.QueuedAt)
            .ThenBy(pair => pair.delivery.Id)
            .Select(pair => new NoticeOnItsWay(pair.delivery, pair.notice))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<NoticeOnItsWay>> LatestOfAsync(
        NotificationDestinationId destination,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        return await context.Set<NotificationDelivery>()
            .AsNoTracking()
            .Where(delivery => delivery.DestinationId == destination)
            .Join(
                context.Set<Notice>().AsNoTracking(),
                delivery => delivery.NoticeId,
                notice => notice.Id,
                (delivery, notice) => new { delivery, notice })
            .OrderByDescending(pair => pair.notice.OccurredAt)
            .ThenByDescending(pair => pair.delivery.QueuedAt)
            .ThenByDescending(pair => pair.delivery.Id)
            .Take(count)
            .Select(pair => new NoticeOnItsWay(pair.delivery, pair.notice))
            .ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        context.Update(delivery);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.Entry(delivery).State = EntityState.Detached;
        }
        catch
        {
            context.Entry(delivery).State = EntityState.Detached;

            throw;
        }
    }
}
