using System.Buffers.Text;
using System.Security.Cryptography;

using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Notifications;

[Trait("Category", "Unit")]
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

    [Fact(DisplayName = "D-9: the first browser, with no destination and no set of rules held, comes with the usual set of rules told to it")]
    public async Task TheFirstBrowserComesWithTheUsualSetOfRules()
    {
        PushSubscription subscription = Subscription("first");

        PushEnrolment enrolment = await Service.EnrolAsync(new NotificationName("A browser"), subscription, [4, 5, 6], At, Cancel);

        Assert.True(enrolment.Added);
        Assert.Equal(WebhookTarget.Push, enrolment.Destination.Target);
        Assert.Equal("A browser", enrolment.Destination.Name.Value);
        Assert.Equal(subscription.Fingerprint, enrolment.Destination.EndpointHash);
        Assert.Equal([4, 5, 6], enrolment.Destination.SealedUrl);
        Assert.True(enrolment.Destination.Enabled);
        Assert.NotNull(enrolment.Usual);
        Assert.Equal([enrolment.Destination.Id], enrolment.Usual.Destinations);
    }

    [Fact(DisplayName = "D-9: a browser registered beside another destination comes with no set of rules")]
    public async Task ALaterBrowserComesWithNoSetOfRules()
    {
        destinations.Destinations.Add(Unheld());

        PushEnrolment enrolment = await Service.EnrolAsync(new NotificationName("A browser"), Subscription("first"), [4, 5, 6], At, Cancel);

        Assert.True(enrolment.Added);
        Assert.Null(enrolment.Usual);
    }

    [Fact(DisplayName = "BR-ND-015: a browser registered again under the same endpoint stays one row, which takes the subscription and keeps its name and whether it is on")]
    public async Task ABrowserRegisteredAgainStaysOneRow()
    {
        NotificationDestination held = Browser("first", [4, 5, 6]);
        held.Disable(At.AddMinutes(1));
        held.NoteTurnedDown(At.AddMinutes(2));
        destinations.Destinations.Add(held);
        PushSubscription again = PushSubscription.Read(Subscription("first").EndpointAsWritten, OtherKey(), OtherSecret)!;

        PushEnrolment enrolment = await Service.EnrolAsync(new NotificationName("Another name"), again, [7, 8], At.AddHours(1), Cancel);

        Assert.False(enrolment.Added);
        Assert.Null(enrolment.Usual);
        Assert.Same(held, enrolment.Destination);
        Assert.Equal("A browser", held.Name.Value);
        Assert.False(held.Enabled);
        Assert.Equal([7, 8], held.SealedUrl);
        Assert.Equal(again.Fingerprint, held.EndpointHash);
        Assert.Null(held.LastFailure);
        Assert.Equal(At.AddHours(1), held.ChangedAt);
    }

    [Fact(DisplayName = "BR-ND-015: a browser registered again when it is the only destination and no set of rules is held makes no set of rules")]
    public async Task ABrowserRegisteredAgainMakesNoSetOfRules()
    {
        destinations.Destinations.Add(Browser("first", [4, 5, 6]));

        PushEnrolment enrolment = await Service.EnrolAsync(new NotificationName("A browser"), Subscription("first"), [7, 8], At.AddHours(1), Cancel);

        Assert.False(enrolment.Added);
        Assert.Null(enrolment.Usual);
    }

    [Fact(DisplayName = "BR-ND-015: the browser of a row takes another subscription and keeps its name and whether it is on")]
    public async Task TheBrowserOfARowTakesAnotherSubscription()
    {
        NotificationDestination held = Browser("first", [4, 5, 6]);
        destinations.Destinations.Add(held);
        PushSubscription other = Subscription("second");

        PushReplacement replacement = await Service.ReplaceAsync(held.Id, other, [7, 8], At.AddHours(1), Cancel);

        Assert.Null(replacement.Refusal);
        Assert.Same(held, replacement.Destination);
        Assert.Equal(other.Fingerprint, held.EndpointHash);
        Assert.Equal([7, 8], held.SealedUrl);
        Assert.Equal("A browser", held.Name.Value);
        Assert.True(held.Enabled);
    }

    [Fact(DisplayName = "BR-ND-015: the browser of a row takes the same endpoint again with other keys")]
    public async Task TheBrowserOfARowTakesTheSameEndpointAgain()
    {
        NotificationDestination held = Browser("first", [4, 5, 6]);
        destinations.Destinations.Add(held);
        PushSubscription again = PushSubscription.Read(Subscription("first").EndpointAsWritten, OtherKey(), OtherSecret)!;

        PushReplacement replacement = await Service.ReplaceAsync(held.Id, again, [7, 8], At.AddHours(1), Cancel);

        Assert.Null(replacement.Refusal);
        Assert.Equal([7, 8], held.SealedUrl);
    }

    [Fact(DisplayName = "BR-ND-015: the browser of a row does not take an endpoint another row is held under")]
    public async Task TheBrowserOfARowDoesNotTakeAnEndpointAnotherRowIsHeldUnder()
    {
        NotificationDestination held = Browser("first", [4, 5, 6]);
        NotificationDestination other = Browser("second", [1, 1]);
        destinations.Destinations.Add(held);
        destinations.Destinations.Add(other);

        PushReplacement replacement = await Service.ReplaceAsync(held.Id, Subscription("second"), [7, 8], At.AddHours(1), Cancel);

        Assert.Equal(PushReplacementRefusal.HeldByAnother, replacement.Refusal);
        Assert.Null(replacement.Destination);
        Assert.Equal([4, 5, 6], held.SealedUrl);
        Assert.Equal(Subscription("first").Fingerprint, held.EndpointHash);
        Assert.Equal([1, 1], other.SealedUrl);
    }

    [Fact(DisplayName = "BR-ND-015: a row that is not held, or is a webhook, takes no subscription")]
    public async Task ARowThatIsNotABrowserTakesNoSubscription()
    {
        NotificationDestination webhook = Unheld();
        destinations.Destinations.Add(webhook);

        PushReplacement unknown = await Service.ReplaceAsync(NotificationDestinationId.New(), Subscription("first"), [7, 8], At, Cancel);
        PushReplacement notABrowser = await Service.ReplaceAsync(webhook.Id, Subscription("first"), [7, 8], At, Cancel);

        Assert.Equal(PushReplacementRefusal.NoSuchBrowser, unknown.Refusal);
        Assert.Equal(PushReplacementRefusal.NoSuchBrowser, notABrowser.Refusal);
        Assert.Equal(WebhookTarget.Slack, webhook.Target);
        Assert.Equal([1, 2, 3], webhook.SealedUrl);
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

    private const string OtherSecret = "AAECAwQFBgcICQoLDA0ODw";

    private static NotificationDestination Browser(string registration, byte[] sealedSubscription)
        => NotificationDestination.Subscribe(
            NotificationDestinationId.New(),
            new NotificationName("A browser"),
            Subscription(registration),
            sealedSubscription,
            At);

    private static PushSubscription Subscription(string registration)
        => PushSubscription.Read(
            $"https://fcm.googleapis.com/fcm/send/made-up-{registration}",
            "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4",
            "BTBZMqHH6r4Tts7J_aSIgg")!;

    private static string OtherKey()
    {
        using ECDiffieHellman pair = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        ECParameters parameters = pair.ExportParameters(includePrivateParameters: false);

        return Base64Url.EncodeToString([0x04, .. parameters.Q.X!, .. parameters.Q.Y!]);
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
