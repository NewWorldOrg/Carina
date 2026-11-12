using System.Reflection;

using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Notifications;

public sealed class NotificationDestinationTests
{
    private static readonly DateTime At = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NoPartOfADestinationCanBeMovedFromOutside()
    {
        Assert.DoesNotContain(
            typeof(NotificationDestination).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.SetMethod is { IsPublic: true });
    }

    [Fact(DisplayName = "BR-NA-003: a destination holds its address only as sealed bytes, the last four characters and, for a browser, the hash of its endpoint")]
    public void ADestinationHoldsItsAddressOnlyAsSealedBytesATailAndAHash()
    {
        string[] written = [nameof(NotificationDestination.UrlTail), nameof(NotificationDestination.EndpointHash)];

        Assert.DoesNotContain(
            typeof(NotificationDestination).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            property => property.PropertyType == typeof(Uri)
                        || property.PropertyType == typeof(PushSubscription)
                        || (property.PropertyType == typeof(string) && !written.Contains(property.Name)));
    }

    [Fact(DisplayName = "a destination starts turned on, with no result yet")]
    public void ADestinationStartsTurnedOn()
    {
        NotificationDestination destination = Registered();

        Assert.True(destination.Enabled);
        Assert.Null(destination.LastResultAt);
        Assert.Null(destination.LastFailure);
        Assert.Equal("wxyz", destination.UrlTail);
    }

    [Fact(DisplayName = "a destination keeps its own copy of the sealed bytes")]
    public void ADestinationKeepsItsOwnCopyOfTheSealedBytes()
    {
        byte[] handed = [1, 2, 3];
        NotificationDestination destination = NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName("A channel"),
            WebhookTarget.Discord,
            handed,
            "wxyz",
            At);

        handed[0] = 9;

