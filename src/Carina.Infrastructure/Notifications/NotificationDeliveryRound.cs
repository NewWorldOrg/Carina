using Carina.Domain.Notifications;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Notifications;

public sealed record NotificationDeliveries(int Sent, int Failed, int GivenUp)
{
    public static NotificationDeliveries None { get; } = new(0, 0, 0);

    public int Moved => Sent + Failed + GivenUp;

    public NotificationDeliveries With(NotificationDeliveries other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new NotificationDeliveries(Sent + other.Sent, Failed + other.Failed, GivenUp + other.GivenUp);
    }
}

/// <summary>
/// One pass over the deliveries that wait: each destination is sent what waits for it in the order it happened, as
/// far as the destination has room, and every delivery that was tried or can no longer be sent is written where it
/// now stands.
/// </summary>
/// <remarks>
/// A delivery whose next attempt is not due is passed over without holding up the ones behind it. After a failure
/// worth another try a destination is left alone until the next pass.
/// </remarks>
public sealed class NotificationDeliveryRound(
    INotificationDeliveryRepository deliveries,
    INotificationDestinationRepository destinations,
    WebhookUrlSeal seal,
    NoticeDispatch dispatch,
    NotificationDeliverySettings settings,
    TimeProvider clock,
    ILogger<NotificationDeliveryRound> logger)
{
    public async Task<NotificationDeliveries> SendAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<NoticeOnItsWay> waiting = await deliveries.WaitingAsync(cancellationToken);

        if (waiting.Count is 0)
        {
            return NotificationDeliveries.None;
        }

        Dictionary<NotificationDestinationId, NotificationDestination> known =
            (await destinations.ListAsync(cancellationToken)).ToDictionary(destination => destination.Id);
        NotificationDeliveries moved = NotificationDeliveries.None;

        foreach (IGrouping<NotificationDestinationId, NoticeOnItsWay> bound in waiting.GroupBy(row => row.Delivery.DestinationId))
        {
            if (known.TryGetValue(bound.Key, out NotificationDestination? destination))
            {
                moved = moved.With(await SendToAsync(destination, [.. bound], cancellationToken));
            }
        }

        return moved;
    }

    private async Task<NotificationDeliveries> SendToAsync(
        NotificationDestination destination,
        IReadOnlyList<NoticeOnItsWay> bound,
        CancellationToken cancellationToken)
    {
        WebhookUrl? webhook = await OpenedAsync(destination, cancellationToken);
        NotificationDeliveries moved = NotificationDeliveries.None;
        bool resting = false;

        foreach (NoticeOnItsWay row in bound)
        {
            DateTime now = Now();

            if (webhook is null || !destination.Enabled || IsTooOld(row.Notice, now))
            {
                moved = moved.With(await GiveUpAsync(row, destination, webhook, cancellationToken));

                continue;
            }

            if (resting || row.Delivery.NextAttemptAt > now || !dispatch.HasRoomFor(destination.Id))
            {
                continue;
            }

            NotificationDeliveries came = await SendOneAsync(row, destination, webhook, cancellationToken);

            resting = came.Failed > 0;
            moved = moved.With(came);
        }

        return moved;
    }

    private async Task<WebhookUrl?> OpenedAsync(NotificationDestination destination, CancellationToken cancellationToken)
    {
        if (!destination.Enabled)
        {
            return null;
        }

        WebhookUrl? webhook = seal.Open(destination.SealedUrl);

        if (webhook is null)
        {
            destination.NoteFailed(NoticeFailure.Unreadable, Now());
            await destinations.KeepResultAsync(destination, cancellationToken);
            logger.LogWarning(
                "The address of destination {Destination} does not open with the keys held; nothing is sent to it.",
                destination.Id);
        }

        return webhook;
    }

    private async Task<NotificationDeliveries> GiveUpAsync(
        NoticeOnItsWay row,
        NotificationDestination destination,
        WebhookUrl? webhook,
        CancellationToken cancellationToken)
    {
        NoticeFailure reason = NoticeFailure.TooOld;

        if (!destination.Enabled)
        {
            reason = NoticeFailure.DestinationDisabled;
        }
        else if (webhook is null)
        {
            reason = NoticeFailure.Unreadable;
        }

        row.Delivery.GiveUp(reason);
        await deliveries.SaveAsync(row.Delivery, cancellationToken);

        return new NotificationDeliveries(0, 0, 1);
    }

    private async Task<NotificationDeliveries> SendOneAsync(
        NoticeOnItsWay row,
        NotificationDestination destination,
        WebhookUrl webhook,
        CancellationToken cancellationToken)
    {
        NoticeAnswer answer = await dispatch.SendAsync(
            destination.Id,
            webhook,
            NoticePayload.For(destination.Target, row.Notice),
            cancellationToken);
        NotificationDeliveries came = Settle(row.Delivery, destination, answer, Now());

        await deliveries.SaveAsync(row.Delivery, cancellationToken);
        await destinations.KeepResultAsync(destination, cancellationToken);

        if (answer.Failure is { } failure)
        {
            logger.LogWarning(
                "A {Kind} notice did not reach destination {Destination}: {Failure}. The delivery is now {State}.",
                row.Notice.Kind,
                destination.Id,
                failure,
                row.Delivery.State);
        }

        if (answer.AddressGone)
        {
            logger.LogWarning(
                "Destination {Destination} no longer knows its address and was turned off.",
                destination.Id);
        }

        return came;
    }

    private static NotificationDeliveries Settle(
        NotificationDelivery delivery,
        NotificationDestination destination,
        NoticeAnswer answer,
        DateTime at)
    {
        if (answer.Failure is not { } failure)
        {
            delivery.Deliver(at);
            destination.NoteDelivered(at);

            return new NotificationDeliveries(1, 0, 0);
        }

        if (NoticeFailures.IsWorthAnotherTry(failure))
        {
            delivery.Fail(failure, at, answer.Asked);
            destination.NoteFailed(failure, at);

            return delivery.IsWaiting ? new NotificationDeliveries(0, 1, 0) : new NotificationDeliveries(0, 1, 1);
        }

        delivery.GiveUp(failure);

        if (answer.AddressGone)
        {
            destination.NoteFailed(failure, at);
        }
        else
        {
            destination.NoteTurnedDown(at);
        }

        return new NotificationDeliveries(0, 0, 1);
    }

    private bool IsTooOld(Notice notice, DateTime now) => notice.OccurredAt < now - settings.TooOldAfter;

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
