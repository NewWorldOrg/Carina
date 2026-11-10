namespace Carina.Domain.Notifications;

public interface INotificationDestinationRepository
{
    Task<NotificationDestination?> FindAsync(NotificationDestinationId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationDestination>> ListAsync(CancellationToken cancellationToken);

    Task AddAsync(NotificationDestination destination, CancellationToken cancellationToken);

    Task SaveAsync(NotificationDestination destination, CancellationToken cancellationToken);

    /// <summary>
    /// Writes what the last attempt came to and nothing else, turning the destination off where the attempt did. A
    /// destination somebody else turned off in the meantime stays off.
    /// </summary>
    Task KeepResultAsync(NotificationDestination destination, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a destination, and with it the deliveries bound for it.
    /// </summary>
    Task RemoveAsync(NotificationDestination destination, CancellationToken cancellationToken);
}
