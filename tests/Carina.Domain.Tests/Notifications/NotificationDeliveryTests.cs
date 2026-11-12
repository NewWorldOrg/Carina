using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Notifications;

public sealed class NotificationDeliveryTests
{
    private static readonly DateTime At = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    [Theory(DisplayName = "FR: at most fifty of a destination's deliveries are listed, and that is how many are listed unless fewer are asked for")]
    [InlineData(null, 50)]
    [InlineData(0, 50)]
    [InlineData(-3, 50)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(51, 50)]
    public void AtMostFiftyDeliveriesAreListed(int? asked, int listed)
        => Assert.Equal(listed, NotificationDelivery.Listed(asked));

    [Fact(DisplayName = "BR-NS-001: a delivery starts queued, untried, and due at once")]
    public void ADeliveryStartsQueuedAndDueAtOnce()
    {
        NotificationDelivery delivery = Queued();

        Assert.Equal(NotificationDeliveryState.Queued, delivery.State);
        Assert.Equal(0, delivery.Attempts);
        Assert.Equal(At, delivery.NextAttemptAt);
        Assert.Null(delivery.SentAt);
        Assert.Null(delivery.LastFailure);
    }

    [Fact(DisplayName = "BR-NS-001: a queued delivery that went through is sent, with when")]
    public void AQueuedDeliveryThatWentThroughIsSent()
    {
        NotificationDelivery delivery = Queued();

        delivery.Deliver(At.AddSeconds(2));

        Assert.Equal(NotificationDeliveryState.Sent, delivery.State);
        Assert.Equal(1, delivery.Attempts);
        Assert.Equal(At.AddSeconds(2), delivery.SentAt);
        Assert.Null(delivery.NextAttemptAt);
    }

    [Theory(DisplayName = "BR-NS-001: a failure worth another try leaves the delivery failed and due after the wait")]
    [InlineData(NoticeFailure.ServerFailed)]
    [InlineData(NoticeFailure.RateLimited)]
    [InlineData(NoticeFailure.TimedOut)]
    [InlineData(NoticeFailure.Unreachable)]
    [InlineData(NoticeFailure.TlsFailed)]
    public void AFailureWorthAnotherTryLeavesTheDeliveryDueAfterTheWait(NoticeFailure failure)
    {
        NotificationDelivery delivery = Queued();

        delivery.Fail(failure, At.AddSeconds(1));

        Assert.Equal(NotificationDeliveryState.Failed, delivery.State);
        Assert.Equal(1, delivery.Attempts);
        Assert.Equal(failure, delivery.LastFailure);
        Assert.Equal(At.AddSeconds(31), delivery.NextAttemptAt);
    }

    [Fact(DisplayName = "BR-NS-001, BR-NC-001: the first three failures move the next attempt out by 30 seconds, 2 minutes and 10 minutes, and the fourth failure is given up")]
    public void TheFourthFailureAfterThreeWaitsIsGivenUp()
    {
        NotificationDelivery delivery = Queued();

        delivery.Fail(NoticeFailure.ServerFailed, At);
        Assert.Equal(At.AddSeconds(30), delivery.NextAttemptAt);

        delivery.Fail(NoticeFailure.ServerFailed, At.AddMinutes(1));
        Assert.Equal(At.AddMinutes(3), delivery.NextAttemptAt);

        delivery.Fail(NoticeFailure.TimedOut, At.AddMinutes(4));
        Assert.Equal(At.AddMinutes(14), delivery.NextAttemptAt);
        Assert.Equal(NotificationDeliveryState.Failed, delivery.State);

        delivery.Fail(NoticeFailure.Unreachable, At.AddMinutes(15));

        Assert.Equal(NotificationDeliveryState.GivenUp, delivery.State);
        Assert.Equal(4, delivery.Attempts);
        Assert.Equal(NoticeFailure.Unreachable, delivery.LastFailure);
        Assert.Null(delivery.NextAttemptAt);
    }

    [Fact(DisplayName = "BR-NS-001: a failed delivery that goes through on a later attempt is sent and keeps no failure")]
    public void AFailedDeliveryThatGoesThroughLaterIsSent()
    {
        NotificationDelivery delivery = Queued();
        delivery.Fail(NoticeFailure.ServerFailed, At);

        delivery.Deliver(At.AddSeconds(31));

        Assert.Equal(NotificationDeliveryState.Sent, delivery.State);
        Assert.Equal(2, delivery.Attempts);
        Assert.Null(delivery.LastFailure);
    }

