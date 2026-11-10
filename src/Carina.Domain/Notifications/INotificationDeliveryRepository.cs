namespace Carina.Domain.Notifications;

/// <summary>
/// A delivery together with the notice it carries.
/// </summary>
public sealed record NoticeOnItsWay(NotificationDelivery Delivery, Notice Notice);

public interface INotificationDeliveryRepository
{
    Task<NotificationDelivery?> FindAsync(NotificationDeliveryId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationDelivery>> ListOfAsync(NoticeId notice, CancellationToken cancellationToken);

    /// <summary>
    /// Every delivery not yet sent or given up, in the order the notices happened.
    /// </summary>
    Task<IReadOnlyList<NoticeOnItsWay>> WaitingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The latest deliveries to one destination, the newest notice first.
    /// </summary>
    Task<IReadOnlyList<NoticeOnItsWay>> LatestOfAsync(
        NotificationDestinationId destination,
        int count,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes a delivery where it now stands. A delivery that is no longer held is left gone.
    /// </summary>
    Task SaveAsync(NotificationDelivery delivery, CancellationToken cancellationToken);
}
