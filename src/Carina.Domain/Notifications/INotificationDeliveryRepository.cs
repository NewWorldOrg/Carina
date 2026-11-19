namespace Carina.Domain.Notifications;

public sealed record NoticeOnItsWay(NotificationDelivery Delivery, Notice Notice);

public interface INotificationDeliveryRepository
{
    Task<NotificationDelivery?> FindAsync(NotificationDeliveryId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationDelivery>> ListOfAsync(NoticeId notice, CancellationToken cancellationToken);

    Task<IReadOnlyList<NoticeOnItsWay>> WaitingAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<NoticeOnItsWay>> LatestOfAsync(
        NotificationDestinationId destination,
        int count,
        CancellationToken cancellationToken);

    Task SaveAsync(NotificationDelivery delivery, CancellationToken cancellationToken);
}
