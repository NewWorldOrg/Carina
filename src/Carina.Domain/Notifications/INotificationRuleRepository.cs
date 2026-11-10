namespace Carina.Domain.Notifications;

public interface INotificationRuleRepository
{
    Task<NotificationRule?> FindAsync(NotificationRuleId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationRule>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Waits until no other writer of the destinations and the sets of rules is underway, and keeps the others waiting
    /// until the transaction this is called in ends.
    /// </summary>
    Task TakeTurnAsync(CancellationToken cancellationToken);

    Task AddAsync(NotificationRule rule, CancellationToken cancellationToken);

    Task SaveAsync(NotificationRule rule, CancellationToken cancellationToken);

    Task RemoveAsync(NotificationRule rule, CancellationToken cancellationToken);
}
