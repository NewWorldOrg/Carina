using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Notifications;

public sealed class NoticeRoutingTests
{
    private static readonly DateTime At = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-ND-010: a destination two sets of rules both name is listed once")]
    public void ADestinationTwoSetsOfRulesNameIsListedOnce()
    {
        NotificationDestination first = Destination("First");
        NotificationDestination second = Destination("Second");
        NotificationRule failures = Rule([NoticeKind.RecordingFailed], first, second);
        NotificationRule everything = Rule([NoticeKind.RecordingFailed, NoticeKind.RecordingCompleted], second);

        IReadOnlyList<NotificationDestinationId> routed = NoticeRouting.DestinationsFor(
            NoticeKind.RecordingFailed,
            [failures, everything],
            [first, second]);

        Assert.Equal([first.Id, second.Id], routed);
    }

    [Fact(DisplayName = "BR-ND-010: a set of rules that is turned off sends nowhere")]
    public void ASetOfRulesThatIsTurnedOffSendsNowhere()
    {
        NotificationDestination first = Destination("First");
        NotificationDestination second = Destination("Second");
        NotificationRule off = Rule([NoticeKind.RecordingFailed], first);
        off.Disable(At);
        NotificationRule on = Rule([NoticeKind.RecordingFailed], second);

        IReadOnlyList<NotificationDestinationId> routed = NoticeRouting.DestinationsFor(
            NoticeKind.RecordingFailed,
            [off, on],
            [first, second]);

        Assert.Equal([second.Id], routed);
    }

    [Fact(DisplayName = "BR-ND-010: a destination that is turned off, or that is no longer held, is sent nothing")]
    public void ADestinationThatIsTurnedOffOrGoneIsSentNothing()
    {
        NotificationDestination off = Destination("Off");
        off.Disable(At);
        NotificationDestination gone = Destination("Gone");
        NotificationDestination kept = Destination("Kept");
        NotificationRule rule = Rule([NoticeKind.EncodeFailed], off, gone, kept);

        IReadOnlyList<NotificationDestinationId> routed = NoticeRouting.DestinationsFor(NoticeKind.EncodeFailed, [rule], [off, kept]);

        Assert.Equal([kept.Id], routed);
    }

    [Fact(DisplayName = "BR-ND-010: a kind no set of rules lists goes nowhere")]
    public void AKindNoSetOfRulesListsGoesNowhere()
    {
        NotificationDestination first = Destination("First");
        NotificationRule rule = Rule([NoticeKind.RecordingFailed], first);

        Assert.Empty(NoticeRouting.DestinationsFor(NoticeKind.RecordingStarted, [rule], [first]));
    }

    private static NotificationDestination Destination(string name)
        => NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName(name),
            WebhookTarget.Slack,
            [1, 2, 3],
            "wxyz",
            At);

    private static NotificationRule Rule(IReadOnlyList<NoticeKind> events, params NotificationDestination[] destinations)
        => NotificationRule.Define(
            NotificationRuleId.New(),
            new NotificationName("A set of rules"),
            events,
            [.. destinations.Select(destination => destination.Id)],
            At);
}