    [Fact(DisplayName = "BR-NC-001: a rate-limited delivery waits as long as the other end asks, up to 10 minutes")]
    public void ARateLimitedDeliveryWaitsAsLongAsAsked()
    {
        NotificationDelivery delivery = Queued();

        delivery.Fail(NoticeFailure.RateLimited, At, TimeSpan.FromSeconds(5));
        Assert.Equal(At.AddSeconds(5), delivery.NextAttemptAt);

        delivery.Fail(NoticeFailure.RateLimited, At.AddSeconds(5), TimeSpan.FromHours(1));
        Assert.Equal(At.AddSeconds(5).AddMinutes(10), delivery.NextAttemptAt);
    }

    [Theory(DisplayName = "BR-NS-001: a delivery that cannot go through is given up without another try, keeping why")]
    [InlineData(NoticeFailure.Rejected)]
    [InlineData(NoticeFailure.DestinationDisabled)]
    [InlineData(NoticeFailure.Unreadable)]
    [InlineData(NoticeFailure.TooOld)]
    public void ADeliveryThatCannotGoThroughIsGivenUp(NoticeFailure failure)
    {
        NotificationDelivery delivery = Queued();

        Assert.Throws<ArgumentException>(() => delivery.Fail(failure, At));

        delivery.GiveUp(failure);

        Assert.Equal(NotificationDeliveryState.GivenUp, delivery.State);
        Assert.Equal(failure, delivery.LastFailure);
        Assert.Null(delivery.NextAttemptAt);
    }

    [Fact(DisplayName = "BR-NS-001: a delivery that was sent or given up is not moved again")]
    public void ADeliveryThatHasEndedIsNotMovedAgain()
    {
        NotificationDelivery sent = Queued();
        sent.Deliver(At);
        NotificationDelivery givenUp = Queued();
        givenUp.GiveUp(NoticeFailure.TooOld);

        Assert.Throws<InvalidOperationException>(() => sent.Fail(NoticeFailure.ServerFailed, At));
        Assert.Throws<InvalidOperationException>(() => sent.GiveUp(NoticeFailure.TooOld));
        Assert.Throws<InvalidOperationException>(() => givenUp.Deliver(At));
        Assert.Throws<InvalidOperationException>(() => givenUp.Fail(NoticeFailure.ServerFailed, At));
    }

    [Fact(DisplayName = "BR-NS-001, BR-NC-001: a delivery is due from its next attempt on, and not before")]
    public void ADeliveryIsDueFromItsNextAttemptOn()
    {
        NotificationDelivery delivery = Queued();
        delivery.Fail(NoticeFailure.ServerFailed, At);

        Assert.True(Queued().IsDueAt(At));
        Assert.False(delivery.IsDueAt(At.AddSeconds(29)));
        Assert.True(delivery.IsDueAt(At.AddSeconds(30)));
        Assert.True(delivery.IsDueAt(At.AddMinutes(5)));
    }

    [Fact(DisplayName = "a delivery is not read back standing where its own fields do not put it")]
    public void ADeliveryIsNotReadBackStandingWhereItsFieldsDoNotPutIt()
    {
        Assert.Throws<ArgumentException>(() => Held(NotificationDeliveryState.Queued, 0, null, null, null));
        Assert.Throws<ArgumentException>(() => Held(NotificationDeliveryState.Sent, 1, null, null, null));
        Assert.Throws<ArgumentException>(() => Held(NotificationDeliveryState.Failed, 1, At, null, null));
        Assert.Throws<ArgumentException>(() => Held(NotificationDeliveryState.GivenUp, 0, null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => Held(NotificationDeliveryState.Failed, 5, At, null, NoticeFailure.TimedOut));
        Assert.Throws<ArgumentException>(
            () => Held(NotificationDeliveryState.Queued, 0, new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Local), null, null));
    }

    private static NotificationDelivery Queued()
        => NotificationDelivery.Queue(NotificationDeliveryId.New(), NoticeId.New(), NotificationDestinationId.New(), At);

    private static NotificationDelivery Held(
        NotificationDeliveryState state,
        int attempts,
        DateTime? nextAttemptAt,
        DateTime? sentAt,
        NoticeFailure? lastFailure)
        => NotificationDelivery.Rehydrate(
            NotificationDeliveryId.New(),
            NoticeId.New(),
            NotificationDestinationId.New(),
            state,
            attempts,
            nextAttemptAt,
            sentAt,
            lastFailure,
            At);
}
