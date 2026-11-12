namespace Carina.Domain.Notifications;

/// <summary>
/// What removing a destination takes out of the rest: the sets of rules left with no destination, to be removed; the
/// sets of rules that send to fewer destinations; and the programmes that wait and are no longer told to it.
/// </summary>
public sealed record NotificationDestinationRetirement(
    IReadOnlyList<NotificationRule> Emptied,
    IReadOnlyList<NotificationRule> Narrowed,
    IReadOnlyList<ProgrammeWatch> NoLongerTelling);

/// <summary>
/// Decides what a destination registered, turned off or removed means for the sets of rules, the deliveries that wait
/// and the programmes asked to be told of, looking each of them up.
/// </summary>
public sealed class NotificationDestinationDomainService(
    INotificationDestinationRepository destinations,
    INotificationRuleRepository rules,
    INotificationDeliveryRepository deliveries,
    IProgrammeWatchRepository watches)
{
    /// <summary>
    /// The set of rules made with the first destination, when no destination and no set of rules is held yet;
    /// otherwise nothing. It is asked before the destination is added.
    /// </summary>
    public async Task<NotificationRule?> UsualRuleForAsync(NotificationDestination registered, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registered);

        IReadOnlyList<NotificationDestination> heldDestinations = await destinations.ListAsync(cancellationToken);
        IReadOnlyList<NotificationRule> heldRules = await rules.ListAsync(cancellationToken);

        return heldDestinations.Count is 0 && heldRules.Count is 0
            ? NotificationRule.Usual(NotificationRuleId.New(), registered.Id, registered.CreatedAt)
            : null;
    }

    /// <summary>
    /// Gives up every delivery that waits for a destination turned off, and hands them back to be written.
    /// </summary>
    public async Task<IReadOnlyList<NotificationDelivery>> GiveUpWhatWaitsForAsync(
        NotificationDestinationId destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        IReadOnlyList<NoticeOnItsWay> waiting = await deliveries.WaitingAsync(cancellationToken);
        List<NotificationDelivery> givenUp = [];

        foreach (NoticeOnItsWay row in waiting.Where(row => row.Delivery.DestinationId.Equals(destination)))
        {
            row.Delivery.GiveUp(NoticeFailure.DestinationDisabled);
            givenUp.Add(row.Delivery);
        }

        return givenUp;
    }

    /// <summary>
    /// Takes a destination being removed out of every set of rules and every programme that waits, at
    /// <paramref name="at"/>, and hands back what changed to be written.
    /// </summary>
    public async Task<NotificationDestinationRetirement> RetireAsync(
        NotificationDestinationId destination,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        List<NotificationRule> emptied = [];
        List<NotificationRule> narrowed = [];

        foreach (NotificationRule rule in await rules.ListAsync(cancellationToken))
        {
            NotificationRuleNarrowing narrowing = rule.StopSendingTo(destination, at);

            if (narrowing is NotificationRuleNarrowing.LeftWithNone)
            {
                emptied.Add(rule);
            }

            if (narrowing is NotificationRuleNarrowing.Narrowed)
            {
                narrowed.Add(rule);
            }
        }

        IReadOnlyList<ProgrammeWatch> followed = await watches.ListFollowedAsync(at, cancellationToken);
        List<ProgrammeWatch> noLongerTelling = [.. followed.Where(watch => watch.StopTelling(destination, at))];

        return new NotificationDestinationRetirement(emptied, narrowed, noLongerTelling);
    }
}
