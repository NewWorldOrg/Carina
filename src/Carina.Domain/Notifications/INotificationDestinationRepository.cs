namespace Carina.Domain.Notifications;

public interface INotificationDestinationRepository
{
    Task<NotificationDestination?> FindAsync(NotificationDestinationId id, CancellationToken cancellationToken);

    Task<NotificationDestination?> FindByEndpointHashAsync(string endpointHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationDestination>> ListAsync(CancellationToken cancellationToken);

    Task AddAsync(NotificationDestination destination, CancellationToken cancellationToken);

    Task SaveAsync(NotificationDestination destination, CancellationToken cancellationToken);

    Task KeepResultAsync(NotificationDestination destination, CancellationToken cancellationToken);

    Task RemoveAsync(NotificationDestination destination, CancellationToken cancellationToken);
}
