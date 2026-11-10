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

    [Fact(DisplayName = "BR-NA-003: a destination holds its address only as sealed bytes and the last four characters")]
    public void ADestinationHoldsItsAddressOnlyAsSealedBytesAndATail()
    {
        Assert.DoesNotContain(
            typeof(NotificationDestination).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            property => property.PropertyType == typeof(Uri) || (property.PropertyType == typeof(string) && property.Name != nameof(NotificationDestination.UrlTail)));
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

    private static NotificationDestination Registered()
        => NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName("A channel"),
            WebhookTarget.Slack,
            [1, 2, 3],
            "wxyz",
            At);
}
