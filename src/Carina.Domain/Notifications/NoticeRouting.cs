namespace Carina.Domain.Notifications;

public static class NoticeRouting
{
    public static IReadOnlyList<NotificationDestinationId> DestinationsFor(
        NoticeKind kind,
        IReadOnlyList<NotificationRule> rules,
        IReadOnlyList<NotificationDestination> destinations)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(destinations);

        HashSet<NotificationDestinationId> named =
        [
            .. rules.Where(rule => rule.Covers(kind)).SelectMany(rule => rule.Destinations),
        ];

        return
        [
            .. destinations
                .Where(destination => destination.Enabled && named.Contains(destination.Id))
                .Select(destination => destination.Id)
                .Distinct(),
        ];
    }

    public static IReadOnlyList<NotificationDestinationId> DestinationsFor(
        ProgrammeWatch watch,
        IReadOnlyList<NotificationDestination> destinations)
    {
        ArgumentNullException.ThrowIfNull(watch);
        ArgumentNullException.ThrowIfNull(destinations);

        return
        [
            .. destinations
                .Where(destination => destination.Enabled && watch.Destinations.Contains(destination.Id))
                .Select(destination => destination.Id)
                .Distinct(),
        ];
    }
}
