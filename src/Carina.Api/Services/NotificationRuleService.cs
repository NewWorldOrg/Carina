using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Notifications;

namespace Carina.Api.Services;

public sealed record NotificationRuleChange(
    string? Name,
    bool? Enabled,
    IReadOnlyList<NoticeKind>? Events,
    IReadOnlyList<Guid>? Destinations);

/// <summary>
/// Lists, makes, changes and removes the sets of rules that say which kinds of notice go to which destinations.
/// </summary>
public sealed class NotificationRuleService(
    INotificationRuleRepository rules,
    NotificationRuleDomainService defining,
    IAtomicWrite writes,
    TimeProvider clock)
{
    private const string BadName = "A set of rules is named in one to sixty-four characters, none of them a control character.";

    private const string BadEvents = "A set of rules lists the kinds of notice it tells of, each a kind the ledger holds. The list may be empty.";

    private const string BadDestinations = "A set of rules sends to at least one destination, each of them registered.";

    private const string Unknown = "No set of rules is held under that id.";

    private static readonly string TooMany = $"At most {NotificationRule.MostRules} sets of rules are held. Remove one to make another.";

    public async Task<ServiceResult<IReadOnlyList<NotificationRule>>> ListAsync(CancellationToken cancellationToken)
        => ServiceResult<IReadOnlyList<NotificationRule>>.Success(await rules.ListAsync(cancellationToken));

    public async Task<ServiceResult<NotificationRule, NotificationFailure>> DefineAsync(
        NotificationRuleChange asked,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asked);

        if (NotificationName.Read(asked.Name) is not { } named)
        {
            return Failure<NotificationRule>(BadName, NotificationFailure.Refused);
        }

        if (Kinds(asked.Events) is not { } events)
        {
            return Failure<NotificationRule>(BadEvents, NotificationFailure.Refused);
        }

        if (await defining.SentToAsync(asked.Destinations, cancellationToken) is not { } sentTo)
        {
            return Failure<NotificationRule>(BadDestinations, NotificationFailure.Refused);
        }

        DateTime now = Now();
        NotificationRule rule = NotificationRule.Define(NotificationRuleId.New(), named, events, sentTo, now);

        if (asked.Enabled is false)
        {
            rule.Disable(now);
        }

        bool kept = await writes.AllOrNothingAsync(
            async inner =>
            {
                await rules.TakeTurnAsync(inner);

                if (!await defining.HasRoomForAnotherAsync(inner))
                {
                    return false;
                }

                await rules.AddAsync(rule, inner);

                return true;
            },
            cancellationToken);

        return kept
            ? ServiceResult<NotificationRule, NotificationFailure>.Success(rule)
            : Failure<NotificationRule>(TooMany, NotificationFailure.TooManyRules);
    }

    public async Task<ServiceResult<NotificationRule, NotificationFailure>> ReviseAsync(
        NotificationRuleId id,
        NotificationRuleChange change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(change);

        if (await rules.FindAsync(id, cancellationToken) is not { } held)
        {
            return Failure<NotificationRule>(Unknown, NotificationFailure.NoSuchRule);
        }

        NotificationName? named = change.Name is null ? held.Name : NotificationName.Read(change.Name);
        IReadOnlyList<NoticeKind>? events = change.Events is null ? held.Events : Kinds(change.Events);
        IReadOnlyList<NotificationDestinationId>? sentTo = change.Destinations is null
            ? held.Destinations
            : await defining.SentToAsync(change.Destinations, cancellationToken);

        if (named is null)
        {
            return Failure<NotificationRule>(BadName, NotificationFailure.Refused);
        }

        if (events is null)
        {
            return Failure<NotificationRule>(BadEvents, NotificationFailure.Refused);
        }

        if (sentTo is null)
        {
            return Failure<NotificationRule>(BadDestinations, NotificationFailure.Refused);
        }

        Change(held, named, change, events, sentTo, Now());
        await rules.SaveAsync(held, cancellationToken);

        return ServiceResult<NotificationRule, NotificationFailure>.Success(held);
    }

    public async Task<ServiceResult<NotificationRemoved, NotificationFailure>> RemoveAsync(
        NotificationRuleId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await rules.FindAsync(id, cancellationToken) is not { } held)
        {
            return Failure<NotificationRemoved>(Unknown, NotificationFailure.NoSuchRule);
        }

        await rules.RemoveAsync(held, cancellationToken);

        return ServiceResult<NotificationRemoved, NotificationFailure>.Success(new NotificationRemoved(id.Value));
    }

    private static ServiceResult<T, NotificationFailure> Failure<T>(string message, NotificationFailure failure)
        => ServiceResult<T, NotificationFailure>.Failure(message, failure);

    private static IReadOnlyList<NoticeKind>? Kinds(IReadOnlyList<NoticeKind>? asked)
        => asked is not null && asked.All(NoticeKinds.ChosenByRules.Contains) ? asked : null;

    private static void Change(
        NotificationRule held,
        NotificationName named,
        NotificationRuleChange change,
        IReadOnlyList<NoticeKind> events,
        IReadOnlyList<NotificationDestinationId> sentTo,
        DateTime now)
    {
        if (!named.Equals(held.Name))
        {
            held.Rename(named, now);
        }

        if (change.Events is not null || change.Destinations is not null)
        {
            held.Tell(events, sentTo, now);
        }

        if (change.Enabled is true && !held.Enabled)
        {
            held.Enable(now);
        }

        if (change.Enabled is false && held.Enabled)
        {
            held.Disable(now);
        }
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
