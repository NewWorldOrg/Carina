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
/// Lists, registers, changes, removes and tries the webhooks notices are sent to. The address of a webhook is sealed
/// before it is kept and is never given back. A webhook removed leaves the sets of rules and the programmes that wait.
/// </summary>
public sealed class NotificationDestinationService(
    INotificationDestinationRepository destinations,
    INotificationRuleRepository rules,
    INotificationDeliveryRepository deliveries,
    IProgrammeWatchRepository watches,
    NotificationDestinationDomainService retiring,
    IAtomicWrite writes,
    WebhookUrlSeal seal,
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

        if (seal.Open(held.SealedUrl) is not { } webhook)
        {
            return Failure<NoticeTrialOutcome>(
                "The address of this destination cannot be opened with the keys this server holds. Write the address again to send to it.",
                NotificationFailure.Unreadable);
        }

        if (!dispatch.HasRoomFor(id) || !trials.Admit(id))
        {
            return Failure<NoticeTrialOutcome>(
                "A trial was sent a moment ago. One destination is tried once a minute, and five trials are sent a minute in all.",
                NotificationFailure.TooSoon);
        }

        byte[] sample = NoticePayload.For(held.Target, NoticeTone.Informative, TrialTitle, TrialBody, links.To(Screen));
        NoticeAnswer answer = await dispatch.SendAsync(id, webhook, sample, cancellationToken);

        return ServiceResult<NoticeTrialOutcome, NotificationFailure>.Success(new NoticeTrialOutcome(answer.IsTaken, answer.Failure));
    }

    private static ServiceResult<T, NotificationFailure> Failure<T>(string message, NotificationFailure failure)
        => ServiceResult<T, NotificationFailure>.Failure(message, failure);

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
        => new(destination, seal.Open(destination.SealedUrl) is not null);

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
