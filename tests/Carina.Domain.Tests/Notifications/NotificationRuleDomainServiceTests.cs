using Carina.Domain.Notifications;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Notifications;

public sealed class NotificationRuleDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private readonly HeldNotificationRules rules = new();

    private readonly HeldNotificationDestinations destinations = new();

    private NotificationRuleDomainService Service => new(rules, destinations);

    [Fact(DisplayName = "BR-NT-004: a set of rules sends to the destinations named, each registered, each once")]
    public async Task ASetOfRulesSendsToTheDestinationsNamed()
    {
        NotificationDestination first = Destination();
        NotificationDestination off = Destination();
        off.Disable(At);

        Assert.Equal([off.Id, first.Id], await Service.SentToAsync([off.Id.Value, first.Id.Value, off.Id.Value], Cancel));
    }

    [Fact(DisplayName = "BR-NT-004: a set of rules sends to at least one destination, and none it names is unregistered")]
    public async Task ASetOfRulesSendsToRegisteredDestinationsOnly()
    {
        NotificationDestination first = Destination();

        Assert.Null(await Service.SentToAsync(null, Cancel));
        Assert.Null(await Service.SentToAsync([], Cancel));
        Assert.Null(await Service.SentToAsync([first.Id.Value, Guid.NewGuid()], Cancel));
    }

    [Fact(DisplayName = "BR-NT-006: another set of rules is made while fewer than twenty are held")]
    public async Task AnotherSetOfRulesIsMadeWhileFewerThanTwentyAreHeld()
    {
        NotificationDestination destination = Destination();

        for (int made = 0; made < NotificationRule.MostRules - 1; made++)
        {
            rules.Rules.Add(Rule(destination));
        }

        Assert.True(await Service.HasRoomForAnotherAsync(Cancel));

        rules.Rules.Add(Rule(destination));

        Assert.False(await Service.HasRoomForAnotherAsync(Cancel));
    }

    private static NotificationRule Rule(NotificationDestination destination)
        => NotificationRule.Define(
            NotificationRuleId.New(),
            new NotificationName("A set of rules"),
            [NoticeKind.RecordingFailed],
            [destination.Id],
            At);

    private NotificationDestination Destination()
    {
        NotificationDestination destination = NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName("A channel"),
            WebhookTarget.Slack,
            [1, 2, 3],
            "wxyz",
            At.AddSeconds(destinations.Destinations.Count));
        destinations.Destinations.Add(destination);

        return destination;
    }
}
