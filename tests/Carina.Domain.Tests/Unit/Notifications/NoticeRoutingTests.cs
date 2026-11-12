using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Unit.Notifications;

[Trait("Category", "Unit")]
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

    [Fact(DisplayName = "BR-NS-003, BR-ND-010: a programme asked to be told of goes to each destination it names that is turned on, once")]
    public void AProgrammeAskedToBeToldOfGoesToTheDestinationsItNamesThatAreOn()
    {
        NotificationDestination first = Destination("First");
        NotificationDestination off = Destination("Off");
        off.Disable(At);
        NotificationDestination unnamed = Destination("Unnamed");
        NotificationDestination last = Destination("Last");
        ProgrammeWatch watch = Watch(last, off, first, last);

        IReadOnlyList<NotificationDestinationId> routed = NoticeRouting.DestinationsFor(watch, [first, off, unnamed, last]);

        Assert.Equal([first.Id, last.Id], routed);
    }

    [Fact(DisplayName = "BR-NS-003: a programme whose destinations are all turned off goes nowhere")]
    public void AProgrammeWhoseDestinationsAreAllOffGoesNowhere()
    {
        NotificationDestination off = Destination("Off");
        off.Disable(At);

        Assert.Empty(NoticeRouting.DestinationsFor(Watch(off), [off]));
    }

    private static NotificationDestination Destination(string name)
        => NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName(name),
            WebhookTarget.Slack,
            [1, 2, 3],
            "wxyz",
            At);

    private static ProgrammeWatch Watch(params NotificationDestination[] destinations)
        => ProgrammeWatch.Ask(
            ProgrammeWatchId.New(),
            new ProgrammeId(new NetworkId(32000), new ServiceId(1024), new EventId(4100)),
            new WatchedBroadcast(At.AddHours(1), At.AddHours(2), "A programme", null),
            3,
            [.. destinations.Select(destination => destination.Id)],
            At);

    private static NotificationRule Rule(IReadOnlyList<NoticeKind> events, params NotificationDestination[] destinations)
        => NotificationRule.Define(
            NotificationRuleId.New(),
            new NotificationName("A set of rules"),
            events,
            [.. destinations.Select(destination => destination.Id)],
            At);
}
