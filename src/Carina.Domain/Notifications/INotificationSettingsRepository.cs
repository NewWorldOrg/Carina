namespace Carina.Domain.Notifications;

public interface INotificationSettingsRepository
{
    Task<NotificationSettings?> ReadAsync(CancellationToken cancellationToken);

    Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken);
}
