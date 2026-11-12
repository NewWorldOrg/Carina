using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Notifications;

public sealed class NotificationDestinationDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private readonly HeldNotificationDestinations destinations = new();

    private readonly HeldNotificationRules rules = new();

    private readonly HeldNotificationDeliveries deliveries = new();

    private readonly HeldProgrammeWatches watches = new();

    private NotificationDestinationDomainService Service => new(destinations, rules, deliveries, watches);

    [Fact(DisplayName = "D-9: the first destination, with no destination and no set of rules held, comes with the usual set of rules told to it")]
    public async Task TheFirstDestinationComesWithTheUsualSetOfRules()
    {
        NotificationDestination first = Unheld();

        NotificationRule? usual = await Service.UsualRuleForAsync(first, Cancel);

        Assert.NotNull(usual);
        Assert.Equal(NotificationRule.UsualName, usual.Name.Value);
        Assert.Equal([first.Id], usual.Destinations);
        Assert.Equal(first.CreatedAt, usual.CreatedAt);
    }

    [Fact(DisplayName = "D-9: a destination registered beside another destination or a set of rules comes with no set of rules")]
    public async Task ALaterDestinationComesWithNoSetOfRules()
    {
        NotificationDestination held = Unheld();
        destinations.Destinations.Add(held);

        Assert.Null(await Service.UsualRuleForAsync(Unheld(), Cancel));

        destinations.Destinations.Clear();
        rules.Rules.Add(NotificationRule.Usual(NotificationRuleId.New(), held.Id, At));

        Assert.Null(await Service.UsualRuleForAsync(Unheld(), Cancel));
    }

    [Fact(DisplayName = "BR-NS-002: a destination turned off gives up the deliveries that wait for it, and no other")]
    public async Task ADestinationTurnedOffGivesUpWhatWaitsForIt()
    {
        NotificationDestinationId off = NotificationDestinationId.New();
        NotificationDestinationId other = NotificationDestinationId.New();
        NotificationDelivery waiting = Waiting(off);
        NotificationDelivery failed = Waiting(off);
        failed.Fail(NoticeFailure.ServerFailed, At);
        NotificationDelivery sent = Waiting(off);
        sent.Deliver(At);
        NotificationDelivery elsewhere = Waiting(other);

        IReadOnlyList<NotificationDelivery> givenUp = await Service.GiveUpWhatWaitsForAsync(off, Cancel);

        Assert.Equal([waiting.Id, failed.Id], givenUp.Select(delivery => delivery.Id));
        Assert.All(givenUp, delivery => Assert.Equal(NoticeFailure.DestinationDisabled, delivery.LastFailure));
        Assert.Equal(NotificationDeliveryState.GivenUp, waiting.State);
        Assert.Equal(NotificationDeliveryState.Sent, sent.State);
        Assert.Equal(NotificationDeliveryState.Queued, elsewhere.State);
    }

    [Fact(DisplayName = "BR-NT-004: a destination removed is taken out of every set of rules, and a set left with none is removed")]
    public async Task ADestinationRemovedIsTakenOutOfEverySetOfRules()
    {
        NotificationDestinationId removed = NotificationDestinationId.New();
        NotificationDestinationId kept = NotificationDestinationId.New();
        NotificationRule onlyIt = Rule(removed);
        NotificationRule both = Rule(removed, kept);
        NotificationRule neither = Rule(kept);

        NotificationDestinationRetirement retired = await Service.RetireAsync(removed, At.AddHours(1), Cancel);

        Assert.Equal([onlyIt.Id], retired.Emptied.Select(rule => rule.Id));
        Assert.Equal([both.Id], retired.Narrowed.Select(rule => rule.Id));
        Assert.Equal([kept], retired.Narrowed[0].Destinations);
        Assert.Equal([kept], neither.Destinations);
    }

    [Fact(DisplayName = "BR-ND-013: a destination removed is taken out of the programmes that wait, and one told of keeps it")]
    public async Task ADestinationRemovedIsTakenOutOfTheProgrammesThatWait()
    {
        NotificationDestinationId removed = NotificationDestinationId.New();
        NotificationDestinationId kept = NotificationDestinationId.New();
        ProgrammeWatch shared = Watch(4100, removed, kept);
        ProgrammeWatch alone = Watch(4101, removed);
        ProgrammeWatch told = Watch(4102, removed);
        told.Notify(At);
        await watches.SaveAsync(told, Cancel);
        Watch(4103, kept);

        NotificationDestinationRetirement retired = await Service.RetireAsync(removed, At.AddMinutes(1), Cancel);

        Assert.Equal([shared.Id, alone.Id], retired.NoLongerTelling.Select(watch => watch.Id));
        Assert.Equal([kept], retired.NoLongerTelling[0].Destinations);
        Assert.Equal(ProgrammeWatchState.Cancelled, retired.NoLongerTelling[1].State);
    }

    private static NotificationDestination Unheld()
        => NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName("A channel"),
            WebhookTarget.Slack,
            [1, 2, 3],
            "wxyz",
            At);

    private NotificationRule Rule(params NotificationDestinationId[] sentTo)
    {
        NotificationRule rule = NotificationRule.Define(
            NotificationRuleId.New(),
            new NotificationName("A set of rules"),
            [NoticeKind.RecordingFailed],
            sentTo,
            At.AddSeconds(rules.Rules.Count));
        rules.Rules.Add(rule);

        return rule;
    }

    private NotificationDelivery Waiting(NotificationDestinationId destination)
    {
        Notice notice = Notice.Take(
            NoticeId.New(),
            NoticeKind.RecordingFailed,
            Guid.NewGuid(),
            At.AddSeconds(deliveries.Rows.Count),
            new NoticeWords("A title", "A body", null),
            At);
        NotificationDelivery delivery = NotificationDelivery.Queue(NotificationDeliveryId.New(), notice.Id, destination, At);
        deliveries.Rows.Add(new NoticeOnItsWay(delivery, notice));

        return delivery;
    }

    private ProgrammeWatch Watch(int eventId, params NotificationDestinationId[] toldTo)
    {
        DateTime starts = At.AddHours(2).AddMinutes(eventId - 4100);
        ProgrammeWatch watch = ProgrammeWatch.Ask(
            ProgrammeWatchId.New(),
            new ProgrammeId(new NetworkId(32000), new ServiceId(1024), new EventId(eventId)),
            new WatchedBroadcast(starts, starts.AddMinutes(30), "A programme", null),
            3,
            toldTo,
            At.AddHours(-1));
        watches.Watches.Add(watch);

        return watch;
    }
}
