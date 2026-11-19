namespace Carina.Domain.Notifications;

public sealed class NotificationRuleDomainService(
    INotificationRuleRepository rules,
    INotificationDestinationRepository destinations)
{
    public async Task<IReadOnlyList<NotificationDestinationId>?> SentToAsync(
        IReadOnlyList<Guid>? named,
        CancellationToken cancellationToken)
    {
        if (named is null || named.Count is 0)
        {
            return null;
        }

        IReadOnlyList<NotificationDestination> held = await destinations.ListAsync(cancellationToken);
        HashSet<Guid> registered = [.. held.Select(destination => destination.Id.Value)];

        return named.All(registered.Contains) ? [.. named.Distinct().Select(id => new NotificationDestinationId(id))] : null;
    }

    public async Task<bool> HasRoomForAnotherAsync(CancellationToken cancellationToken)
        => (await rules.ListAsync(cancellationToken)).Count < NotificationRule.MostRules;
}
