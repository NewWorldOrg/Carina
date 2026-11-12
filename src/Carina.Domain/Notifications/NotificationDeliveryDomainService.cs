namespace Carina.Domain.Notifications;

/// <summary>
/// Decides, across a delivery, the notice it carries and the destination it is bound for, whether the delivery is sent
/// at all, and where an attempt to send it leaves both the delivery and the destination.
/// </summary>
public sealed class NotificationDeliveryDomainService
{
    /// <summary>
    /// Why a delivery that waits is given up rather than sent at <paramref name="at"/>, or nothing when it may be sent:
    /// its destination is turned off, the address does not open with the keys held, or what it tells happened more
    /// than <paramref name="tooOldAfter"/> ago.
    /// </summary>
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

    /// <summary>
    /// Writes what one attempt came to into the delivery and its destination. A failure worth another try is tried
    /// again while attempts are left; any other failure gives the delivery up, turning the destination off only when
    /// the other end no longer knows the address.
    /// </summary>
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
