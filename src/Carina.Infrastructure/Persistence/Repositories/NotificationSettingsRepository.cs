using Carina.Domain.Notifications;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class NotificationSettingsRepository(CarinaDbContext context) : INotificationSettingsRepository
{
    public async Task<NotificationSettings?> ReadAsync(CancellationToken cancellationToken)
        => await context.Set<NotificationSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == NotificationSettings.TheOnlyRow, cancellationToken);

    /// <summary>
    /// Inserts the one row, or writes the lead time and when it changed over the row held, in a single statement.
    /// </summary>
    public async Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO notification_settings (id, notify_lead_minutes, changed_at)
            VALUES ({settings.Id}, {settings.NotifyLeadMinutes}, {settings.ChangedAt})
            ON CONFLICT (id) DO UPDATE SET
                notify_lead_minutes = excluded.notify_lead_minutes,
                changed_at = excluded.changed_at
            """,
            cancellationToken);
    }
}
