namespace Carina.Domain.Notifications;

public interface INotificationDeliveryRepository
{
    Task<NotificationDelivery?> FindAsync(NotificationDeliveryId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationDelivery>> ListOfAsync(NoticeId notice, CancellationToken cancellationToken);

    Task SaveAsync(NotificationDelivery delivery, CancellationToken cancellationToken);
}
