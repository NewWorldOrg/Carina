using Carina.Domain.Notifications;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class NotificationRuleRepository(CarinaDbContext context) : INotificationRuleRepository
{
    public async Task<NotificationRule?> FindAsync(
        NotificationRuleId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<NotificationRule>()
            .SingleOrDefaultAsync(rule => rule.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationRule>> ListAsync(CancellationToken cancellationToken)
        => await context.Set<NotificationRule>()
            .OrderBy(rule => rule.CreatedAt)
            .ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(NotificationRule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);

        context.Add(rule);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(NotificationRule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);

        context.Update(rule);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(NotificationRule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);

        context.Remove(rule);

        await context.SaveChangesAsync(cancellationToken);
    }
}
