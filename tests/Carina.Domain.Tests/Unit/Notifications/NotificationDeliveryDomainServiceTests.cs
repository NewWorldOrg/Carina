using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Unit.Notifications;

[Trait("Category", "Unit")]
public sealed class NotificationDeliveryDomainServiceTests
{
    private static readonly DateTime At = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan TooOldAfter = TimeSpan.FromHours(1);

    private readonly NotificationDeliveryDomainService service = new();

    [Fact(DisplayName = "BR-NS-001: a delivery to a destination turned on, whose address opens, of a notice not too old, is sent")]
    public void ADeliveryThatCanGoIsSent()
    {
        Assert.Null(service.ReasonToGiveUp(Row(At), Destination(), addressOpens: true, At.AddHours(1), TooOldAfter));
    }

    [Fact(DisplayName = "BR-NS-002: a delivery to a destination turned off is given up, before anything else is asked of it")]
    public void ADeliveryToADestinationTurnedOffIsGivenUp()
    {
        NotificationDestination off = Destination();
        off.Disable(At);

        Assert.Equal(
            NoticeFailure.DestinationDisabled,
            service.ReasonToGiveUp(Row(At), off, addressOpens: false, At.AddHours(2), TooOldAfter));
    }

    [Fact(DisplayName = "BR-ND-012: a delivery to a destination whose address does not open is given up as unreadable, before its age is asked")]
    public void ADeliveryWhoseAddressDoesNotOpenIsGivenUp()
    {
        Assert.Equal(
            NoticeFailure.Unreadable,
            service.ReasonToGiveUp(Row(At), Destination(), addressOpens: false, At.AddHours(2), TooOldAfter));
    }

    [Fact(DisplayName = "BR-NS-001: a delivery of a notice more than an hour old is given up as too old")]
    public void ADeliveryOfANoticeTooOldIsGivenUp()
    {
        Assert.Equal(
            NoticeFailure.TooOld,
            service.ReasonToGiveUp(Row(At), Destination(), addressOpens: true, At.AddHours(1).AddTicks(1), TooOldAfter));
    }

    [Fact(DisplayName = "BR-NS-001: an attempt that went through sends the delivery and clears the destination's last failure")]
    public void AnAttemptThatWentThroughSendsTheDelivery()
    {
        NotificationDelivery delivery = Queued();
        NotificationDestination destination = Destination();
        destination.NoteFailed(NoticeFailure.TimedOut, At);

        Assert.Equal(NoticeSettlement.Sent, service.Settle(delivery, destination, null, false, null, At.AddMinutes(1)));
        Assert.Equal(NotificationDeliveryState.Sent, delivery.State);
        Assert.Null(destination.LastFailure);
        Assert.Equal(At.AddMinutes(1), destination.LastResultAt);
    }

    [Fact(DisplayName = "BR-NS-001, BR-NC-001: a failure worth another try leaves the delivery waiting, until no attempt is left")]
    public void AFailureWorthAnotherTryLeavesTheDeliveryWaiting()
    {
        NotificationDelivery delivery = Queued();
        NotificationDestination destination = Destination();

        Assert.Equal(
            NoticeSettlement.FailedToTryAgain,
            service.Settle(delivery, destination, NoticeFailure.RateLimited, false, TimeSpan.FromSeconds(7), At));
        Assert.Equal(At.AddSeconds(7), delivery.NextAttemptAt);
        Assert.Equal(NoticeFailure.RateLimited, destination.LastFailure);
        Assert.True(destination.Enabled);

        service.Settle(delivery, destination, NoticeFailure.ServerFailed, false, null, At);
        service.Settle(delivery, destination, NoticeFailure.ServerFailed, false, null, At);

        Assert.Equal(
            NoticeSettlement.FailedForGood,
            service.Settle(delivery, destination, NoticeFailure.ServerFailed, false, null, At));
        Assert.Equal(NotificationDeliveryState.GivenUp, delivery.State);
    }

    [Fact(DisplayName = "BR-NS-002: an address the other end no longer knows gives the delivery up and turns the destination off")]
    public void AnAddressGoneTurnsTheDestinationOff()
    {
        NotificationDelivery delivery = Queued();
        NotificationDestination destination = Destination();

        Assert.Equal(NoticeSettlement.GivenUp, service.Settle(delivery, destination, NoticeFailure.Rejected, true, null, At));
        Assert.Equal(NotificationDeliveryState.GivenUp, delivery.State);
        Assert.False(destination.Enabled);
        Assert.Equal(NoticeFailure.Rejected, destination.LastFailure);
    }

    [Fact(DisplayName = "BR-NS-002: a notice turned down while the address stands gives the delivery up and leaves the destination on")]
    public void ANoticeTurnedDownLeavesTheDestinationOn()
    {
        NotificationDelivery delivery = Queued();
        NotificationDestination destination = Destination();

        Assert.Equal(NoticeSettlement.GivenUp, service.Settle(delivery, destination, NoticeFailure.Rejected, false, null, At));
        Assert.Equal(NoticeFailure.Rejected, delivery.LastFailure);
        Assert.True(destination.Enabled);
        Assert.Equal(NoticeFailure.Rejected, destination.LastFailure);
    }

    private static NoticeOnItsWay Row(DateTime occurredAt)
    {
        Notice notice = Notice.Take(
            NoticeId.New(),
            NoticeKind.RecordingFailed,
            Guid.NewGuid(),
            occurredAt,
            new NoticeWords("A title", "A body", null),
            occurredAt);

        return new NoticeOnItsWay(
            NotificationDelivery.Queue(NotificationDeliveryId.New(), notice.Id, NotificationDestinationId.New(), occurredAt),
            notice);
    }

    private static NotificationDelivery Queued()
        => NotificationDelivery.Queue(NotificationDeliveryId.New(), NoticeId.New(), NotificationDestinationId.New(), At);

    private static NotificationDestination Destination()
        => NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName("A channel"),
            WebhookTarget.Slack,
            [1, 2, 3],
            "wxyz",
            At);
}
