using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;

namespace Carina.Api.Services;

public sealed record ListedNotificationDestination(NotificationDestination Destination, bool Readable);

public sealed record NotificationDestinationChange(string? Name, string? Url, bool? Enabled);

public sealed record NoticeTrialOutcome(bool Delivered, NoticeFailure? Failure);

public sealed record NotificationRemoved(Guid Id);

/// <summary>
/// Lists, changes, removes and tries the destinations notices are sent to, and registers the webhooks among them. The
/// address of a webhook and the subscription of a browser are sealed before they are kept and are never given back. A
/// destination removed leaves the sets of rules and the programmes that wait.
/// </summary>
public sealed class NotificationDestinationService(
    INotificationDestinationRepository destinations,
    INotificationRuleRepository rules,
    INotificationDeliveryRepository deliveries,
    IProgrammeWatchRepository watches,
    NotificationDestinationDomainService retiring,
    IAtomicWrite writes,
    WebhookUrlSeal seal,
    PushSubscriptionSeal pushSeal,
    PushMessages messages,
    NoticeDispatch dispatch,
    NoticeTrials trials,
    NoticeLinkBase links,
    TimeProvider clock)
{
    public const string TrialTitle = "テスト送信";

    public const string TrialBody = "この送り先に通知が届くことを確かめるための見本です。";

    private const string Screen = "/settings/notifications";

    private const string BadName = "A destination is named in one to sixty-four characters, none of them a control character.";

    private const string BadAddress =
        "The address is not one a notice can be sent to: an https webhook of Slack or of Discord. It is not repeated here.";

    private const string Unknown = "No destination is registered under that id.";

    private const string NoAddressToChange =
        "A browser has no address to change. It is given another subscription by subscribing again.";

    private const string Unopened =
        "The address of this destination cannot be opened with the keys this server holds. Write the address again to send to it.";

    private const string UnopenedBrowser =
        "This browser cannot be sent to with the keys this server holds. Subscribe from the browser again to send to it.";

    private const string TooSoon =
        "A trial was sent a moment ago. One destination is tried once a minute, and five trials are sent a minute in all.";

    public async Task<ServiceResult<IReadOnlyList<ListedNotificationDestination>>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationDestination> held = await destinations.ListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<ListedNotificationDestination>>.Success([.. held.Select(Listed)]);
    }

    public async Task<ServiceResult<ListedNotificationDestination, NotificationFailure>> RegisterAsync(
        string? name,
        string? url,
        CancellationToken cancellationToken)
    {
        if (NotificationName.Read(name) is not { } named)
        {
            return Failure<ListedNotificationDestination>(BadName, NotificationFailure.Refused);
        }

        if (WebhookUrl.Read(url) is not { } webhook)
        {
            return Failure<ListedNotificationDestination>(BadAddress, NotificationFailure.Refused);
        }

        NotificationDestination destination = NotificationDestination.Register(
            NotificationDestinationId.New(),
            named,
            webhook.Target,
            seal.Seal(webhook),
            webhook.Tail,
            Now());

        await writes.AllOrNothingAsync(
            async inner =>
            {
                await rules.TakeTurnAsync(inner);

                NotificationRule? usual = await retiring.UsualRuleForAsync(destination, inner);

                await destinations.AddAsync(destination, inner);

                if (usual is not null)
                {
                    await rules.AddAsync(usual, inner);
                }

                return true;
            },
            cancellationToken);

        return ServiceResult<ListedNotificationDestination, NotificationFailure>.Success(new ListedNotificationDestination(destination, true));
    }

    public async Task<ServiceResult<ListedNotificationDestination, NotificationFailure>> ReviseAsync(
        NotificationDestinationId id,
        NotificationDestinationChange change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(change);

        if (await destinations.FindAsync(id, cancellationToken) is not { } held)
        {
            return Failure<ListedNotificationDestination>(Unknown, NotificationFailure.NoSuchDestination);
        }

        NotificationName? named = change.Name is null ? held.Name : NotificationName.Read(change.Name);
        bool keepsTheAddress = string.IsNullOrWhiteSpace(change.Url);
        WebhookUrl? webhook = keepsTheAddress ? null : WebhookUrl.Read(change.Url);

        if (named is null)
        {
            return Failure<ListedNotificationDestination>(BadName, NotificationFailure.Refused);
        }

        if (!keepsTheAddress && held.Target is WebhookTarget.Push)
        {
            return Failure<ListedNotificationDestination>(NoAddressToChange, NotificationFailure.Refused);
        }

        if (!keepsTheAddress && webhook is null)
        {
            return Failure<ListedNotificationDestination>(BadAddress, NotificationFailure.Refused);
        }

        DateTime now = Now();

        await writes.AllOrNothingAsync(
            async inner =>
            {
                Change(held, named, webhook, change.Enabled, now);
                await destinations.SaveAsync(held, inner);

                if (change.Enabled is false)
                {
                    await SaveAllAsync(await retiring.GiveUpWhatWaitsForAsync(id, inner), inner);
                }

                return true;
            },
            cancellationToken);

        if (webhook is not null)
        {
            dispatch.Forget(id);
        }

        return ServiceResult<ListedNotificationDestination, NotificationFailure>.Success(Listed(held));
    }

    public async Task<ServiceResult<NotificationRemoved, NotificationFailure>> RemoveAsync(
        NotificationDestinationId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await destinations.FindAsync(id, cancellationToken) is not { } held)
        {
            return Failure<NotificationRemoved>(Unknown, NotificationFailure.NoSuchDestination);
        }

        DateTime now = Now();

        await writes.AllOrNothingAsync(
            async inner =>
            {
                await rules.TakeTurnAsync(inner);

                await KeepAsync(await retiring.RetireAsync(id, now, inner), inner);
                await destinations.RemoveAsync(held, inner);

                return true;
            },
            cancellationToken);

        dispatch.Forget(id);

        return ServiceResult<NotificationRemoved, NotificationFailure>.Success(new NotificationRemoved(id.Value));
    }

    public async Task<ServiceResult<NoticeTrialOutcome, NotificationFailure>> TryAsync(
        NotificationDestinationId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await destinations.FindAsync(id, cancellationToken) is not { } held)
        {
            return Failure<NoticeTrialOutcome>(Unknown, NotificationFailure.NoSuchDestination);
        }

        return held.Target is WebhookTarget.Push
            ? await TryBrowserAsync(held, cancellationToken)
            : await TryWebhookAsync(held, cancellationToken);
    }

    private static ServiceResult<T, NotificationFailure> Failure<T>(string message, NotificationFailure failure)
        => ServiceResult<T, NotificationFailure>.Failure(message, failure);

    private async Task<ServiceResult<NoticeTrialOutcome, NotificationFailure>> TryWebhookAsync(
        NotificationDestination held,
        CancellationToken cancellationToken)
    {
        if (seal.Open(held.SealedUrl) is not { } webhook)
        {
            return Failure<NoticeTrialOutcome>(Unopened, NotificationFailure.Unreadable);
        }

        if (!Admits(held.Id))
        {
            return Failure<NoticeTrialOutcome>(TooSoon, NotificationFailure.TooSoon);
        }

        byte[] sample = NoticePayload.For(held.Target, NoticeTone.Informative, TrialTitle, TrialBody, links.To(Screen));

        return Tried(await dispatch.SendAsync(held.Id, webhook, sample, cancellationToken));
    }

    private async Task<ServiceResult<NoticeTrialOutcome, NotificationFailure>> TryBrowserAsync(
        NotificationDestination held,
        CancellationToken cancellationToken)
    {
        if (pushSeal.Open(held.SealedUrl) is not { } subscription || !messages.CanSign())
        {
            return Failure<NoticeTrialOutcome>(UnopenedBrowser, NotificationFailure.Unreadable);
        }

        if (!Admits(held.Id))
        {
            return Failure<NoticeTrialOutcome>(TooSoon, NotificationFailure.TooSoon);
        }

        PushMessage sample = messages.Trying(subscription, TrialTitle, TrialBody, Screen);

        return Tried(await dispatch.SendAsync(held.Id, subscription, sample, cancellationToken));
    }

    private static ServiceResult<NoticeTrialOutcome, NotificationFailure> Tried(NoticeAnswer answer)
        => ServiceResult<NoticeTrialOutcome, NotificationFailure>.Success(new NoticeTrialOutcome(answer.IsTaken, answer.Failure));

    private bool Admits(NotificationDestinationId id) => dispatch.HasRoomFor(id) && trials.Admit(id);

    private void Change(NotificationDestination held, NotificationName named, WebhookUrl? webhook, bool? enabled, DateTime now)
    {
        if (!named.Equals(held.Name))
        {
            held.Rename(named, now);
        }

        if (webhook is not null)
        {
            held.PointAt(webhook.Target, seal.Seal(webhook), webhook.Tail, now);
        }

        if (enabled is true && !held.Enabled)
        {
            held.Enable(now);
        }

        if (enabled is false && held.Enabled)
        {
            held.Disable(now);
        }
    }

    private async Task SaveAllAsync(IReadOnlyList<NotificationDelivery> givenUp, CancellationToken cancellationToken)
    {
        foreach (NotificationDelivery delivery in givenUp)
        {
            await deliveries.SaveAsync(delivery, cancellationToken);
        }
    }

    private async Task KeepAsync(NotificationDestinationRetirement retired, CancellationToken cancellationToken)
    {
        foreach (NotificationRule rule in retired.Emptied)
        {
            await rules.RemoveAsync(rule, cancellationToken);
        }

        foreach (NotificationRule rule in retired.Narrowed)
        {
            await rules.SaveAsync(rule, cancellationToken);
        }

        foreach (ProgrammeWatch watch in retired.NoLongerTelling)
        {
            await watches.SaveAsync(watch, cancellationToken);
        }
    }

    private ListedNotificationDestination Listed(NotificationDestination destination)
        => new(
            destination,
            destination.Target is WebhookTarget.Push
                ? pushSeal.Open(destination.SealedUrl) is not null
                : seal.Open(destination.SealedUrl) is not null);

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
