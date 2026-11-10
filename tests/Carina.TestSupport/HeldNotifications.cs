using Carina.Domain.Notifications;

namespace Carina.TestSupport;

public sealed class HeldNotificationDestinations : INotificationDestinationRepository
{
    public List<NotificationDestination> Destinations { get; } = [];

    public Task<NotificationDestination?> FindAsync(NotificationDestinationId id, CancellationToken cancellationToken)
        => Task.FromResult(Destinations.FirstOrDefault(destination => destination.Id.Equals(id)));

    public Task<IReadOnlyList<NotificationDestination>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationDestination> listed = [.. Destinations.OrderBy(destination => destination.CreatedAt)];

        return Task.FromResult(listed);
    }

    public Task AddAsync(NotificationDestination destination, CancellationToken cancellationToken)
    {
        Destinations.Add(destination);

        return Task.CompletedTask;
    }

    public Task SaveAsync(NotificationDestination destination, CancellationToken cancellationToken)
    {
        if (!Destinations.Contains(destination))
        {
            Destinations.Add(destination);
        }

        return Task.CompletedTask;
    }

    public Task KeepResultAsync(NotificationDestination destination, CancellationToken cancellationToken)
        => SaveAsync(destination, cancellationToken);

    public Task RemoveAsync(NotificationDestination destination, CancellationToken cancellationToken)
    {
        Destinations.Remove(destination);

        return Task.CompletedTask;
    }
}

public sealed class HeldNotificationRules : INotificationRuleRepository
{
    public List<NotificationRule> Rules { get; } = [];

    public Task<NotificationRule?> FindAsync(NotificationRuleId id, CancellationToken cancellationToken)
        => Task.FromResult(Rules.FirstOrDefault(rule => rule.Id.Equals(id)));

    public Task<IReadOnlyList<NotificationRule>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationRule> listed = [.. Rules.OrderBy(rule => rule.CreatedAt)];

        return Task.FromResult(listed);
    }

    public Task TakeTurnAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AddAsync(NotificationRule rule, CancellationToken cancellationToken)
    {
        Rules.Add(rule);

        return Task.CompletedTask;
    }

    public Task SaveAsync(NotificationRule rule, CancellationToken cancellationToken)
    {
        if (!Rules.Contains(rule))
        {
            Rules.Add(rule);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(NotificationRule rule, CancellationToken cancellationToken)
    {
        Rules.Remove(rule);

        return Task.CompletedTask;
    }
}

public sealed class HeldNotificationDeliveries : INotificationDeliveryRepository
{
    public List<NoticeOnItsWay> Rows { get; } = [];

    public Task<NotificationDelivery?> FindAsync(NotificationDeliveryId id, CancellationToken cancellationToken)
        => Task.FromResult(Rows.Select(row => row.Delivery).FirstOrDefault(delivery => delivery.Id.Equals(id)));

    public Task<IReadOnlyList<NotificationDelivery>> ListOfAsync(NoticeId notice, CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationDelivery> listed =
            [.. Rows.Select(row => row.Delivery).Where(delivery => delivery.NoticeId.Equals(notice))];

        return Task.FromResult(listed);
    }

    public Task<IReadOnlyList<NoticeOnItsWay>> WaitingAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<NoticeOnItsWay> waiting =
            [.. Rows.Where(row => row.Delivery.IsWaiting).OrderBy(row => row.Notice.OccurredAt)];

        return Task.FromResult(waiting);
    }

    public Task<IReadOnlyList<NoticeOnItsWay>> LatestOfAsync(
        NotificationDestinationId destination,
        int count,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NoticeOnItsWay> latest =
        [
            .. Rows
                .Where(row => row.Delivery.DestinationId.Equals(destination))
                .OrderByDescending(row => row.Notice.OccurredAt)
                .Take(count),
        ];

        return Task.FromResult(latest);
    }

    public Task SaveAsync(NotificationDelivery delivery, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class HeldNotificationSettings : INotificationSettingsRepository
{
    public NotificationSettings? Settings { get; set; }

    public Task<NotificationSettings?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);

    public Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken)
    {
        Settings = settings;

        return Task.CompletedTask;
    }
}
