using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;

namespace Carina.Api.Services;

public sealed record PushKeyHeld(string PublicKey);

public sealed record PushEnrolled(ListedNotificationDestination Destination, bool Added);

/// <summary>
/// Gives the key browsers subscribe with, and registers and replaces what a browser subscribed. A subscription is
/// sealed before it is kept and is never given back.
/// </summary>
public sealed class NotificationPushService(
    INotificationDestinationRepository destinations,
    INotificationRuleRepository rules,
    NotificationDestinationDomainService enrolling,
    IAtomicWrite writes,
    PushSubscriptionSeal seal,
    PushMessages messages,
    NoticeDispatch dispatch,
    NoticeLinkBase links,
    TimeProvider clock)
{
    private const string BadName = "A destination is named in one to sixty-four characters, none of them a control character.";

    private const string BadSubscription =
        "The subscription is not one a notice can be sent to: an https endpoint of a push service this server knows, "
        + "a P-256 public key and a sixteen-byte secret, both in base64url. It is not repeated here.";

    private const string NoKey =
        "The key this server signs with for browsers cannot be read. It is left as it is, and nothing is sent to a browser until it can be read.";

    private const string NoPublicOrigin =
        "This server does not know the address its screens are reached at, so a notice could not open them. Name the public origin first.";

    private const string Unknown = "No browser is registered under that id.";

    private const string HeldByAnother = "Another destination already holds that subscription.";

    public ServiceResult<PushKeyHeld, NotificationFailure> Key()
        => messages.PublishedKey() is { } published
            ? ServiceResult<PushKeyHeld, NotificationFailure>.Success(new PushKeyHeld(published))
            : Failure<PushKeyHeld>(NoKey, NotificationFailure.Unreadable);

    public async Task<ServiceResult<PushEnrolled, NotificationFailure>> SubscribeAsync(
        string? name,
        string? endpoint,
        string? key,
        string? secret,
        CancellationToken cancellationToken)
    {
        if (NotificationName.Read(name) is not { } named)
        {
            return Failure<PushEnrolled>(BadName, NotificationFailure.Refused);
        }

        if (PushSubscription.Read(endpoint, key, secret) is not { } subscription)
        {
            return Failure<PushEnrolled>(BadSubscription, NotificationFailure.Refused);
        }

        if (!links.IsKnown)
        {
            return Failure<PushEnrolled>(NoPublicOrigin, NotificationFailure.NoPublicOrigin);
        }

        byte[] sealedSubscription = seal.Seal(subscription);
        DateTime now = Now();

        PushEnrolment enrolment = await writes.AllOrNothingAsync(
            async inner =>
            {
                await rules.TakeTurnAsync(inner);

                PushEnrolment made = await enrolling.EnrolAsync(named, subscription, sealedSubscription, now, inner);

                await KeepAsync(made, inner);

                return made;
            },
            cancellationToken);

        if (!enrolment.Added)
        {
            dispatch.Forget(enrolment.Destination.Id);
        }

        return ServiceResult<PushEnrolled, NotificationFailure>.Success(
            new PushEnrolled(new ListedNotificationDestination(enrolment.Destination, true), enrolment.Added));
    }

    public async Task<ServiceResult<ListedNotificationDestination, NotificationFailure>> ResubscribeAsync(
        NotificationDestinationId id,
        string? endpoint,
        string? key,
        string? secret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (PushSubscription.Read(endpoint, key, secret) is not { } subscription)
        {
            return Failure<ListedNotificationDestination>(BadSubscription, NotificationFailure.Refused);
        }

        if (!links.IsKnown)
        {
            return Failure<ListedNotificationDestination>(NoPublicOrigin, NotificationFailure.NoPublicOrigin);
        }

        byte[] sealedSubscription = seal.Seal(subscription);
        DateTime now = Now();

        PushReplacement replacement = await writes.AllOrNothingAsync(
            async inner =>
            {
                await rules.TakeTurnAsync(inner);

                PushReplacement made = await enrolling.ReplaceAsync(id, subscription, sealedSubscription, now, inner);

                if (made.Destination is not null)
                {
                    await destinations.SaveAsync(made.Destination, inner);
                }

                return made;
            },
            cancellationToken);

        if (replacement.Destination is null)
        {
            return replacement.Refusal is PushReplacementRefusal.HeldByAnother
                ? Failure<ListedNotificationDestination>(HeldByAnother, NotificationFailure.HeldByAnother)
                : Failure<ListedNotificationDestination>(Unknown, NotificationFailure.NoSuchDestination);
        }

        dispatch.Forget(id);

        return ServiceResult<ListedNotificationDestination, NotificationFailure>.Success(
            new ListedNotificationDestination(replacement.Destination, true));
    }

    private static ServiceResult<T, NotificationFailure> Failure<T>(string message, NotificationFailure failure)
        => ServiceResult<T, NotificationFailure>.Failure(message, failure);

    private async Task KeepAsync(PushEnrolment enrolment, CancellationToken cancellationToken)
    {
        if (!enrolment.Added)
        {
            await destinations.SaveAsync(enrolment.Destination, cancellationToken);

            return;
        }

        await destinations.AddAsync(enrolment.Destination, cancellationToken);

        if (enrolment.Usual is not null)
        {
            await rules.AddAsync(enrolment.Usual, cancellationToken);
        }
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
