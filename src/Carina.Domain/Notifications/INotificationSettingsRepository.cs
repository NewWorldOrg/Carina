namespace Carina.Domain.Notifications;

public interface INotificationSettingsRepository
{
    /// <summary>
    /// Reads the one row, or nothing while nobody has changed the settings.
    /// </summary>
    Task<NotificationSettings?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the one row as it is handed over, in place of the row held where there is one.
    /// </summary>
    Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken);
}
