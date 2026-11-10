namespace Carina.Domain.Notifications;

public interface INotificationRuleRepository
{
    Task<NotificationRule?> FindAsync(NotificationRuleId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationRule>> ListAsync(CancellationToken cancellationToken);

    Task AddAsync(NotificationRule rule, CancellationToken cancellationToken);

    Task SaveAsync(NotificationRule rule, CancellationToken cancellationToken);

    Task RemoveAsync(NotificationRule rule, CancellationToken cancellationToken);
}
