namespace Carina.Domain.Notifications;

public interface INotificationDestinationRepository
{
    Task<NotificationDestination?> FindAsync(NotificationDestinationId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationDestination>> ListAsync(CancellationToken cancellationToken);

    Task AddAsync(NotificationDestination destination, CancellationToken cancellationToken);

    Task SaveAsync(NotificationDestination destination, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a destination, and with it the deliveries bound for it.
    /// </summary>
    Task RemoveAsync(NotificationDestination destination, CancellationToken cancellationToken);
}
