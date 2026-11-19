namespace Carina.Domain.Notifications;

public sealed class NotificationDeliveryDomainService
{
    public NoticeFailure? ReasonToGiveUp(
        NoticeOnItsWay row,
        NotificationDestination destination,
        bool addressOpens,
        DateTime at,
        TimeSpan tooOldAfter)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.Enabled)
        {
            return NoticeFailure.DestinationDisabled;
        }

        if (!addressOpens)
        {
            return NoticeFailure.Unreadable;
        }

        return row.Notice.IsTooOldAt(at, tooOldAfter) ? NoticeFailure.TooOld : null;
    }

    public NoticeSettlement Settle(
        NotificationDelivery delivery,
        NotificationDestination destination,
        NoticeFailure? failure,
        bool addressGone,
        TimeSpan? asked,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(destination);

        if (failure is not { } failed)
        {
            delivery.Deliver(at);
            destination.NoteDelivered(at);

            return NoticeSettlement.Sent;
        }

        if (NoticeFailures.IsWorthAnotherTry(failed))
        {
            delivery.Fail(failed, at, asked);
            destination.NoteFailed(failed, at);

            return delivery.IsWaiting ? NoticeSettlement.FailedToTryAgain : NoticeSettlement.FailedForGood;
        }

        delivery.GiveUp(failed);

        if (addressGone)
        {
            destination.NoteFailed(failed, at);
        }
        else
        {
            destination.NoteTurnedDown(at);
        }

        return NoticeSettlement.GivenUp;
    }
}
