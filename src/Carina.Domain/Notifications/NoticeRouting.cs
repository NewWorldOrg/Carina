namespace Carina.Domain.Notifications;

public static class NoticeRouting
{
    /// <summary>
    /// The destinations a notice of <paramref name="kind"/> goes to: each one that is turned on and that some set of
    /// rules turned on names for that kind, once, in the order the destinations are handed over.
    /// </summary>
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

    /// <summary>
    /// The destinations a notice of a programme asked to be told of goes to: each one it names that is turned on, once,
    /// in the order the destinations are handed over. The sets of rules play no part.
    /// </summary>
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
