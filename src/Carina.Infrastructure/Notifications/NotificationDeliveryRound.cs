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
/// worth another try a destination is left alone until the next pass. A delivery that was taken but could not be
/// written as sent is not sent again while the process remembers it; only the writing is tried again.
/// </remarks>
public sealed class NotificationDeliveryRound(
    INotificationDeliveryRepository deliveries,
    INotificationDestinationRepository destinations,
    NotificationDeliveryDomainService settlement,
    WebhookUrlSeal seal,
    PushSubscriptionSeal pushSeal,
    PushMessages messages,
    PushHandlingDomainService handling,
    NoticeDispatch dispatch,
    UnwrittenDeliveries unwritten,
    NotificationDeliverySettings settings,
    TimeProvider clock,
    ILogger<NotificationDeliveryRound> logger)
{
    private delegate Task<NoticeAnswer> Way(NoticeOnItsWay row, CancellationToken cancellationToken);

    public async Task<NotificationDeliveries> SendAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<NoticeOnItsWay> waiting = await deliveries.WaitingAsync(cancellationToken);

        unwritten.DropAllBut(waiting.Select(row => row.Delivery.Id));

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
        Way? way = await OpenedAsync(destination, cancellationToken);
        NotificationDeliveries moved = NotificationDeliveries.None;
        bool resting = false;

        foreach (NoticeOnItsWay row in bound)
        {
            DateTime now = Now();

            if (unwritten.TakenAt(row.Delivery.Id) is { } takenAt)
            {
                await WriteAsTakenAsync(row.Delivery, takenAt, cancellationToken);

                continue;
            }

            if (settlement.ReasonToGiveUp(row, destination, way is not null, now, settings.TooOldAfter) is { } reason)
            {
                moved = moved.With(await GiveUpAsync(row.Delivery, reason, cancellationToken));

                continue;
            }

            if (way is null || resting || !row.Delivery.IsDueAt(now) || !dispatch.HasRoomFor(destination.Id))
            {
                continue;
            }

            NotificationDeliveries came = await SendOneAsync(row, destination, way, cancellationToken);

            resting = came.Failed > 0;
            moved = moved.With(came);
        }

        return moved;
    }

    private async Task<Way?> OpenedAsync(NotificationDestination destination, CancellationToken cancellationToken)
    {
        if (!destination.Enabled)
        {
            return null;
        }

        Way? way = destination.Target is WebhookTarget.Push ? ToTheBrowser(destination) : ToTheWebhook(destination);

        if (way is null)
        {
            destination.NoteUnreadable(Now());
            await KeepResultAsync(destination, cancellationToken);
            logger.LogWarning(
                "The address of destination {Destination} does not open with the keys held; nothing is sent to it.",
                destination.Id);
        }

        return way;
    }

    private Way? ToTheWebhook(NotificationDestination destination)
    {
        WebhookUrl? webhook = seal.Open(destination.SealedUrl);

        if (webhook is null)
        {
            return null;
        }

        return (row, cancellationToken) => dispatch.SendAsync(
            destination.Id,
            webhook,
            NoticePayload.For(destination.Target, row.Notice),
            cancellationToken);
    }

    private Way? ToTheBrowser(NotificationDestination destination)
    {
        PushSubscription? subscription = pushSeal.Open(destination.SealedUrl);

        if (subscription is null || !messages.CanSign())
        {
            return null;
        }

        return async (row, cancellationToken) => await dispatch.SendAsync(
            destination.Id,
            subscription,
            messages.Telling(subscription, row.Notice, await handling.OfAsync(row.Notice, Now(), cancellationToken)),
            cancellationToken);
    }

    private async Task<NotificationDeliveries> GiveUpAsync(
        NotificationDelivery delivery,
        NoticeFailure reason,
        CancellationToken cancellationToken)
    {
        delivery.GiveUp(reason);

        return await IsWrittenAsync(delivery, cancellationToken)
            ? new NotificationDeliveries(0, 0, 1)
            : NotificationDeliveries.None;
    }

    private async Task WriteAsTakenAsync(NotificationDelivery delivery, DateTime takenAt, CancellationToken cancellationToken)
    {
        delivery.Deliver(takenAt);

        if (await IsWrittenAsync(delivery, cancellationToken))
        {
            unwritten.Drop(delivery.Id);
        }
    }

    private async Task<bool> IsWrittenAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        try
        {
            await deliveries.SaveAsync(delivery, cancellationToken);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            logger.LogError(
                "Delivery {Delivery} could not be written as {State} ({Failure}); the rest of this pass goes on.",
                delivery.Id,
                delivery.State,
                failure.GetType().Name);

            return false;
        }
    }

    private async Task KeepResultAsync(NotificationDestination destination, CancellationToken cancellationToken)
    {
        try
        {
            await destinations.KeepResultAsync(destination, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            logger.LogError(
                "What came of the last send to destination {Destination} could not be written ({Failure}); the rest of this pass goes on.",
                destination.Id,
                failure.GetType().Name);
        }
    }

    private async Task<NotificationDeliveries> SendOneAsync(
        NoticeOnItsWay row,
        NotificationDestination destination,
        Way way,
        CancellationToken cancellationToken)
    {
        NoticeAnswer answer = await way(row, cancellationToken);
        DateTime at = Now();
        NotificationDeliveries came = Counted(
            settlement.Settle(row.Delivery, destination, answer.Failure, answer.AddressGone, answer.Asked, at));

        if (!await IsWrittenAsync(row.Delivery, cancellationToken) && answer.Failure is null)
        {
            unwritten.Keep(row.Delivery.Id, at);
        }

        await KeepResultAsync(destination, cancellationToken);

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

    private static NotificationDeliveries Counted(NoticeSettlement settled)
        => settled switch
        {
            NoticeSettlement.Sent => new NotificationDeliveries(1, 0, 0),
            NoticeSettlement.FailedToTryAgain => new NotificationDeliveries(0, 1, 0),
            NoticeSettlement.FailedForGood => new NotificationDeliveries(0, 1, 1),
            _ => new NotificationDeliveries(0, 0, 1),
        };

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
