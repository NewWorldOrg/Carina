namespace Carina.Domain.Notifications;

public sealed record NotificationDestinationRetirement(
    IReadOnlyList<NotificationRule> Emptied,
    IReadOnlyList<NotificationRule> Narrowed,
    IReadOnlyList<ProgrammeWatch> NoLongerTelling);

public sealed record PushEnrolment(NotificationDestination Destination, bool Added, NotificationRule? Usual);

public enum PushReplacementRefusal
{
    NoSuchBrowser = 1,

    HeldByAnother = 2,
}

public sealed record PushReplacement(NotificationDestination? Destination, PushReplacementRefusal? Refusal);

public sealed class NotificationDestinationDomainService(
    INotificationDestinationRepository destinations,
    INotificationRuleRepository rules,
    INotificationDeliveryRepository deliveries,
    IProgrammeWatchRepository watches)
{
    public async Task<NotificationRule?> UsualRuleForAsync(NotificationDestination registered, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registered);

        IReadOnlyList<NotificationDestination> heldDestinations = await destinations.ListAsync(cancellationToken);
        IReadOnlyList<NotificationRule> heldRules = await rules.ListAsync(cancellationToken);

        return heldDestinations.Count is 0 && heldRules.Count is 0
            ? NotificationRule.Usual(NotificationRuleId.New(), registered.Id, registered.CreatedAt)
            : null;
    }

    public async Task<PushEnrolment> EnrolAsync(
        NotificationName name,
        PushSubscription subscription,
        byte[] sealedSubscription,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(subscription);

        NotificationDestination? held = await destinations.FindByEndpointHashAsync(subscription.Fingerprint, cancellationToken);

        if (held is not null)
        {
            held.Resubscribe(subscription, sealedSubscription, at);

            return new PushEnrolment(held, false, null);
        }

        NotificationDestination added = NotificationDestination.Subscribe(
            NotificationDestinationId.New(),
            name,
            subscription,
            sealedSubscription,
            at);

        return new PushEnrolment(added, true, await UsualRuleForAsync(added, cancellationToken));
    }

    public async Task<PushReplacement> ReplaceAsync(
        NotificationDestinationId id,
        PushSubscription subscription,
        byte[] sealedSubscription,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(subscription);

        NotificationDestination? held = await destinations.FindAsync(id, cancellationToken);

        if (held is not { Target: WebhookTarget.Push })
        {
            return new PushReplacement(null, PushReplacementRefusal.NoSuchBrowser);
        }

        NotificationDestination? other = await destinations.FindByEndpointHashAsync(subscription.Fingerprint, cancellationToken);

        if (other is not null && !other.Id.Equals(id))
        {
            return new PushReplacement(null, PushReplacementRefusal.HeldByAnother);
        }

        held.Resubscribe(subscription, sealedSubscription, at);

        return new PushReplacement(held, null);
    }

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