        Assert.Equal([1, 2, 3], destination.SealedUrl);
    }

    [Theory(DisplayName = "a destination is not kept with no sealed bytes, too many, or a tail that is not four characters")]
    [InlineData(0, "wxyz")]
    [InlineData(4097, "wxyz")]
    [InlineData(8, "xyz")]
    [InlineData(8, "vwxyz")]
    [InlineData(8, "w yz")]
    public void ADestinationIsNotKeptWithABadSealOrTail(int sealedBytes, string tail)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => NotificationDestination.Register(
                NotificationDestinationId.New(),
                new NotificationName("A channel"),
                WebhookTarget.Slack,
                new byte[sealedBytes],
                tail,
                At));
    }

    [Fact(DisplayName = "BR-NS-002: a destination the other end rejected is turned off and keeps why")]
    public void ADestinationTheOtherEndRejectedIsTurnedOff()
    {
        NotificationDestination destination = Registered();

        destination.NoteFailed(NoticeFailure.Rejected, At.AddMinutes(1));

        Assert.False(destination.Enabled);
        Assert.Equal(NoticeFailure.Rejected, destination.LastFailure);
        Assert.Equal(At.AddMinutes(1), destination.LastResultAt);
    }

    [Fact(DisplayName = "BR-NS-001: a destination that turned one notice down without its address being gone stays on and keeps why")]
    public void ADestinationThatTurnedOneNoticeDownStaysOn()
    {
        NotificationDestination destination = Registered();

        destination.NoteTurnedDown(At.AddMinutes(1));

        Assert.True(destination.Enabled);
        Assert.Equal(NoticeFailure.Rejected, destination.LastFailure);
        Assert.Equal(At.AddMinutes(1), destination.LastResultAt);
    }

    [Fact(DisplayName = "BR-NS-002: a failure worth another try leaves the destination turned on")]
    public void AFailureWorthAnotherTryLeavesTheDestinationTurnedOn()
    {
        NotificationDestination destination = Registered();

        destination.NoteFailed(NoticeFailure.ServerFailed, At.AddMinutes(1));

        Assert.True(destination.Enabled);
        Assert.Equal(NoticeFailure.ServerFailed, destination.LastFailure);
    }

    [Fact(DisplayName = "BR-NS-002: a rejected destination nobody turned on again stays off, whatever comes of later attempts")]
    public void ARejectedDestinationNobodyTurnedOnAgainStaysOff()
    {
        NotificationDestination destination = Registered();

        destination.NoteFailed(NoticeFailure.Rejected, At.AddMinutes(1));
        Assert.False(destination.Enabled);

        destination.NoteDelivered(At.AddMinutes(2));
        Assert.False(destination.Enabled);

        destination.NoteFailed(NoticeFailure.ServerFailed, At.AddMinutes(3));
        Assert.False(destination.Enabled);
    }

    [Fact(DisplayName = "BR-NS-002: a rejected destination is on again once somebody turns it on")]
    public void ARejectedDestinationIsOnAgainOnceSomebodyTurnsItOn()
    {
        NotificationDestination destination = Registered();
        destination.NoteFailed(NoticeFailure.Rejected, At.AddMinutes(1));

        destination.Enable(At.AddMinutes(2));
        destination.NoteDelivered(At.AddMinutes(3));

        Assert.True(destination.Enabled);
        Assert.Null(destination.LastFailure);
        Assert.Equal(At.AddMinutes(3), destination.LastResultAt);
    }

    [Fact(DisplayName = "a destination pointed elsewhere takes the kind, the seal and the tail of the new address")]
    public void ADestinationPointedElsewhereTakesTheNewAddress()
    {
        NotificationDestination destination = Registered();

        destination.PointAt(WebhookTarget.Discord, [7, 8], "abcd", At.AddHours(1));

        Assert.Equal(WebhookTarget.Discord, destination.Target);
        Assert.Equal([7, 8], destination.SealedUrl);
        Assert.Equal("abcd", destination.UrlTail);
        Assert.Equal(At.AddHours(1), destination.ChangedAt);
    }

    [Fact(DisplayName = "BR-NS-002: a destination pointed elsewhere forgets what came of the last send to the old address")]
    public void ADestinationPointedElsewhereForgetsTheLastResult()
    {
        NotificationDestination destination = Registered();
        destination.NoteTurnedDown(At.AddMinutes(1));

        destination.PointAt(WebhookTarget.Discord, [7, 8], "abcd", At.AddHours(1));

        Assert.Null(destination.LastResultAt);
        Assert.Null(destination.LastFailure);
        Assert.True(destination.Enabled);
    }

    [Fact(DisplayName = "BR-NS-002: a destination that was turned off stays off when it is pointed elsewhere")]
    public void ADestinationTurnedOffStaysOffWhenPointedElsewhere()
    {
        NotificationDestination destination = Registered();
        destination.NoteFailed(NoticeFailure.Rejected, At.AddMinutes(1));

        destination.PointAt(WebhookTarget.Discord, [7, 8], "abcd", At.AddHours(1));

        Assert.False(destination.Enabled);
        Assert.Null(destination.LastResultAt);
        Assert.Null(destination.LastFailure);
    }

    [Fact(DisplayName = "BR-ND-012: a destination whose address does not open with the keys held keeps that it is unreadable, and stays on")]
    public void ADestinationWhoseAddressDoesNotOpenKeepsThatItIsUnreadable()
    {
        NotificationDestination destination = Registered();

        destination.NoteUnreadable(At.AddMinutes(1));

        Assert.True(destination.Enabled);
        Assert.Equal(NoticeFailure.Unreadable, destination.LastFailure);
        Assert.Equal(At.AddMinutes(1), destination.LastResultAt);
    }

    [Fact(DisplayName = "BR-ND-015: a browser is held by the sealed bytes of its subscription and the hash of its endpoint, turned on")]
    public void ABrowserIsHeldByItsSealedSubscriptionAndTheHashOfItsEndpoint()
    {
        PushSubscription subscription = Subscription("first");

        NotificationDestination destination = NotificationDestination.Subscribe(
            NotificationDestinationId.New(),
            new NotificationName("A browser"),
            subscription,
            [4, 5, 6],
            At);

        Assert.Equal(WebhookTarget.Push, destination.Target);
        Assert.Equal([4, 5, 6], destination.SealedUrl);
        Assert.Equal(subscription.Fingerprint, destination.EndpointHash);
        Assert.Equal(subscription.Fingerprint[^4..], destination.UrlTail);
        Assert.True(destination.Enabled);
        Assert.Null(destination.LastResultAt);
        Assert.Null(destination.LastFailure);
    }

    [Fact(DisplayName = "a webhook is held with no hash of an endpoint")]
    public void AWebhookIsHeldWithNoHashOfAnEndpoint()
    {
        Assert.Null(Registered().EndpointHash);
    }

    [Fact(DisplayName = "a browser is not registered the way a webhook is")]
    public void ABrowserIsNotRegisteredTheWayAWebhookIs()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => NotificationDestination.Register(
                NotificationDestinationId.New(),
                new NotificationName("A browser"),
                WebhookTarget.Push,
                [1, 2, 3],
                "wxyz",
                At));
    }

    [Theory(DisplayName = "a destination is not kept with a hash that does not go with its kind, or one that is not 64 characters of lowercase hex")]
    [InlineData(WebhookTarget.Slack, "0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData(WebhookTarget.Discord, "0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData(WebhookTarget.Push, null)]
    [InlineData(WebhookTarget.Push, "")]
    [InlineData(WebhookTarget.Push, "000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData(WebhookTarget.Push, "00000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData(WebhookTarget.Push, "000000000000000000000000000000000000000000000000000000000000000A")]
    [InlineData(WebhookTarget.Push, "000000000000000000000000000000000000000000000000000000000000000g")]
    public void ADestinationIsNotKeptWithAHashThatDoesNotGoWithItsKind(WebhookTarget target, string? hash)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => NotificationDestination.Rehydrate(
                NotificationDestinationId.New(),
                new NotificationName("A destination"),
                target,
                [1, 2, 3],
                "wxyz",
                hash,
                enabled: true,
                null,
                null,
                At,
                At));
    }

    [Fact(DisplayName = "BR-ND-015: a browser that takes another subscription keeps its name and stays on, and forgets the last result")]
    public void ABrowserThatTakesAnotherSubscriptionKeepsItsNameAndStaysOn()
    {
        NotificationDestination destination = Subscribed();
        destination.NoteTurnedDown(At.AddMinutes(1));
        PushSubscription other = Subscription("second");

        destination.Resubscribe(other, [7, 8], At.AddHours(1));

        Assert.Equal("A browser", destination.Name.Value);
        Assert.Equal(WebhookTarget.Push, destination.Target);
        Assert.Equal([7, 8], destination.SealedUrl);
        Assert.Equal(other.Fingerprint, destination.EndpointHash);
        Assert.Equal(other.Fingerprint[^4..], destination.UrlTail);
        Assert.Equal(At.AddHours(1), destination.ChangedAt);
        Assert.True(destination.Enabled);
        Assert.Null(destination.LastResultAt);
        Assert.Null(destination.LastFailure);
    }

    [Fact(DisplayName = "BR-ND-015: a browser that was turned off stays off when it takes another subscription")]
    public void ABrowserTurnedOffStaysOffWhenItTakesAnotherSubscription()
    {
        NotificationDestination destination = Subscribed();
        destination.NoteFailed(NoticeFailure.Rejected, At.AddMinutes(1));

        destination.Resubscribe(Subscription("second"), [7, 8], At.AddHours(1));

        Assert.False(destination.Enabled);
        Assert.Null(destination.LastResultAt);
        Assert.Null(destination.LastFailure);
    }

    [Fact(DisplayName = "a webhook takes no subscription, and a browser is not pointed at an address")]
    public void AWebhookTakesNoSubscriptionAndABrowserIsNotPointedAtAnAddress()
    {
        NotificationDestination webhook = Registered();
        NotificationDestination browser = Subscribed();

        Assert.Throws<InvalidOperationException>(() => webhook.Resubscribe(Subscription("second"), [7, 8], At.AddHours(1)));
        Assert.Throws<InvalidOperationException>(() => browser.PointAt(WebhookTarget.Slack, [7, 8], "abcd", At.AddHours(1)));
        Assert.Throws<InvalidOperationException>(() => webhook.PointAt(WebhookTarget.Push, [7, 8], "abcd", At.AddHours(1)));
        Assert.Equal(WebhookTarget.Slack, webhook.Target);
        Assert.Equal([1, 2, 3], webhook.SealedUrl);
        Assert.Equal(WebhookTarget.Push, browser.Target);
        Assert.Equal([4, 5, 6], browser.SealedUrl);
    }

    [Fact(DisplayName = "BR-NS-002: a browser whose push service says the subscription is gone is turned off and keeps why")]
    public void ABrowserWhoseSubscriptionIsGoneIsTurnedOff()
    {
        NotificationDestination destination = Subscribed();

        destination.NoteFailed(NoticeFailure.Rejected, At.AddMinutes(1));

        Assert.False(destination.Enabled);
        Assert.Equal(NoticeFailure.Rejected, destination.LastFailure);
    }

    private static NotificationDestination Subscribed()
        => NotificationDestination.Subscribe(
            NotificationDestinationId.New(),
            new NotificationName("A browser"),
            Subscription("first"),
            [4, 5, 6],
            At);

    private static PushSubscription Subscription(string registration)
        => PushSubscription.Read(
            $"https://updates.push.services.mozilla.com/wpush/v2/made-up-{registration}",
            "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4",
            "BTBZMqHH6r4Tts7J_aSIgg")!;

    private static NotificationDestination Registered()
        => NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName("A channel"),
            WebhookTarget.Slack,
            [1, 2, 3],
            "wxyz",
            At);
}
