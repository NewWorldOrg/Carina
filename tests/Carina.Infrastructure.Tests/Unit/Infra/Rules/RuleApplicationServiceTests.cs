using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Rules;
using Carina.Infrastructure.Tests.Fixtures.Reservations;
using Carina.Infrastructure.Tests.Fixtures.Rules;
using Carina.TestSupport;

using Puppis.Contracts;

namespace Carina.Infrastructure.Tests.Unit.Infra.Rules;

[Trait("Category", "Unit")]
public sealed class RuleApplicationServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const int Network = 4;

    private const int Carried = 32_736;

    private const int Beside = 32_737;

    private const int Listed = 1049;

    private const int Alongside = 1040;

    private const int NetworkForSimulcast = 9001;

    private const int Main = 901;

    private const int Sub = 902;

    private const int Third = 903;

    private const int Stream = 9002;

    private const int Event = 77;

    private static readonly DateTime NowForImpact = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan LongEnoughToTellAStallFromAWait = TimeSpan.FromSeconds(30);

    private const int Taken = 1;

    private const int BesideForAReturn = 2;

    [Fact]
    public async Task ARuleMakesAReservationForTheProgrammeItTakesAndNotForTheOneItLeaves()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill", name: "hills"));
        world.Guide(Broadcast(Listed, 1, "hill walking"), Broadcast(Listed, 2, "river fishing"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Reservation made = Assert.Single(run.Made);
        Assert.Equal("hill walking", made.SnapshotName);
        Assert.Equal(new EventId(1), made.EventId);
        Assert.Equal(["hill walking"], Named(world));
    }

    [Fact(DisplayName = "a run says how many rules it read against how many programmes and how long that took")]
    public async Task ARunSaysHowManyRulesItReadAgainstHowManyProgrammesAndHowLongThatTook()
    {
        World world = World.Of(applying: new SteppingClock(Now, TimeSpan.FromMilliseconds(250)));
        world.Rules.Rules.Add(Written("keyword=hill", name: "hills"));
        world.Rules.Rules.Add(Written("keyword=river", name: "rivers"));
        world.Rules.Rules.Add(Written("keyword=sea", name: "seas", enabled: false));
        world.Guide(
            Broadcast(Listed, 1, "hill walking"),
            Broadcast(Listed, 2, "river fishing"),
            Broadcast(Listed, 3, "a quiz"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(2, run.Rules);
        Assert.Equal(3, run.Read);
        Assert.Equal(TimeSpan.FromMilliseconds(250), run.Took);
    }

    [Fact]
    public async Task AReservationARuleMadeCarriesTheRuleTheMarginsAndThePriorityItWasWrittenWith()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill", priority: 40, marginBefore: 30, marginAfter: 60);
        world.Rules.Rules.Add(rule);
        world.Guide(Broadcast(Listed, 1, "hill walking"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Reservation made = Assert.Single(run.Made);
        Assert.Equal(rule.Id, made.RuleId);
        Assert.Equal(40, made.Priority.Value);
        Assert.Equal(TimeSpan.FromSeconds(30), made.MarginBefore.Value);
        Assert.Equal(TimeSpan.FromSeconds(60), made.MarginAfter.Value);
        Assert.True(made.IsRuleBorn);
    }

    [Fact]
    public async Task AReservationARuleMadeIsSettledOnTheTunersRatherThanLeftUndecided()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(Listed, 1, "hill walking"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(ReservationState.Scheduled, Assert.Single(run.Made).State);
        Assert.Equal(1, world.Write.Committed);
    }

    [Fact]
    public async Task ARuleThatIsOffMakesNothingThoughTheSameQueryWouldTakeIt()
    {
        World taking = World.Of();
        taking.Rules.Rules.Add(Written("keyword=hill"));
        taking.Guide(Broadcast(Listed, 1, "hill walking"));

        World leaving = World.Of();
        leaving.Rules.Rules.Add(Written("keyword=hill", enabled: false));
        leaving.Guide(Broadcast(Listed, 1, "hill walking"));

        Assert.Single((await taking.Applying.EverythingAsync(Cancel)).Made);
        Assert.Empty((await leaving.Applying.EverythingAsync(Cancel)).Made);
    }

    [Fact]
    public async Task AProgrammeThatIsAlreadyReservedIsNotReservedTwice()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        Programme already = Broadcast(Listed, 1, "hill walking");
        world.Guide(already);
        world.Reservations.Standing(Standing(already, ReservationState.Scheduled, RuleId.New()));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Empty(run.Made);
        Assert.Single(world.Reservations.Held);
    }

    [Fact]
    public async Task AReservationSomebodyCancelledIsNotMadeAgainThoughTheRuleStillTakesTheProgramme()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        Programme cancelled = Broadcast(Listed, 1, "hill walking");
        world.Guide(cancelled, Broadcast(Listed, 2, "hillside"));
        world.Reservations.Standing(Standing(cancelled, ReservationState.Cancelled, rule.Id));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(["hillside"], [.. run.Made.Select(reservation => reservation.SnapshotName)]);
    }

    [Fact]
    public async Task TwoChannelsCarryingTheSameEventNumberEachGetTheirOwnReservation()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(Listed, 7, "hill walking"), Broadcast(Alongside, 7, "hillside"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(2, run.Made.Count);
        Assert.Equal(["hill walking", "hillside"], Named(world));
    }

    [Fact]
    public async Task AnEventNumberUsedAgainAfterItsBroadcastWasRecordedGetsItsOwnReservation()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        Programme recorded = Broadcast(Listed, 7, "hill walking", startsAt: Now.AddMinutes(-30));
        world.Reservations.Standing(
            Standing(recorded, ReservationState.Scheduled, rule.Id, startedAt: recorded.StartsAt));
        world.Guide(Broadcast(Listed, 7, "hillside", startsAt: Now.AddDays(7)));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(["hillside"], [.. run.Made.Select(reservation => reservation.SnapshotName)]);
        Assert.Equal(["hill walking", "hillside"], Named(world));
    }

    [Fact]
    public async Task AProgrammeThatIsAlreadyOverIsNotReservedThoughOneStillToComeIs()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill&from=2026-08-01T00:00:00Z&to=2026-08-30T00:00:00Z"));
        world.Guide(
            Broadcast(Listed, 1, "hill walking", startsAt: Now.AddHours(-4)),
            Broadcast(Listed, 2, "hillside", startsAt: Now.AddHours(2)));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(["hillside"], Named(world));
    }

    [Fact]
    public async Task OnlyWhatChangedPastTheRevisionIsRead()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(
            Broadcast(Listed, 1, "hill walking", revision: 1),
            Broadcast(Listed, 2, "hillside", revision: 7));

        RuleApplicationRun run = await world.Applying.SinceAsync(1, Cancel);

        Assert.Equal(1, run.Read);
        Assert.Equal(7, run.Revision);
        Assert.Equal(["hillside"], Named(world));
    }

    [Fact]
    public async Task ASweepReadsEverythingThoughAnIncrementalRunFromTheSameRevisionReadsNothing()
    {
        World swept = World.Of();
        swept.Rules.Rules.Add(Written("keyword=hill"));
        swept.Guide(Broadcast(Listed, 1, "hill walking", revision: 3));

        World stepped = World.Of();
        stepped.Rules.Rules.Add(Written("keyword=hill"));
        stepped.Guide(Broadcast(Listed, 1, "hill walking", revision: 3));

        Assert.Equal(1, (await swept.Applying.EverythingAsync(Cancel)).Read);
        Assert.Equal(0, (await stepped.Applying.SinceAsync(3, Cancel)).Read);
    }

    [Fact]
    public async Task ARunThatReadsNothingLeavesTheRevisionWhereItWas()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(Listed, 1, "hill walking", revision: 3));

        Assert.Equal(9, (await world.Applying.SinceAsync(9, Cancel)).Revision);
    }

    [Fact]
    public async Task ASweepLargerThanOnePageStillSeesEverythingRatherThanTheFirstPage()
    {
        World world = World.Of(new RuleApplicationSettings { Rows = 2 });
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(
            Broadcast(Listed, 1, "hill one", revision: 1),
            Broadcast(Listed, 2, "hill two", revision: 2),
            Broadcast(Listed, 3, "hill three", revision: 3),
            Broadcast(Listed, 4, "hill four", revision: 4),
            Broadcast(Listed, 5, "hill five", revision: 5));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(5, run.Read);
        Assert.Equal(5, run.Made.Count);
        Assert.Equal(5, run.Revision);
    }

    [Fact]
    public async Task AProgrammeThatVanishedTakesItsReservationWithItOnASweepButNotOnAnIncrementalRun()
    {
        World swept = Vanished();
        World stepped = Vanished();

        RuleApplicationRun sweeping = await swept.Applying.EverythingAsync(Cancel);
        RuleApplicationRun stepping = await stepped.Applying.SinceAsync(500, Cancel);

        Assert.Single(sweeping.Withdrawn);
        Assert.Empty(swept.Reservations.Held);
        Assert.Empty(stepping.Withdrawn);
        Assert.Single(stepped.Reservations.Held);
    }

    [Fact]
    public async Task AProgrammeThatStoppedMatchingTakesItsReservationWithItWhileTheOneStillMatchingKeepsIt()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        Programme kept = Broadcast(Listed, 1, "hill walking", revision: 1);
        Programme drifted = Broadcast(Listed, 2, "river fishing", revision: 2);
        world.Guide(kept, drifted);
        world.Reservations.Standing(
            Standing(kept, ReservationState.Scheduled, rule.Id),
            Standing(drifted, ReservationState.Scheduled, rule.Id));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(["river fishing"], [.. run.Withdrawn.Select(reservation => reservation.SnapshotName)]);
        Assert.Equal(["hill walking"], Named(world));
    }

    [Fact]
    public async Task TheReservationThatGoesIsTheOneOnTheChannelThatStoppedCarryingTheNumber()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        Programme kept = Broadcast(Listed, 7, "hill walking");
        Programme drifted = Broadcast(Alongside, 7, "river fishing");
        world.Guide(kept, drifted);
        world.Reservations.Standing(
            Standing(kept, ReservationState.Scheduled, rule.Id),
            Standing(drifted, ReservationState.Scheduled, rule.Id));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(["river fishing"], [.. run.Withdrawn.Select(reservation => reservation.SnapshotName)]);
        Assert.Equal(["hill walking"], Named(world));
    }

    [Fact]
    public async Task TheReservationThatGoesIsTheOneAtTheStartThatStoppedCarryingTheNumber()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        Programme kept = Broadcast(Listed, 7, "hill walking", startsAt: Now.AddHours(2));
        Programme drifted = Broadcast(Listed, 7, "river fishing", startsAt: Now.AddDays(7));
        world.Guide(kept, drifted);
        world.Reservations.Standing(
            Standing(kept, ReservationState.Scheduled, rule.Id),
            Standing(drifted, ReservationState.Scheduled, rule.Id));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(["river fishing"], [.. run.Withdrawn.Select(reservation => reservation.SnapshotName)]);
        Assert.Equal(["hill walking"], Named(world));
    }

    [Fact]
    public async Task ASweepOverAGuideDiscardedSinceTheStreamWasLastCollectedTakesNothingOut()
    {
        World world = Vanished();
        await DiscardedAsync(world, Now.AddMinutes(-30));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Empty(run.Withdrawn);
        Assert.Equal(["hill walking"], Named(world));
    }

    [Fact]
    public async Task ASweepAfterTheStreamWasCollectedAgainTakesOutWhatTheNewGuideNoLongerCarries()
    {
        World world = Vanished();
        await DiscardedAsync(world, Now.AddHours(-2));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(["hill walking"], [.. run.Withdrawn.Select(reservation => reservation.SnapshotName)]);
    }

    [Fact]
    public async Task APassThatWithdrewAReservationTellsTheScreensTheReservationsMoved()
    {
        World world = Vanished();

        await world.Applying.EverythingAsync(Cancel);

        Assert.Contains(AppEventName.Reservations, world.Events.Signalled);
    }

    [Fact]
    public async Task APassThatLeftEveryReservationWhereItWasTellsTheScreensNothing()
    {
        World world = Vanished(VisitOutcome.Incomplete);

        await world.Applying.EverythingAsync(Cancel);
        world.Events.Signalled.Clear();

        await world.Applying.EverythingAsync(Cancel);

        Assert.Empty(world.Events.Signalled);
    }

    [Fact]
    public async Task WithdrawingSendsWhatIsLeftBackThroughTheOnePlaceThatWorksOutTheTuners()
    {
        World withdrawing = Vanished();
        World leaving = Vanished(VisitOutcome.Incomplete);
        Reservation going = withdrawing.Reservations.Held[0];

        await withdrawing.Applying.EverythingAsync(Cancel);
        await leaving.Applying.EverythingAsync(Cancel);

        Assert.Contains($"withdraw {going.Id.Value}", withdrawing.Reservations.Wrote, StringComparer.Ordinal);
        Assert.Equal(1, withdrawing.Seating.Reads);
        Assert.Equal(0, leaving.Seating.Reads);
    }

    [Fact]
    public async Task AReservationNoRuleMadeIsLeftAloneWhileTheRuleBornOneBesideItGoes()
    {
        World world = Vanished();
        Programme gone = Broadcast(Alongside, 9, "somebody asked for this", revision: 4);
        world.Reservations.Standing(Standing(gone, ReservationState.Scheduled, null));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Single(run.Withdrawn);
        Assert.Equal(["somebody asked for this"], Named(world));
    }

    [Fact]
    public async Task AReservationThatIsAlreadyRecordingIsLeftAloneWhileTheOneNotYetStartedGoes()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        Programme recording = Broadcast(Listed, 1, "hill walking");
        Programme waiting = Broadcast(Alongside, 2, "hillside");
        world.Reservations.Standing(
            Standing(recording, ReservationState.Scheduled, rule.Id, startedAt: Now.AddMinutes(-5)),
            Standing(waiting, ReservationState.Scheduled, rule.Id));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(["hillside"], [.. run.Withdrawn.Select(reservation => reservation.SnapshotName)]);
        Assert.Equal(["hill walking"], Named(world));
    }

    [Theory]
    [InlineData(ReservationState.Scheduled, true)]
    [InlineData(ReservationState.Conflict, true)]
    [InlineData(ReservationState.Cancelled, false)]
    [InlineData(ReservationState.Missed, false)]
    public async Task DroppingARuleTakesWhatIsStandingAndLeavesWhatIsAlreadyARecordOfWhatHappened(
        ReservationState state,
        bool leaves)
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        world.Reservations.Standing(Standing(Broadcast(Listed, 1, "hill walking"), state, rule.Id));

        IReadOnlyList<Reservation> dropped = await world.Applying.DroppedAsync(rule.Id, Cancel);

        Assert.Equal(leaves, dropped.Count is 1);
        Assert.Equal(leaves, world.Reservations.Held.Count is 0);
    }

    [Fact]
    public async Task DroppingARuleLeavesWhatAnotherRuleMade()
    {
        World world = World.Of();
        Rule going = Written("keyword=hill", identifier: 1);
        Rule staying = Written("keyword=river", identifier: 2);
        world.Rules.Rules.Add(going);
        world.Rules.Rules.Add(staying);
        world.Visited(VisitOutcome.Complete);
        world.Reservations.Standing(
            Standing(Broadcast(Listed, 1, "hill walking"), ReservationState.Scheduled, going.Id),
            Standing(Broadcast(Listed, 2, "river fishing"), ReservationState.Scheduled, staying.Id));

        await world.Applying.DroppedAsync(going.Id, Cancel);

        Assert.Equal(["river fishing"], Named(world));
    }

    [Theory]
    [InlineData(VisitOutcome.Complete, true)]
    [InlineData(VisitOutcome.BasicOnly, true)]
    [InlineData(VisitOutcome.Incomplete, false)]
    [InlineData(VisitOutcome.Interrupted, false)]
    [InlineData(VisitOutcome.NoLock, false)]
    [InlineData(VisitOutcome.NoBytes, false)]
    public async Task AReservationGoesOnlyWhenTheStreamItCameFromWasCollectedToTheEnd(
        VisitOutcome outcome,
        bool leaves)
    {
        World world = Vanished(outcome);

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(leaves, run.Withdrawn.Count is 1);
        Assert.Equal(leaves, world.Reservations.Held.Count is 0);
    }

    [Fact]
    public async Task AReservationOnAStreamNobodyHasVisitedStays()
    {
        World world = Vanished();
        world.Visits.Visits.Clear();

        Assert.Empty((await world.Applying.EverythingAsync(Cancel)).Withdrawn);
        Assert.Single(world.Reservations.Held);
    }

    [Fact]
    public async Task AReservationOnAServiceNoStreamCarriesStays()
    {
        World world = Vanished();
        world.Streams.Carried.Clear();
        world.Streams.Carried.Add(Terrestrial(Beside, Alongside));

        Assert.Empty((await world.Applying.EverythingAsync(Cancel)).Withdrawn);
        Assert.Single(world.Reservations.Held);
    }

    [Fact]
    public async Task AReservationStartingInsideTheGraceStaysWhileTheOneOutsideItGoes()
    {
        World world = World.Of(new RuleApplicationSettings { Grace = TimeSpan.FromMinutes(10) });
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        world.Reservations.Standing(
            Standing(Broadcast(Listed, 1, "soon"), ReservationState.Scheduled, rule.Id, startAt: Now.AddMinutes(4)),
            Standing(Broadcast(Listed, 2, "later"), ReservationState.Scheduled, rule.Id, startAt: Now.AddMinutes(40)));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(["later"], [.. run.Withdrawn.Select(reservation => reservation.SnapshotName)]);
        Assert.Equal(["soon"], Named(world));
    }

    [Fact]
    public async Task TheGraceIsMeasuredFromWhenTheRecorderStartsRatherThanWhenTheProgrammeDoes()
    {
        World world = World.Of(new RuleApplicationSettings { Grace = TimeSpan.FromMinutes(10) });
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        world.Reservations.Standing(
            Standing(
                Broadcast(Listed, 1, "early bird"),
                ReservationState.Scheduled,
                rule.Id,
                startAt: Now.AddMinutes(15),
                marginBefore: 600));

        Assert.Empty((await world.Applying.EverythingAsync(Cancel)).Withdrawn);
        Assert.Single(world.Reservations.Held);
    }

    [Fact]
    public async Task ARuleSomebodyTurnedOffTakesItsReservationsBackInsideTheGraceToo()
    {
        World standing = World.Of(new RuleApplicationSettings { Grace = TimeSpan.FromMinutes(10) });
        Rule kept = Written("keyword=hill");
        standing.Rules.Rules.Add(kept);
        standing.Visited(VisitOutcome.Complete);
        standing.Reservations.Standing(
            Standing(Broadcast(Listed, 1, "soon"), ReservationState.Scheduled, kept.Id, startAt: Now.AddMinutes(4)));

        World turned = World.Of(new RuleApplicationSettings { Grace = TimeSpan.FromMinutes(10) });
        Rule off = Written("keyword=hill", enabled: false);
        turned.Rules.Rules.Add(off);
        turned.Visited(VisitOutcome.Complete);
        turned.Reservations.Standing(
            Standing(Broadcast(Listed, 1, "soon"), ReservationState.Scheduled, off.Id, startAt: Now.AddMinutes(4)));

        Assert.Empty((await standing.Applying.EverythingAsync(Cancel)).Withdrawn);
        Assert.Single((await turned.Applying.EverythingAsync(Cancel)).Withdrawn);
    }

    [Fact]
    public async Task ARuleNobodyCanFindAnyMoreTakesItsReservationsBackInsideTheGraceToo()
    {
        World world = World.Of(new RuleApplicationSettings { Grace = TimeSpan.FromMinutes(10) });
        world.Visited(VisitOutcome.Complete);
        world.Reservations.Standing(
            Standing(Broadcast(Listed, 1, "soon"), ReservationState.Scheduled, RuleId.New(), startAt: Now.AddMinutes(4)));

        Assert.Single((await world.Applying.EverythingAsync(Cancel)).Withdrawn);
    }

    [Fact]
    public async Task ARuleWhoseQueryCannotBeReadIsTurnedOffAndWrittenDownWhileTheSoundOneKeepsWorking()
    {
        World world = World.Of();
        Rule broken = Written("keyword=h", identifier: 1, name: "one letter", priority: 90);
        Rule sound = Written("keyword=hill", identifier: 2, name: "sound", priority: 10);
        world.Rules.Rules.Add(broken);
        world.Rules.Rules.Add(sound);
        world.Guide(Broadcast(Listed, 1, "hill walking"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal("one letter", Assert.Single(run.TurnedOff).Name);
        Assert.Equal([broken.Id.Value], world.Rules.Saved);
        Assert.False(broken.Enabled);
        Assert.Equal(["hill walking"], Named(world));
    }

    [Fact]
    public async Task ARuleThatCouldNotBeWorkedOutIsReportedRatherThanStoppingTheRunAndTheRestStillWork()
    {
        World world = Broken();
        world.Guide(Broadcast(Listed, 1, "hill walking"), Broadcast(Listed, 2, "river fishing"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal("names a broadcast type", Assert.Single(run.Faulted).Rule.Name);
        Assert.Equal(["river fishing"], Named(world));
    }

    [Fact]
    public async Task ARuleThatCouldNotBeWorkedOutIsNotTurnedOff()
    {
        World world = Broken();
        world.Guide(Broadcast(Listed, 1, "hill walking"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Empty(run.TurnedOff);
        Assert.Empty(world.Rules.Saved);
        Assert.True(world.Rules.Rules[0].Enabled);
    }

    [Fact]
    public async Task ARuleThatCouldNotBeWorkedOutKeepsItsReservationsWhileASoundRuleLosesTheOnesItDropped()
    {
        World world = Broken();
        world.Visited(VisitOutcome.Complete);
        Rule broken = world.Rules.Rules[0];
        Rule sound = world.Rules.Rules[1];
        world.Reservations.Standing(
            Standing(Broadcast(Listed, 1, "kept by the broken rule"), ReservationState.Scheduled, broken.Id),
            Standing(Broadcast(Alongside, 2, "dropped by the sound rule"), ReservationState.Scheduled, sound.Id));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(
            ["dropped by the sound rule"],
            [.. run.Withdrawn.Select(reservation => reservation.SnapshotName)]);
        Assert.Equal(["kept by the broken rule"], Named(world));
    }

    [Fact]
    public async Task TheChannelsAndTheGuideAreReadOnceForAWholeRunRatherThanOncePerRule()
    {
        World world = World.Of();

        for (int identifier = 1; identifier <= 6; identifier++)
        {
            world.Rules.Rules.Add(Written(
                identifier % 2 is 0 ? "keyword=hill" : "keyword=hill&type=IsdbT",
                identifier: identifier));
        }

        world.Guide(Broadcast(Listed, 1, "hill walking"));

        await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(6, world.Rules.Rules.Count);
        Assert.Equal(1, world.Services.Reads);
        Assert.Equal(2, world.Streams.Reads);
    }

    [Fact]
    public async Task BrRd010ARuleTakingBothListingsOfAMovedBroadcastReservesItOnceOnThePrimary()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(
            Linked(Listed, 1, "hill walking", related: [Link(Alongside, 2, RelationKind.Moved)]),
            Linked(Alongside, 2, "hill walking", running: ProgrammeRunning.Running));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Reservation made = Assert.Single(run.Made);
        Assert.Equal(Alongside, made.ServiceId.Value);
        Assert.Equal(BroadcastGroupRole.MovementPrimary, made.BroadcastGroupRole);
        Assert.Equal(new BroadcastGroupKey($"movement:{Network}-{Alongside}-2"), made.BroadcastGroupKey);
        Assert.Equal(1, run.Moved);
    }

    [Fact]
    public async Task BrRd010ARuleThatOnlyReachesTheSuppressedListingStillReservesTheBroadcastOnThePrimary()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written($"keyword=hill&channel={Network}-{Listed}"));
        world.Guide(
            Linked(Listed, 1, "hill walking", related: [Link(Alongside, 2, RelationKind.Moved)]),
            Linked(Alongside, 2, "hill walking", running: ProgrammeRunning.Running));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(Alongside, Assert.Single(run.Made).ServiceId.Value);
    }

    [Fact]
    public async Task BrRd010ARuleTakingOneSegmentOfARelayReservesEverySegmentUnderOneKey()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(
            Linked(Listed, 1, "hill walking", related: [Link(Alongside, 2, RelationKind.Relayed)]),
            Linked(Alongside, 2, "continued", startsAt: Now.AddHours(3)));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal([1, 2], run.Made.Select(reservation => reservation.EventId.Value).Order());
        Assert.All(run.Made, reservation => Assert.Equal(BroadcastGroupRole.RelaySegment, reservation.BroadcastGroupRole));
        Assert.Single(run.Made.Select(reservation => reservation.BroadcastGroupKey).Distinct());
    }

    [Fact]
    public async Task BrRd010AnIncrementThatReadsOneSegmentOfARelayReadsTheRestOfItToo()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(
            Linked(Listed, 1, "hill walking", revision: 9, related: [Link(Alongside, 2, RelationKind.Relayed)]),
            Linked(Alongside, 2, "continued", startsAt: Now.AddHours(3), revision: 1));

        RuleApplicationRun run = await world.Applying.SinceAsync(5, Cancel);

        Assert.Equal([1, 2], run.Made.Select(reservation => reservation.EventId.Value).Order());
    }

    [Fact]
    public async Task BrRd010TheRestOfARelayIsNotTakenBackOnceTheSegmentBeforeItIsBeingRecorded()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        BroadcastGroupKey key = new($"relay:{Network}-{Listed}-1");
        Programme first = Linked(Listed, 1, "hill walking", startsAt: Now.AddMinutes(-30), related: [Link(Alongside, 2, RelationKind.Relayed)]);
        Programme rest = Linked(Alongside, 2, "continued", startsAt: Now.AddHours(3));
        world.Guide(first, rest);
        Reservation recording = Standing(first, ReservationState.Scheduled, rule.Id, startedAt: Now.AddMinutes(-31));
        Reservation after = Standing(rest, ReservationState.Scheduled, rule.Id);
        recording.Regroup(key, BroadcastGroupRole.RelaySegment);
        after.Regroup(key, BroadcastGroupRole.RelaySegment);
        world.Reservations.Standing(recording, after);
        world.Guide(Linked(Listed, 3, "something else", startsAt: Now.AddHours(5)));
        world.Programmes.Programmes.Remove(first);
        world.Guide(Linked(Listed, 1, "not a match any more", startsAt: Now.AddMinutes(-30), related: [Link(Alongside, 2, RelationKind.Relayed)]));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Empty(run.Withdrawn);
        Assert.Equal(2, world.Reservations.Held.Count);
    }

    [Fact]
    public async Task BrRd010ASegmentARuleNoLongerTakesIsTakenBackWhenNothingElseOfTheRelayHolds()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        BroadcastGroupKey key = new($"relay:{Network}-{Listed}-1");
        Programme rest = Linked(Alongside, 2, "continued", startsAt: Now.AddHours(3));
        world.Guide(Linked(Listed, 1, "not a match", related: [Link(Alongside, 2, RelationKind.Relayed)]), rest);
        Reservation after = Standing(rest, ReservationState.Scheduled, rule.Id);
        after.Regroup(key, BroadcastGroupRole.RelaySegment);
        world.Reservations.Standing(after);

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal([after.Id], run.Withdrawn.Select(reservation => reservation.Id));
    }

    private static RelatedProgramme Link(int service, int carried, RelationKind kind)
        => new(Network, service, carried, kind);

    private static Programme Linked(
        int service,
        int carried,
        string name,
        DateTime? startsAt = null,
        long revision = 1,
        ProgrammeRunning running = ProgrammeRunning.Undetermined,
        IReadOnlyList<RelatedProgramme>? related = null)
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(Carried),
            startsAt ?? Now.AddHours(2),
            (startsAt ?? Now.AddHours(2)).AddHours(1),
            name,
            "a summary",
            false,
            Now,
            related: related,
            revision: revision,
            running: running);

    private static string[] Named(World world)
        => [.. world.Reservations.Held.Select(reservation => reservation.SnapshotName).Order(StringComparer.Ordinal)];

    private static async Task DiscardedAsync(World world, DateTime at)
    {
        CollectionEpoch epoch = await world.Epochs.ReadAsync(at, Cancel);

        epoch.Advance(at);

        await world.Epochs.SaveAsync(epoch, Cancel);
    }

    private static World Vanished(VisitOutcome outcome = VisitOutcome.Complete)
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(outcome);
        world.Reservations.Standing(
            Standing(Broadcast(Listed, 1, "hill walking"), ReservationState.Scheduled, rule.Id));

        return world;
    }

    private static World Broken()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill&type=IsdbT", identifier: 1, name: "names a broadcast type", priority: 90));
        world.Rules.Rules.Add(Written("keyword=river", identifier: 2, name: "names no broadcast type", priority: 10));
        world.Streams.Carried.Clear();
        world.Streams.Carried.Add(new BroadcastStream(
            new NetworkId(Network),
            new TransportStreamId(Carried),
            null!,
            [new ServiceId(Listed), new ServiceId(Alongside)]));

        return world;
    }

    private static Rule Written(
        string query,
        int priority = 10,
        int identifier = 1,
        string name = "a rule",
        bool enabled = true,
        int marginBefore = 0,
        int marginAfter = 0,
        bool encodeWhenRecorded = true)
        => Rule.Draft(
            new RuleId(new Guid($"{identifier:x8}-0000-0000-0000-000000000000")),
            name,
            new RuleQuery(query),
            new Priority(priority),
            enabled,
            Margin.OfSeconds(marginBefore),
            Margin.OfSeconds(marginAfter),
            Now.AddDays(-30),
            encodeWhenRecorded);

    [Fact(DisplayName = "a reservation a rule made carries what that rule asked about encoding")]
    public async Task AReservationARuleMadeCarriesWhatTheRuleAskedAboutEncoding()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill", encodeWhenRecorded: false));
        world.Guide(Broadcast(Listed, 1, "hill walking"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.False(Assert.Single(run.Made).EncodeWhenRecorded);
    }

    [Fact(DisplayName = "a rule that says nothing about encoding leaves what it makes asking for one")]
    public async Task ARuleThatSaysNothingLeavesWhatItMakesAskingForAnEncode()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(Listed, 1, "hill walking"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.True(Assert.Single(run.Made).EncodeWhenRecorded);
    }

    [Fact]
    public async Task AReservationARuleMadeCarriesTheSoundTheBroadcastAnnounced()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(Listed, 1, "hill walking", audio: AudioMode.DualMono, sounds: 2));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Reservation made = Assert.Single(run.Made);

        Assert.Equal(AudioMode.DualMono, made.SnapshotAudio);
        Assert.Equal(2, made.SnapshotSounds);
    }

    [Fact]
    public async Task ADraftSaysWhatItWouldTakeWithoutWritingAnything()
    {
        WorldForRehearsal world = WorldForRehearsal.Of();
        world.Guide(BroadcastForRehearsal(1, "hill walking"), BroadcastForRehearsal(2, "river fishing"));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(Draft("keyword=hill"), Cancel);

        Assert.NotNull(rehearsed);
        Assert.Equal(["hill walking"], [.. rehearsed.Taking.Select(taken => taken.Programme.Name)]);
        Assert.Single(rehearsed.Making);
        Assert.Empty(world.Reservations.Held);
        Assert.Equal(0, world.Write.Committed);
    }

    [Fact]
    public async Task ADraftWhoseConditionsAreAllEmptyIsNotRehearsedAtAll()
    {
        WorldForRehearsal world = WorldForRehearsal.Of();
        world.Guide(BroadcastForRehearsal(1, "hill walking"));

        Assert.Null(await world.Applying.RehearsedAsync(Draft("sort=Name"), Cancel));
    }

    [Fact]
    public async Task AnUnsavedDraftIsWeighedAgainstTheReservationsThatAlreadyStand()
    {
        WorldForRehearsal world = Crowded(seats: 1);

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(Draft("keyword=hill"), Cancel);

        Assert.NotNull(rehearsed);
        Assert.True(rehearsed.Settled.Settled);
        Assert.Single(rehearsed.Making);
        Assert.Single(rehearsed.Settled.Plan.Contended);
    }

    [Fact]
    public async Task TheSameDraftClashesWithNothingWhenThereIsASeatForItAsWell()
    {
        WorldForRehearsal world = Crowded(seats: 2);

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(Draft("keyword=hill"), Cancel);

        Assert.NotNull(rehearsed);
        Reservation proposed = Assert.Single(rehearsed.Making);
        Assert.Equal(AllocationVerdict.Secured, rehearsed.Settled.Plan.For(proposed.Id).Verdict);
        Assert.Empty(rehearsed.Settled.Plan.Contended);
    }

    [Fact]
    public async Task WhatAlreadyStandsClashesWithNothingUntilTheDraftIsWeighedBesideIt()
    {
        WorldForRehearsal world = Crowded(seats: 1);

        SchedulingRun without = await world.Scheduling.PreviewAsync([], Cancel);

        Assert.Empty(without.Plan.Contended);
    }

    [Fact]
    public async Task ABroadcastCarriedAsAShadowIsCountedOutRatherThanTaken()
    {
        WorldForRehearsal world = WorldForRehearsal.Of();
        world.Guide(
            BroadcastForRehearsal(1, "hill walking"),
            BroadcastForRehearsal(2, "hill walking", Alongside, shadow: true));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(Draft("keyword=hill"), Cancel);

        Assert.NotNull(rehearsed);
        Assert.Single(rehearsed.Taking);
        Assert.Equal(1, rehearsed.Shadowed);
    }

    [Fact]
    public async Task NothingIsCountedOutWhenNoBroadcastIsCarriedAsAShadow()
    {
        WorldForRehearsal world = WorldForRehearsal.Of();
        world.Guide(BroadcastForRehearsal(1, "hill walking"), BroadcastForRehearsal(2, "hill climbing", Alongside));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(Draft("keyword=hill"), Cancel);

        Assert.NotNull(rehearsed);
        Assert.Equal(2, rehearsed.Taking.Count);
        Assert.Equal(0, rehearsed.Shadowed);
    }

    [Fact]
    public async Task BrRd010AListingAMovedBroadcastSuppressesIsCountedOutAndThePrimaryIsTakenInstead()
    {
        WorldForRehearsal world = WorldForRehearsal.Of();
        world.Guide(
            LinkedForRehearsal(1, "hill walking", Listed, Now.AddHours(3), related: [new RelatedProgramme(Network, Alongside, 2, RelationKind.Moved)]),
            LinkedForRehearsal(2, "hill walking", Alongside, Now.AddHours(3), running: ProgrammeRunning.Running));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(Draft("keyword=hill"), Cancel);

        Assert.NotNull(rehearsed);
        Assert.Equal(Alongside, Assert.Single(rehearsed.Taking).Programme.ServiceId.Value);
        Assert.Equal(Alongside, Assert.Single(rehearsed.Making).ServiceId.Value);
        Assert.Equal(1, rehearsed.Moved);
    }

    [Fact]
    public async Task BrRd010ADraftTakingOneSegmentOfARelayIsShownTakingEverySegment()
    {
        WorldForRehearsal world = WorldForRehearsal.Of();
        world.Guide(
            LinkedForRehearsal(1, "hill walking", Listed, Now.AddHours(3), related: [new RelatedProgramme(Network, Alongside, 2, RelationKind.Relayed)]),
            LinkedForRehearsal(2, "continued", Alongside, Now.AddHours(4)));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(Draft("keyword=hill"), Cancel);

        Assert.NotNull(rehearsed);
        Assert.Equal(["hill walking", "continued"], [.. rehearsed.Taking.Select(taken => taken.Programme.Name)]);
        Assert.Equal(2, rehearsed.Making.Count);
        Assert.Equal(0, rehearsed.Moved);
    }

    [Fact]
    public async Task AProgrammeAlreadyReservedIsNotCountedAsOneMoreToMake()
    {
        WorldForRehearsal world = WorldForRehearsal.Of();
        Programme already = BroadcastForRehearsal(1, "hill walking");
        world.Guide(already, BroadcastForRehearsal(2, "hillside"));
        world.Reservations.Standing(StandingForRehearsal(already, ruleId: RuleId.New()));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(Draft("keyword=hill"), Cancel);

        Assert.NotNull(rehearsed);
        Assert.Equal(2, rehearsed.Taking.Count);
        Assert.Equal(["hillside"], [.. rehearsed.Making.Select(made => made.SnapshotName)]);
        Assert.Single(rehearsed.ChangingHands);
    }

    [Fact]
    public async Task WhatSomebodyBookedByHandIsNotCountedAsChangingHands()
    {
        WorldForRehearsal world = WorldForRehearsal.Of();
        Programme already = BroadcastForRehearsal(1, "hill walking");
        world.Guide(already);
        world.Reservations.Standing(StandingForRehearsal(already));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(Draft("keyword=hill"), Cancel);

        Assert.NotNull(rehearsed);
        Assert.Single(rehearsed.Taking);
        Assert.Empty(rehearsed.Making);
        Assert.Empty(rehearsed.ChangingHands);
    }

    [Fact]
    public async Task WhatTheDraftItselfAlreadyReservedIsNotCountedAsChangingHands()
    {
        WorldForRehearsal world = WorldForRehearsal.Of();
        Rule draft = Draft("keyword=hill");
        world.Rules.Rules.Add(draft);
        Programme already = BroadcastForRehearsal(1, "hill walking");
        world.Guide(already);
        world.Reservations.Standing(StandingForRehearsal(already, ruleId: draft.Id));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(draft, Cancel);

        Assert.NotNull(rehearsed);
        Assert.Empty(rehearsed.Making);
        Assert.Empty(rehearsed.ChangingHands);
        Assert.Empty(rehearsed.Withdrawing);
    }

    [Fact]
    public async Task WhatTheDraftNoLongerTakesIsCountedAsComingBack()
    {
        WorldForRehearsal world = Collected();
        Rule saved = Draft("keyword=hill");
        world.Rules.Rules.Add(saved);
        Programme leaving = BroadcastForRehearsal(1, "hill walking");
        world.Guide(leaving, BroadcastForRehearsal(2, "river fishing"));
        world.Reservations.Standing(StandingForRehearsal(leaving, ruleId: saved.Id));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(
            Draft("keyword=river", identifier: saved.Id),
            Cancel);

        Assert.NotNull(rehearsed);
        Assert.Equal(["river fishing"], [.. rehearsed.Making.Select(made => made.SnapshotName)]);
        Assert.Equal(["hill walking"], [.. rehearsed.Withdrawing.Select(going => going.SnapshotName)]);
    }

    [Fact]
    public async Task ARuleThatComesFirstKeepsWhatTheDraftWouldOtherwiseTake()
    {
        WorldForRehearsal world = WorldForRehearsal.Of();
        world.Rules.Rules.Add(Rule.Draft(
            new RuleId(new Guid("00000009-0000-0000-0000-000000000000")),
            "ahead of the draft",
            new RuleQuery("keyword=hill"),
            new Priority(90),
            true,
            Margin.None,
            Margin.None,
            Now.AddDays(-30)));
        world.Guide(BroadcastForRehearsal(1, "hill walking"));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(
            Draft("keyword=hill", priority: 10),
            Cancel);

        Assert.NotNull(rehearsed);
        Assert.Empty(rehearsed.Taking);
        Assert.Empty(rehearsed.Making);
    }

    [Fact]
    public async Task ADraftThatComesFirstTakesItFromTheRuleBehindIt()
    {
        WorldForRehearsal world = WorldForRehearsal.Of();
        world.Rules.Rules.Add(Rule.Draft(
            new RuleId(new Guid("00000009-0000-0000-0000-000000000000")),
            "behind the draft",
            new RuleQuery("keyword=hill"),
            new Priority(10),
            true,
            Margin.None,
            Margin.None,
            Now.AddDays(-30)));
        world.Guide(BroadcastForRehearsal(1, "hill walking"));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(
            Draft("keyword=hill", priority: 90),
            Cancel);

        Assert.NotNull(rehearsed);
        Assert.Single(rehearsed.Taking);
        Assert.Single(rehearsed.Making);
    }

    [Fact]
    public async Task RetiringARuleTakesTheReservationsItMadeWithIt()
    {
        WorldForRetirement world = CollectedForRetirement();
        Rule rule = WrittenForRetirement();
        world.Rules.Rules.Add(rule);
        world.Reservations.Standing(
            StandingForRetirement(BroadcastForRetirement(1), ReservationState.Scheduled, rule.Id),
            StandingForRetirement(BroadcastForRetirement(2), ReservationState.Conflict, rule.Id));

        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.NotNull(retired);
        Assert.Empty(world.Reservations.Held);
        Assert.Empty(world.Rules.Rules);
        Assert.Equal(2, retired.Withdrawn.Count + retired.Swept.Count);
    }

    [Fact]
    public async Task AStandingReservationNoCollectionVouchesForStillGoesWhenTheRuleItselfGoes()
    {
        WorldForRetirement world = WorldForRetirement.Of();
        Rule rule = WrittenForRetirement();
        world.Rules.Rules.Add(rule);
        world.Reservations.Standing(StandingForRetirement(BroadcastForRetirement(1), ReservationState.Scheduled, rule.Id));

        IReadOnlyList<Reservation> held = await world.Applying.DroppedAsync(rule.Id, Cancel);
        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.Empty(held);
        Assert.NotNull(retired);
        Assert.Single(retired.Swept);
        Assert.Empty(world.Reservations.Held);
    }

    [Fact]
    public async Task WhatTheGuardLetsGoIsWithdrawnRatherThanSwept()
    {
        WorldForRetirement world = CollectedForRetirement();
        Rule rule = WrittenForRetirement();
        world.Rules.Rules.Add(rule);
        world.Reservations.Standing(StandingForRetirement(BroadcastForRetirement(1), ReservationState.Scheduled, rule.Id));

        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.NotNull(retired);
        Assert.Single(retired.Withdrawn);
        Assert.Empty(retired.Swept);
    }

    [Fact]
    public async Task RetiringARuleLeavesTheReservationThatIsBeingRecorded()
    {
        WorldForRetirement world = CollectedForRetirement();
        Rule rule = WrittenForRetirement();
        world.Rules.Rules.Add(rule);
        Reservation recording = StandingForRetirement(
            BroadcastForRetirement(1),
            ReservationState.Scheduled,
            rule.Id,
            startedAt: Now.AddMinutes(-5));
        world.Reservations.Standing(recording);

        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.NotNull(retired);
        Assert.Equal([recording.Id], [.. world.Reservations.Held.Select(held => held.Id)]);
    }

    [Fact]
    public async Task RetiringARuleLeavesTheReservationSomebodyCancelled()
    {
        WorldForRetirement world = CollectedForRetirement();
        Rule rule = WrittenForRetirement();
        world.Rules.Rules.Add(rule);
        Reservation cancelled = StandingForRetirement(BroadcastForRetirement(1), ReservationState.Cancelled, rule.Id);
        world.Reservations.Standing(cancelled);

        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.NotNull(retired);
        Assert.Equal([cancelled.Id], [.. world.Reservations.Held.Select(held => held.Id)]);
    }

    [Fact]
    public async Task RetiringARuleLeavesWhatSomebodyBookedByHandAndWhatAnotherRuleMade()
    {
        WorldForRetirement world = CollectedForRetirement();
        Rule rule = WrittenForRetirement();
        Rule beside = WrittenForRetirement(identifier: 2);
        world.Rules.Rules.Add(rule);
        world.Rules.Rules.Add(beside);
        Reservation byHand = StandingForRetirement(BroadcastForRetirement(1), ReservationState.Scheduled, null);
        Reservation byAnother = StandingForRetirement(BroadcastForRetirement(2), ReservationState.Scheduled, beside.Id);
        world.Reservations.Standing(byHand, byAnother);

        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.NotNull(retired);
        Assert.Equal(
            [byHand.Id, byAnother.Id],
            [.. world.Reservations.Held.Select(held => held.Id)]);
        Assert.Equal([beside.Id], [.. world.Rules.Rules.Select(held => held.Id)]);
    }

    [Fact]
    public async Task RetiringARuleThatIsNotThereSaysSoAndTouchesNothing()
    {
        WorldForRetirement world = CollectedForRetirement();
        Rule rule = WrittenForRetirement();
        world.Rules.Rules.Add(rule);
        Reservation standing = StandingForRetirement(BroadcastForRetirement(1), ReservationState.Scheduled, rule.Id);
        world.Reservations.Standing(standing);

        RuleRetirement? retired = await world.Applying.RetiredAsync(
            new RuleId(new Guid("0000dead-0000-0000-0000-000000000000")),
            Cancel);

        Assert.Null(retired);
        Assert.Equal([standing.Id], [.. world.Reservations.Held.Select(held => held.Id)]);
        Assert.Single(world.Rules.Rules);
        Assert.Equal(0, world.Write.Committed);
    }

    [Fact]
    public async Task WhatGoesWithTheRuleGoesInOneTransactionWithIt()
    {
        WorldForRetirement world = CollectedForRetirement();
        Rule rule = WrittenForRetirement();
        world.Rules.Rules.Add(rule);
        world.Reservations.Standing(StandingForRetirement(BroadcastForRetirement(1), ReservationState.Scheduled, rule.Id));

        await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.False(world.Write.Open);
        Assert.True(world.Write.Committed > 0);
        Assert.Equal(0, world.Write.RolledBack);
    }

    [Fact]
    public async Task WhatTheSweepTookMakesRoomThatIsSettledOnTheTunersAgain()
    {
        WorldForRetirement world = WorldForRetirement.Of();
        Rule rule = WrittenForRetirement();
        world.Rules.Rules.Add(rule);
        world.Reservations.Standing(StandingForRetirement(BroadcastForRetirement(1), ReservationState.Scheduled, rule.Id));

        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.NotNull(retired);
        Assert.Single(retired.Swept);
        Assert.Equal(2, world.Write.Committed);
    }

    [Fact]
    public async Task ARuleThatMadeNothingIsRetiredWithoutSettlingTheTunersAgain()
    {
        WorldForRetirement world = WorldForRetirement.Of();
        Rule rule = WrittenForRetirement();
        world.Rules.Rules.Add(rule);

        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.NotNull(retired);
        Assert.Empty(retired.Swept);
        Assert.Empty(retired.Withdrawn);
        Assert.Equal(1, world.Write.Committed);
    }

    [Fact]
    public async Task BrRd010ARuleTakingABroadcastTheMainAndTheSubChannelsShareReservesItOnceOnTheMain()
    {
        WorldForSimulcast world = WorldForSimulcast.Of();
        world.Rules.Rules.Add(WrittenForSimulcast("keyword=hill"));
        world.Guide(
            Listing(Main, "hill walking", related: [Shared(Sub), Shared(Third)]),
            Listing(Sub, "hill walking", related: [Shared(Main)]),
            Listing(Third, "hill walking", related: [Shared(Main)]));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Reservation made = Assert.Single(run.Made);
        Assert.Equal(Main, made.ServiceId.Value);
        Assert.Equal(BroadcastGroupRole.Standalone, made.BroadcastGroupRole);
        Assert.Null(made.BroadcastGroupKey);
        Assert.Equal(0, run.Moved);
    }

    [Fact]
    public async Task BrRd010ARuleTakingOneNameFromOneStartToOneEndOnTwoServicesOfAStreamReservesItOnceOnTheMain()
    {
        WorldForSimulcast world = WorldForSimulcast.Of();
        world.Rules.Rules.Add(WrittenForSimulcast("keyword=hill"));
        world.Guide(Listing(Sub, "hill walking", carried: 12), Listing(Main, "hill walking", carried: 11));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(Main, Assert.Single(run.Made).ServiceId.Value);
    }

    [Fact]
    public async Task BrRd010ARuleReachingOnlyTheSubChannelReservesTheBroadcastOnTheSubChannelItNamed()
    {
        WorldForSimulcast world = WorldForSimulcast.Of();
        world.Rules.Rules.Add(WrittenForSimulcast($"keyword=hill&channel={NetworkForSimulcast}-{Sub}"));
        world.Guide(
            Listing(Main, "hill walking", related: [Shared(Sub)]),
            Listing(Sub, "hill walking", related: [Shared(Main)]));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(Sub, Assert.Single(run.Made).ServiceId.Value);
    }

    [Fact]
    public async Task BrRd010ARuleNamingTheMainAndTheSubChannelReservesTheBroadcastOnceOnTheMain()
    {
        WorldForSimulcast world = WorldForSimulcast.Of();
        world.Rules.Rules.Add(WrittenForSimulcast($"keyword=hill&channel={NetworkForSimulcast}-{Sub}&channel={NetworkForSimulcast}-{Main}"));
        world.Guide(
            Listing(Main, "hill walking", related: [Shared(Sub)]),
            Listing(Sub, "hill walking", related: [Shared(Main)]));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(Main, Assert.Single(run.Made).ServiceId.Value);
    }

    [Fact]
    public async Task BrRd010ASubChannelThatTurnsUpNamedOnlyOnceItIsOnTheAirIsNotReservedBesideTheMain()
    {
        WorldForSimulcast world = WorldForSimulcast.Of();
        Rule rule = WrittenForSimulcast("keyword=hill");
        world.Rules.Rules.Add(rule);
        Programme main = Listing(Main, "hill walking", startsAt: Now.AddMinutes(-5), revision: 1, related: [Shared(Sub)]);
        world.Guide(
            main,
            Listing(Sub, "hill walking", startsAt: Now.AddMinutes(-5), revision: 9, related: [Shared(Main)]));
        world.Reservations.Standing(StandingForSimulcast(main, rule.Id, startedAt: Now.AddMinutes(-5)));

        RuleApplicationRun run = await world.Applying.SinceAsync(5, Cancel);

        Assert.Empty(run.Made);
        Assert.Equal([Main], world.Reservations.Held.Select(reservation => reservation.ServiceId.Value));
    }

    [Fact]
    public async Task BrRd010ASubChannelReservationARuleMadeBeforeItStartsIsTakenBackAndTheMainStays()
    {
        WorldForSimulcast world = WorldForSimulcast.Of();
        Rule rule = WrittenForSimulcast("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        Programme main = Listing(Main, "hill walking", related: [Shared(Sub)]);
        Programme sub = Listing(Sub, "hill walking", related: [Shared(Main)]);
        world.Guide(main, sub);
        Reservation onTheMain = StandingForSimulcast(main, rule.Id);
        Reservation onTheSub = StandingForSimulcast(sub, rule.Id);
        world.Reservations.Standing(onTheMain, onTheSub);

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal([onTheSub.Id], run.Withdrawn.Select(reservation => reservation.Id));
        Assert.Empty(run.Made);
        Assert.Equal([onTheMain.Id], world.Reservations.Held.Select(reservation => reservation.Id));
    }

    [Fact]
    public async Task BrRd010ASubChannelReservationThatHasStartedOrThatAPersonMadeIsLeftAlone()
    {
        WorldForSimulcast world = WorldForSimulcast.Of();
        Rule rule = WrittenForSimulcast("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        Programme running = Listing(Main, "hill walking", carried: 1, startsAt: Now.AddMinutes(-5), related: [Shared(Sub, 1)]);
        Programme runningOnTheSub = Listing(Sub, "hill walking", carried: 1, startsAt: Now.AddMinutes(-5), related: [Shared(Main, 1)]);
        Programme later = Listing(Main, "hill walking", carried: 2, startsAt: Now.AddHours(5), related: [Shared(Sub, 2)]);
        Programme laterOnTheSub = Listing(Sub, "hill walking", carried: 2, startsAt: Now.AddHours(5), related: [Shared(Main, 2)]);
        world.Guide(running, runningOnTheSub, later, laterOnTheSub);
        Reservation started = StandingForSimulcast(runningOnTheSub, rule.Id, startedAt: Now.AddMinutes(-5));
        Reservation byHand = StandingForSimulcast(laterOnTheSub, ruleId: null);
        world.Reservations.Standing(started, byHand);

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Empty(run.Withdrawn);
        Assert.Contains(started, world.Reservations.Held);
        Assert.Contains(byHand, world.Reservations.Held);
    }

    [Theory]
    [InlineData("priority")]
    [InlineData("margin before")]
    [InlineData("margin after")]
    [InlineData("encoding")]
    public async Task BrRd010ASubChannelReservationARuleMadeAndAPersonChangedStaysAndTheMainIsNotReservedBesideIt(string changed)
    {
        WorldForSimulcast world = WorldForSimulcast.Of();
        Rule rule = WrittenForSimulcast("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        Programme sub = Listing(Sub, "hill walking", related: [Shared(Main)]);
        world.Guide(Listing(Main, "hill walking", related: [Shared(Sub)]), sub);
        Reservation onTheSub = StandingForSimulcast(sub, rule.Id);
        Changed(onTheSub, changed);
        world.Reservations.Standing(onTheSub);

        RuleApplicationRun swept = await world.Applying.EverythingAsync(Cancel);
        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(rule, Cancel);

        Assert.Empty(swept.Withdrawn);
        Assert.Empty(swept.Made);
        Assert.Equal([onTheSub.Id], world.Reservations.Held.Select(reservation => reservation.Id));
        Assert.Empty(rehearsed!.Making);
        Assert.Empty(rehearsed.Withdrawing);
    }

    [Fact]
    public async Task WhatTheImpactSaysDeletingWouldSweepIsWhatDeletingSweeps()
    {
        WorldForImpact asked = WorldForImpact.Seeded();
        WorldForImpact acting = WorldForImpact.Seeded();

        RuleRehearsal? rehearsed = await Within(asked.Applying.RehearsedAsync(asked.Rewritten(), Cancel));
        RuleRetirement? retired = await Within(acting.Applying.RetiredAsync(WorldForImpact.TheRule, Cancel));

        Assert.NotNull(rehearsed);
        Assert.NotNull(retired);
        Assert.Equal(NamedForImpact(retired.Withdrawn.Concat(retired.Swept)), NamedForImpact(rehearsed.Sweeping));
    }

    [Fact]
    public async Task WhatTheImpactSaysSavingWouldWithdrawIsWhatTheApplicationAfterSavingWithdraws()
    {
        WorldForImpact asked = WorldForImpact.Seeded();
        WorldForImpact acting = WorldForImpact.Seeded();

        RuleRehearsal? rehearsed = await Within(asked.Applying.RehearsedAsync(asked.Rewritten(), Cancel));
        acting.Rewrite();
        RuleApplicationRun ran = await Within(acting.Applying.EverythingAsync(Cancel));

        Assert.NotNull(rehearsed);
        Assert.Empty(ran.Faulted);
        Assert.Equal(NamedForImpact(ran.Withdrawn), NamedForImpact(rehearsed.Withdrawing));
    }

    [Fact]
    public async Task SavingIsAnsweredWithTheseAndDeletingWithThoseSoNeitherAnswerIsEmptyNorTheSame()
    {
        WorldForImpact world = WorldForImpact.Seeded();

        RuleRehearsal? rehearsed = await Within(world.Applying.RehearsedAsync(world.Rewritten(), Cancel));

        Assert.NotNull(rehearsed);
        Assert.Equal(
            [
                WorldForImpact.WhatOnlyTheBasicTableVouchesFor,
                WorldForImpact.WhatStartsJustBeyondTheGrace,
                WorldForImpact.WhatCollectionVouchesFor,
            ],
            NamedForImpact(rehearsed.Withdrawing));
        Assert.Equal(
            [
                WorldForImpact.WhatStartsExactlyOnTheGrace,
                WorldForImpact.WhatAnotherRuleTakesOver,
                WorldForImpact.WhatOnlyTheBasicTableVouchesFor,
                WorldForImpact.WhatStartsJustBeyondTheGrace,
                WorldForImpact.WhatStartsInsideTheGrace,
                WorldForImpact.WhatWasNeverVisitedAtAll,
                WorldForImpact.WhatCollectionVouchesFor,
                WorldForImpact.WhatTheCollectionNeverFinished,
            ],
            NamedForImpact(rehearsed.Sweeping));
    }

    [Fact]
    public async Task WhatStartsExactlyOnTheGraceIsHeldBackAndWhatStartsASecondLaterIsNot()
    {
        WorldForImpact world = WorldForImpact.Seeded();

        RuleRehearsal? rehearsed = await Within(world.Applying.RehearsedAsync(world.Rewritten(), Cancel));

        Assert.NotNull(rehearsed);
        Assert.DoesNotContain(WorldForImpact.WhatStartsExactlyOnTheGrace, NamedForImpact(rehearsed.Withdrawing));
        Assert.Contains(WorldForImpact.WhatStartsJustBeyondTheGrace, NamedForImpact(rehearsed.Withdrawing));
        Assert.Contains(WorldForImpact.WhatStartsExactlyOnTheGrace, NamedForImpact(rehearsed.Sweeping));
    }

    [Fact]
    public async Task SwitchingTheRuleOffTakesWhatStartsInsideTheGraceThoughAnApplicationLeavesIt()
    {
        WorldForImpact switching = WorldForImpact.Seeded();
        WorldForImpact applying = WorldForImpact.Seeded();

        IReadOnlyList<Reservation> dropped =
            await Within(switching.Applying.DroppedAsync(WorldForImpact.TheRule, Cancel));

        applying.Rewrite();
        RuleApplicationRun ran = await Within(applying.Applying.EverythingAsync(Cancel));

        Assert.Contains(WorldForImpact.WhatStartsInsideTheGrace, NamedForImpact(dropped));
        Assert.Contains(WorldForImpact.WhatStartsExactlyOnTheGrace, NamedForImpact(dropped));
        Assert.DoesNotContain(WorldForImpact.WhatStartsInsideTheGrace, NamedForImpact(ran.Withdrawn));
        Assert.DoesNotContain(WorldForImpact.WhatStartsExactlyOnTheGrace, NamedForImpact(ran.Withdrawn));
        Assert.Contains(WorldForImpact.WhatCollectionVouchesFor, NamedForImpact(dropped));
        Assert.Contains(WorldForImpact.WhatCollectionVouchesFor, NamedForImpact(ran.Withdrawn));
    }

    [Fact]
    public async Task SwitchingTheRuleOffLeavesWhatNoCollectionVouchesForThoughDeletingTakesIt()
    {
        WorldForImpact switching = WorldForImpact.Seeded();
        WorldForImpact deleting = WorldForImpact.Seeded();

        IReadOnlyList<Reservation> dropped =
            await Within(switching.Applying.DroppedAsync(WorldForImpact.TheRule, Cancel));

        RuleRetirement? retired = await Within(deleting.Applying.RetiredAsync(WorldForImpact.TheRule, Cancel));

        Assert.NotNull(retired);
        Assert.DoesNotContain(WorldForImpact.WhatTheCollectionNeverFinished, NamedForImpact(dropped));
        Assert.DoesNotContain(WorldForImpact.WhatWasNeverVisitedAtAll, NamedForImpact(dropped));
        Assert.Contains(WorldForImpact.WhatTheCollectionNeverFinished, NamedForImpact(retired.Swept));
        Assert.Contains(WorldForImpact.WhatWasNeverVisitedAtAll, NamedForImpact(retired.Swept));
    }

    [Fact]
    public async Task TheRuleTakesTheseBroadcastsBeforeItIsRewrittenToTakeNoneOfThem()
    {
        WorldForImpact world = WorldForImpact.Seeded();

        RuleRehearsal? asItStands = await Within(world.Applying.RehearsedAsync(world.AsItStands(), Cancel));
        RuleRehearsal? rewritten = await Within(world.Applying.RehearsedAsync(world.Rewritten(), Cancel));

        Assert.NotNull(asItStands);
        Assert.NotNull(rewritten);
        Assert.Contains(
            WorldForImpact.WhatAnotherRuleTakesOver,
            asItStands.Taking.Select(take => take.Programme.Name).ToList());
        Assert.Empty(rewritten.Taking);
    }

    [Fact]
    public async Task ABroadcastTheGuideDroppedAndAnnouncedAgainIsReservedByTheRowItLeftBehind()
    {
        WorldForAReturn world = WorldForAReturn.Taking("keyword=hill");
        world.Announce(Taken, "hill walking");

        Reservation booked = Assert.Single((await world.Applying.EverythingAsync(Cancel)).Made);

        world.Dropped(Taken);

        Assert.Equal([booked.Id], (await world.Guiding.ReconcileAsync(Cancel)).Cancelled);
        Assert.Equal(ReservationState.Cancelled, booked.State);
        Assert.Equal(ReservationCancellation.ProgrammeGone, booked.Cancellation);

        world.Announce(Taken, "hill walking", heard: Now.AddHours(2));

        RuleApplicationRun back = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal([booked.Id], [.. back.Revived.Select(reservation => reservation.Id)]);
        Assert.Empty(back.Made);
        Assert.Equal(ReservationState.Scheduled, booked.State);
        Assert.Null(booked.Cancellation);
        Assert.False(booked.EpgMissing);
        Assert.Single(world.Reservations.Held);
    }

    [Fact]
    public async Task AReservationBroughtBackIsLeftWhereItIsByTheNextReadingOfTheGuide()
    {
        WorldForAReturn world = WorldForAReturn.Taking("keyword=hill");
        world.Announce(Taken, "hill walking");

        Reservation booked = Assert.Single((await world.Applying.EverythingAsync(Cancel)).Made);

        world.Dropped(Taken);
        await world.Guiding.ReconcileAsync(Cancel);
        world.Announce(Taken, "hill walking", heard: Now.AddHours(2));
        await world.Applying.EverythingAsync(Cancel);

        GuideRun again = await world.Guiding.ReconcileAsync(Cancel);

        Assert.Empty(again.Cancelled);
        Assert.Empty(again.Followed);
        Assert.Equal(ReservationState.Scheduled, booked.State);
    }

    [Fact]
    public async Task ABroadcastThatWentAndCameBackIsWrittenDownOnceEachWayHoweverOftenItDoesIt()
    {
        WorldForAReturn world = WorldForAReturn.Taking("keyword=hill");
        world.Announce(Taken, "hill walking");

        Reservation booked = Assert.Single((await world.Applying.EverythingAsync(Cancel)).Made);

        for (int round = 1; round <= 2; round++)
        {
            world.Dropped(Taken);
            await world.Guiding.ReconcileAsync(Cancel);
            world.Announce(Taken, "hill walking", heard: Now.AddHours(1 + round));
            await world.Applying.EverythingAsync(Cancel);
        }

        Assert.Equal(
            [ReservationOutcomeKind.ProgrammeGone, ReservationOutcomeKind.ProgrammeReturned],
            [.. world.Outcomes.Held.Select(outcome => outcome.Kind).Order()]);
        Assert.All(world.Outcomes.Held, outcome => Assert.Equal(booked.Id, outcome.ReservationId));
    }

    [Fact]
    public async Task AReservationSomebodyCancelledIsNotBroughtBackThoughTheRuleStillTakesTheProgramme()
    {
        WorldForAReturn world = WorldForAReturn.Taking("keyword=hill");
        world.Announce(Taken, "hill walking");

        Reservation booked = Assert.Single((await world.Applying.EverythingAsync(Cancel)).Made);

        await world.Scheduling.ReviseAsync(
            booked,
            new ReservationRevision { Move = ReservationMove.Cancel },
            Cancel);

        Assert.Equal(ReservationCancellation.ByHand, booked.Cancellation);

        RuleApplicationRun again = await world.Applying.EverythingAsync(Cancel);

        Assert.Empty(again.Revived);
        Assert.Empty(again.Made);
        Assert.Equal(ReservationState.Cancelled, booked.State);
        Assert.Empty(world.Outcomes.Held);
    }

    [Fact]
    public async Task AReservationNoRuleMadeIsNotBroughtBackByARuleThatTakesTheProgramme()
    {
        WorldForAReturn world = WorldForAReturn.Taking("keyword=hill");
        world.Announce(Taken, "hill walking");
        Reservation booked = world.BookedByHand(Taken);

        world.Dropped(Taken);
        await world.Guiding.ReconcileAsync(Cancel);
        world.Announce(Taken, "hill walking", heard: Now.AddHours(2));

        RuleApplicationRun again = await world.Applying.EverythingAsync(Cancel);

        Assert.Empty(again.Revived);
        Assert.Empty(again.Made);
        Assert.Equal(ReservationState.Cancelled, booked.State);
    }

    [Fact]
    public async Task AReservationWhoseWindowHasAlreadyClosedIsNotBroughtBackByTheProgrammeReturning()
    {
        WorldForAReturn world = WorldForAReturn.Taking("keyword=hill");
        world.Announce(Taken, "hill walking");

        Reservation closed = world.StandingCancelled(
            Taken,
            startAt: Now.AddHours(-2),
            endAt: Now.AddHours(-1));

        RuleApplicationRun again = await world.Applying.EverythingAsync(Cancel);

        Assert.Empty(again.Revived);
        Assert.Empty(again.Made);
        Assert.Equal(ReservationState.Cancelled, closed.State);
        Assert.Empty(world.Outcomes.Held);
    }

    private static Programme Broadcast(
        int service,
        int carried,
        string name,
        DateTime? startsAt = null,
        long revision = 1,
        int stream = Carried,
        AudioMode audio = AudioMode.Undetermined,
        int sounds = ProgrammeSnapshot.SoundsUnannounced)
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(stream),
            startsAt ?? Now.AddHours(2),
            (startsAt ?? Now.AddHours(2)).AddHours(1),
            name,
            "a summary",
            false,
            Now,
            audio: audio,
            sounds: sounds,
            revision: revision);

    private static Reservation Standing(
        Programme programme,
        ReservationState state,
        RuleId? ruleId,
        DateTime? startedAt = null,
        DateTime? startAt = null,
        int marginBefore = 0,
        ReservationCancellation? cancellation = null)
    {
        DateTime opens = startAt ?? programme.StartsAt;

        return Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(programme.NetworkId, programme.ServiceId, programme.EventId, programme.StartsAt),
            ruleId,
            Priority.Default,
            opens,
            opens.AddHours(1),
            true,
            Margin.OfSeconds(marginBefore),
            Margin.None,
            new ProgrammeSnapshot(
                programme.Name,
                programme.Summary,
                string.Empty,
                [],
                Now,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            state,
            startedAt,
            null,
            false,
            [],
            false,
            null,
            false,
            null,
            Now,
            cancellation
            ?? (state is ReservationState.Cancelled ? ReservationCancellation.ByHand : null));
    }

    private static BroadcastStream Terrestrial(int stream, params int[] services)
        => new(
            new NetworkId(Network),
            new TransportStreamId(stream),
            TuningParameters.Terrestrial(27),
            [.. services.Select(service => new ServiceId(service))]);

    private sealed class World
    {
        private World(RuleApplicationSettings settings, TimeProvider? applying)
        {
            Write = new WatchedWrite();
            Outcomes = new HeldOutcomes(Write);
            Reservations = new HeldReservations(Write, Outcomes);
            Streams = new CountedStreams([Terrestrial(Carried, Listed, Alongside)]);
            Seating = new HeldSeating(new TunerCapacity(
                [
                    new TunerSeat("first", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                    new TunerSeat("second", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                    new TunerSeat("third", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                ],
                []));
            Tuning = new TuningByService { Otherwise = Tunable() };

            Applying = new RuleApplicationService(
                Rules,
                Programmes,
                Reservations,
                new ReservationOutcomeDomainService(Outcomes, new NoRecordings()),
                new RuleWithdrawalDomainService(Reservations, Streams, Visits, Epochs, applying ?? new FixedClock(Now)),
                new RuleTakingDomainService(Reservations),
                new ReservationSchedulingService(
                    Reservations,
                    new NoRecordings(),
                    Seating,
                    Tuning,
                    Write,
                    RollingHorizon.Default,
                    new SilentEvents(),
                    new FixedClock(Now)),
                new RuleMatcher(new ProgrammeSearchScopeDomainService(Streams, Services), new FixedClock(Now)),
                settings,
                Write,
                Events,
                applying ?? new FixedClock(Now));
        }

        public HeldRules Rules { get; } = new();

        public SilentEvents Events { get; } = new();

        public HeldProgrammes Programmes { get; } = new();

        public HeldOutcomes Outcomes { get; }

        public HeldReservations Reservations { get; }

        public HeldStreamVisits Visits { get; } = new();

        public HeldEpochs Epochs { get; } = new();

        public CountedStreams Streams { get; }

        public CountedServices Services { get; } = new();

        public WatchedWrite Write { get; }

        public HeldSeating Seating { get; }

        public TuningByService Tuning { get; }

        public RuleApplicationService Applying { get; }

        public static World Of(RuleApplicationSettings? settings = null, TimeProvider? applying = null)
            => new(settings ?? new RuleApplicationSettings(), applying);

        public void Guide(params Programme[] programmes) => Programmes.Programmes.AddRange(programmes);

        public void Visited(VisitOutcome outcome)
            => Visits.Visits.Add(StreamVisit.Record(
                new NetworkId(Network),
                new TransportStreamId(Carried),
                outcome,
                Now.AddHours(-1),
                TimeSpan.FromSeconds(30)));

        private static TuningResolution Tunable()
            => TuningResolution.Tunable(
                new CandidateChannelId(Guid.NewGuid()),
                TuningParameters.Terrestrial(27),
                impaired: false);
    }

    private static WorldForRehearsal Crowded(int seats)
    {
        WorldForRehearsal world = WorldForRehearsal.Of(seats);
        DateTime opens = Now.AddHours(2);
        world.Tuning.Answer(Alongside, TuningParameters.Terrestrial(29));
        world.Guide(BroadcastForRehearsal(1, "hill walking", startsAt: opens));
        world.Reservations.Standing(StandingForRehearsal(BroadcastForRehearsal(9, "an outside booking", Alongside, opens)));

        return world;
    }

    private static WorldForRehearsal Collected()
    {
        WorldForRehearsal world = WorldForRehearsal.Of();
        world.Visited(VisitOutcome.Complete);

        return world;
    }

    private static Rule Draft(string query, int priority = 10, RuleId? identifier = null)
        => Rule.Draft(
            identifier ?? new RuleId(new Guid("00000001-0000-0000-0000-000000000000")),
            "a draft",
            new RuleQuery(query),
            new Priority(priority),
            true,
            Margin.None,
            Margin.None,
            Now.AddDays(-1));

    private static Programme BroadcastForRehearsal(
        int carried,
        string name,
        int service = Listed,
        DateTime? startsAt = null,
        bool shadow = false)
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(Carried),
            startsAt ?? Now.AddHours(2 + carried),
            (startsAt ?? Now.AddHours(2 + carried)).AddHours(1),
            name,
            "a summary",
            shadow,
            Now,
            revision: 1);

    private static Programme LinkedForRehearsal(
        int carried,
        string name,
        int service,
        DateTime startsAt,
        ProgrammeRunning running = ProgrammeRunning.Undetermined,
        IReadOnlyList<RelatedProgramme>? related = null)
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(Carried),
            startsAt,
            startsAt.AddHours(1),
            name,
            "a summary",
            false,
            Now,
            related: related,
            revision: 1,
            running: running);

    private static Reservation StandingForRehearsal(Programme programme, RuleId? ruleId = null)
        => Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(programme.NetworkId, programme.ServiceId, programme.EventId, programme.StartsAt),
            ruleId,
            Priority.Default,
            programme.StartsAt,
            programme.StartsAt.AddHours(1),
            true,
            Margin.None,
            Margin.None,
            new ProgrammeSnapshot(
                programme.Name,
                programme.Summary,
                string.Empty,
                [],
                Now,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            ReservationState.Scheduled,
            null,
            null,
            false,
            [],
            false,
            null,
            false,
            null,
            Now);

    private sealed class WorldForRehearsal
    {
        private WorldForRehearsal(int seats)
        {
            Write = new WatchedWrite();
            Outcomes = new HeldOutcomes(Write);
            Reservations = new HeldReservations(Write, Outcomes);
            Streams = new CountedStreams([Terrestrial(Carried, Listed, Alongside)]);
            Seating = new HeldSeating(new TunerCapacity(
                [
                    .. Enumerable.Range(0, seats).Select(index =>
                        new TunerSeat($"seat{index}", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)),
                ],
                []));
            Tuning = new TuningByService { Otherwise = Tunable() };
            Scheduling = new ReservationSchedulingService(
                Reservations,
                new NoRecordings(),
                Seating,
                Tuning,
                Write,
                RollingHorizon.Default,
                new SilentEvents(),
                new FixedClock(Now));

            Applying = new RuleApplicationService(
                Rules,
                Programmes,
                Reservations,
                new ReservationOutcomeDomainService(Outcomes, new NoRecordings()),
                new RuleWithdrawalDomainService(Reservations, Streams, Visits, new HeldEpochs(), new FixedClock(Now)),
                new RuleTakingDomainService(Reservations),
                Scheduling,
                new RuleMatcher(new ProgrammeSearchScopeDomainService(Streams, Services), new FixedClock(Now)),
                new RuleApplicationSettings(),
                Write,
                new SilentEvents(),
                new FixedClock(Now));
        }

        public HeldRules Rules { get; } = new();

        public HeldProgrammes Programmes { get; } = new();

        public HeldOutcomes Outcomes { get; }

        public HeldReservations Reservations { get; }

        public HeldStreamVisits Visits { get; } = new();

        public CountedStreams Streams { get; }

        public CountedServices Services { get; } = new();

        public WatchedWrite Write { get; }

        public HeldSeating Seating { get; }

        public TuningByService Tuning { get; }

        public ReservationSchedulingService Scheduling { get; }

        public RuleApplicationService Applying { get; }

        public static WorldForRehearsal Of(int seats = 3) => new(seats);

        public void Guide(params Programme[] programmes) => Programmes.Programmes.AddRange(programmes);

        public void Visited(VisitOutcome outcome)
            => Visits.Visits.Add(StreamVisit.Record(
                new NetworkId(Network),
                new TransportStreamId(Carried),
                outcome,
                Now.AddHours(-1),
                TimeSpan.FromSeconds(30)));

        private static TuningResolution Tunable()
            => TuningResolution.Tunable(
                new CandidateChannelId(Guid.NewGuid()),
                TuningParameters.Terrestrial(27),
                impaired: false);
    }

    private static WorldForRetirement CollectedForRetirement()
    {
        WorldForRetirement world = WorldForRetirement.Of();
        world.Visited(VisitOutcome.Complete);

        return world;
    }

    private static Rule WrittenForRetirement(int identifier = 1, bool enabled = true)
        => Rule.Draft(
            new RuleId(new Guid($"{identifier:x8}-0000-0000-0000-000000000000")),
            "a rule",
            new RuleQuery("keyword=hill"),
            Priority.Default,
            enabled,
            Margin.None,
            Margin.None,
            Now.AddDays(-30));

    private static Programme BroadcastForRetirement(int carried, string name = "hill walking")
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(Listed), new EventId(carried)),
            new TransportStreamId(Carried),
            Now.AddHours(2 + carried),
            Now.AddHours(3 + carried),
            name,
            "a summary",
            false,
            Now,
            revision: 1);

    private static Reservation StandingForRetirement(
        Programme programme,
        ReservationState state,
        RuleId? ruleId,
        DateTime? startedAt = null)
        => Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(programme.NetworkId, programme.ServiceId, programme.EventId, programme.StartsAt),
            ruleId,
            Priority.Default,
            programme.StartsAt,
            programme.StartsAt.AddHours(1),
            true,
            Margin.None,
            Margin.None,
            new ProgrammeSnapshot(
                programme.Name,
                programme.Summary,
                string.Empty,
                [],
                Now,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            state,
            startedAt,
            null,
            false,
            [],
            false,
            null,
            false,
            null,
            Now,
            state is ReservationState.Cancelled ? ReservationCancellation.ByHand : null);

    private sealed class WorldForRetirement
    {
        private WorldForRetirement()
        {
            Write = new WatchedWrite();
            Outcomes = new HeldOutcomes(Write);
            Reservations = new HeldReservations(Write, Outcomes);
            Streams = new CountedStreams([Terrestrial(Carried, Listed)]);
            Seating = new HeldSeating(new TunerCapacity(
                [new TunerSeat("first", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)],
                []));
            Tuning = new TuningByService { Otherwise = Tunable() };

            Applying = new RuleApplicationService(
                Rules,
                Programmes,
                Reservations,
                new ReservationOutcomeDomainService(Outcomes, new NoRecordings()),
                new RuleWithdrawalDomainService(Reservations, Streams, Visits, new HeldEpochs(), new FixedClock(Now)),
                new RuleTakingDomainService(Reservations),
                new ReservationSchedulingService(
                    Reservations,
                    new NoRecordings(),
                    Seating,
                    Tuning,
                    Write,
                    RollingHorizon.Default,
                    new SilentEvents(),
                    new FixedClock(Now)),
                new RuleMatcher(new ProgrammeSearchScopeDomainService(Streams, Services), new FixedClock(Now)),
                new RuleApplicationSettings(),
                Write,
                new SilentEvents(),
                new FixedClock(Now));
        }

        public HeldRules Rules { get; } = new();

        public HeldProgrammes Programmes { get; } = new();

        public HeldOutcomes Outcomes { get; }

        public HeldReservations Reservations { get; }

        public HeldStreamVisits Visits { get; } = new();

        public CountedStreams Streams { get; }

        public CountedServices Services { get; } = new();

        public WatchedWrite Write { get; }

        public HeldSeating Seating { get; }

        public TuningByService Tuning { get; }

        public RuleApplicationService Applying { get; }

        public static WorldForRetirement Of() => new();

        public void Visited(VisitOutcome outcome)
            => Visits.Visits.Add(StreamVisit.Record(
                new NetworkId(Network),
                new TransportStreamId(Carried),
                outcome,
                Now.AddHours(-1),
                TimeSpan.FromSeconds(30)));

        private static TuningResolution Tunable()
            => TuningResolution.Tunable(
                new CandidateChannelId(Guid.NewGuid()),
                TuningParameters.Terrestrial(27),
                impaired: false);
    }

    private static void Changed(Reservation reservation, string what)
    {
        switch (what)
        {
            case "priority":
                reservation.Reprioritise(new Priority(40));
                break;
            case "margin before":
                reservation.Remargin(Margin.OfSeconds(60), Margin.None);
                break;
            case "margin after":
                reservation.Remargin(Margin.None, Margin.OfSeconds(60));
                break;
            default:
                reservation.Rewish(false);
                break;
        }
    }

    private static RelatedProgramme Shared(int service, int carried = Event)
        => new(NetworkForSimulcast, service, carried, RelationKind.Shared);

    private static Programme Listing(
        int service,
        string name,
        int carried = Event,
        DateTime? startsAt = null,
        long revision = 1,
        IReadOnlyList<RelatedProgramme>? related = null)
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(NetworkForSimulcast), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(Stream),
            startsAt ?? Now.AddHours(2),
            (startsAt ?? Now.AddHours(2)).AddHours(1),
            name,
            "a summary",
            false,
            Now,
            related: related,
            revision: revision);

    private static Rule WrittenForSimulcast(string query)
        => Rule.Draft(
            new RuleId(new Guid("00000001-0000-0000-0000-000000000000")),
            "a rule",
            new RuleQuery(query),
            new Priority(10),
            true,
            Margin.None,
            Margin.None,
            Now.AddDays(-30),
            true);

    private static Reservation StandingForSimulcast(Programme programme, RuleId? ruleId, DateTime? startedAt = null)
        => Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(programme.NetworkId, programme.ServiceId, programme.EventId, programme.StartsAt),
            ruleId,
            Priority.Default,
            programme.StartsAt,
            programme.StartsAt.AddHours(1),
            true,
            Margin.None,
            Margin.None,
            new ProgrammeSnapshot(
                programme.Name,
                programme.Summary,
                string.Empty,
                [],
                Now,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            ReservationState.Scheduled,
            startedAt,
            null,
            false,
            [],
            false,
            null,
            false,
            null,
            Now,
            null);

    private sealed class WorldForSimulcast
    {
        private WorldForSimulcast()
        {
            Write = new WatchedWrite();
            Outcomes = new HeldOutcomes(Write);
            Reservations = new HeldReservations(Write, Outcomes);
            Streams = new CountedStreams(
            [
                new BroadcastStream(
                    new NetworkId(NetworkForSimulcast),
                    new TransportStreamId(Stream),
                    TuningParameters.Terrestrial(27),
                    [new ServiceId(Main), new ServiceId(Sub), new ServiceId(Third)]),
            ]);
            HeldSeating seating = new(new TunerCapacity(
                [
                    new TunerSeat("first", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                    new TunerSeat("second", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                ],
                []));
            TuningByService tuning = new()
            {
                Otherwise = TuningResolution.Tunable(
                    new CandidateChannelId(Guid.NewGuid()),
                    TuningParameters.Terrestrial(27),
                    impaired: false),
            };

            Applying = new RuleApplicationService(
                Rules,
                Programmes,
                Reservations,
                new ReservationOutcomeDomainService(Outcomes, new NoRecordings()),
                new RuleWithdrawalDomainService(Reservations, Streams, Visits, Epochs, new FixedClock(Now)),
                new RuleTakingDomainService(Reservations),
                new ReservationSchedulingService(
                    Reservations,
                    new NoRecordings(),
                    seating,
                    tuning,
                    Write,
                    RollingHorizon.Default,
                    new SilentEvents(),
                    new FixedClock(Now)),
                new RuleMatcher(new ProgrammeSearchScopeDomainService(Streams, new CountedServices()), new FixedClock(Now)),
                new RuleApplicationSettings(),
                Write,
                new SilentEvents(),
                new FixedClock(Now));
        }

        public HeldRules Rules { get; } = new();

        public HeldProgrammes Programmes { get; } = new();

        public HeldOutcomes Outcomes { get; }

        public HeldReservations Reservations { get; }

        public HeldStreamVisits Visits { get; } = new();

        public HeldEpochs Epochs { get; } = new();

        public CountedStreams Streams { get; }

        public WatchedWrite Write { get; }

        public RuleApplicationService Applying { get; }

        public static WorldForSimulcast Of() => new();

        public void Guide(params Programme[] programmes) => Programmes.Programmes.AddRange(programmes);

        public void Visited(VisitOutcome outcome)
            => Visits.Visits.Add(StreamVisit.Record(
                new NetworkId(NetworkForSimulcast),
                new TransportStreamId(Stream),
                outcome,
                Now.AddHours(-1),
                TimeSpan.FromSeconds(30)));
    }

    private static IReadOnlyList<string> NamedForImpact(IEnumerable<Reservation> reservations)
        => [.. reservations.Select(reservation => reservation.SnapshotName).Order(StringComparer.Ordinal)];

    private static Task<T> Within<T>(Task<T> asked) => asked.WaitAsync(LongEnoughToTellAStallFromAWait);

    private sealed class WorldForImpact
    {
        public const string WhatCollectionVouchesFor = "hill walking";

        public const string WhatStartsInsideTheGrace = "hill running";

        public const string WhatStartsExactlyOnTheGrace = "hill ambling";

        public const string WhatStartsJustBeyondTheGrace = "hill roving";

        public const string WhatOnlyTheBasicTableVouchesFor = "hill climbing";

        public const string WhatTheCollectionNeverFinished = "hill wandering";

        public const string WhatWasNeverVisitedAtAll = "hill scrambling";

        public const string WhatIsBeingRecorded = "hill rambling";

        public const string WhatSomebodyCancelled = "hill striding";

        public const string WhatSomebodyBookedByHand = "hill trekking";

        public const string WhatAnotherRuleTakesOver = "hill bounding";

        public const string WhatAnotherRuleMade = "river fishing";

        public static readonly RuleId TheRule = new(new Guid("00000001-0000-0000-0000-000000000000"));

        public static readonly RuleId Beside = new(new Guid("00000002-0000-0000-0000-000000000000"));

        public static readonly RuleId Waiting = new(new Guid("00000003-0000-0000-0000-000000000000"));

        private const int Network = 4;

        private const int Vouched = 32_736;

        private const int OnlyBasic = 32_737;

        private const int Unfinished = 32_738;

        private const int Unvisited = 32_739;

        private const int Listed = 1049;

        private const int Alongside = 1040;

        private const int Basic = 1050;

        private const int Partial = 1051;

        private const int Unseen = 1052;

        private const int Ahead = 90;

        private const int Behind = 10;

        private WorldForImpact()
        {
            Write = new WatchedWrite();
            Outcomes = new HeldOutcomes(Write);
            Reservations = new HeldReservations(Write, Outcomes);
            Streams = new CountedStreams(
            [
                Terrestrial(Vouched, 27, Listed, Alongside),
                Terrestrial(OnlyBasic, 29, Basic),
                Terrestrial(Unfinished, 31, Partial),
                Terrestrial(Unvisited, 33, Unseen),
            ]);
            Seating = new HeldSeating(new TunerCapacity(
                [
                    .. Enumerable.Range(0, 9).Select(index =>
                        new TunerSeat($"seat{index}", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)),
                ],
                []));
            Tuning = new TuningByService
            {
                Otherwise = TuningResolution.Tunable(
                    new CandidateChannelId(Guid.NewGuid()),
                    TuningParameters.Terrestrial(27),
                    impaired: false),
            };

            Applying = new RuleApplicationService(
                Rules,
                Programmes,
                Reservations,
                new ReservationOutcomeDomainService(Outcomes, new NoRecordings()),
                new RuleWithdrawalDomainService(Reservations, Streams, Visits, new HeldEpochs(), new FixedClock(NowForImpact)),
                new RuleTakingDomainService(Reservations),
                new ReservationSchedulingService(
                    Reservations,
                    new NoRecordings(),
                    Seating,
                    Tuning,
                    Write,
                    RollingHorizon.Default,
                    new SilentEvents(),
                    new FixedClock(NowForImpact)),
                new RuleMatcher(new ProgrammeSearchScopeDomainService(Streams, Services), new FixedClock(NowForImpact)),
                Settings,
                Write,
                new SilentEvents(),
                new FixedClock(NowForImpact));
        }

        public HeldRules Rules { get; } = new();

        public HeldProgrammes Programmes { get; } = new();

        public HeldOutcomes Outcomes { get; }

        public HeldReservations Reservations { get; }

        public HeldStreamVisits Visits { get; } = new();

        public CountedStreams Streams { get; }

        public CountedServices Services { get; } = new();

        public WatchedWrite Write { get; }

        public HeldSeating Seating { get; }

        public TuningByService Tuning { get; }

        public RuleApplicationSettings Settings { get; } = new();

        public RuleApplicationService Applying { get; }

        public static WorldForImpact Seeded()
        {
            var world = new WorldForImpact();

            world.Rules.Rules.Add(Written(TheRule, "keyword=hill", Ahead));
            world.Rules.Rules.Add(Written(Beside, "keyword=river", Behind));
            world.Rules.Rules.Add(Written(Waiting, "keyword=bounding", Behind));

            world.Visited(Vouched, VisitOutcome.Complete);
            world.Visited(OnlyBasic, VisitOutcome.BasicOnly);
            world.Visited(Unfinished, VisitOutcome.Incomplete);

            world.Standing(1, WhatCollectionVouchesFor, Listed, Vouched, NowForImpact.AddHours(2), TheRule);
            world.Standing(2, WhatStartsInsideTheGrace, Alongside, Vouched, NowForImpact.AddMinutes(4), TheRule);
            world.Standing(
                3,
                WhatStartsExactlyOnTheGrace,
                Alongside,
                Vouched,
                NowForImpact + world.Settings.Grace,
                TheRule);
            world.Standing(
                4,
                WhatStartsJustBeyondTheGrace,
                Alongside,
                Vouched,
                NowForImpact + world.Settings.Grace + TimeSpan.FromSeconds(1),
                TheRule);
            world.Standing(5, WhatOnlyTheBasicTableVouchesFor, Basic, OnlyBasic, NowForImpact.AddHours(3), TheRule);
            world.Standing(6, WhatTheCollectionNeverFinished, Partial, Unfinished, NowForImpact.AddHours(4), TheRule);
            world.Standing(7, WhatWasNeverVisitedAtAll, Unseen, Unvisited, NowForImpact.AddHours(5), TheRule);
            world.Standing(
                8,
                WhatIsBeingRecorded,
                Listed,
                Vouched,
                NowForImpact.AddMinutes(-20),
                TheRule,
                startedAt: NowForImpact.AddMinutes(-20));
            world.Standing(
                9,
                WhatSomebodyCancelled,
                Listed,
                Vouched,
                NowForImpact.AddHours(6),
                TheRule,
                state: ReservationState.Cancelled);
            world.Standing(10, WhatSomebodyBookedByHand, Listed, Vouched, NowForImpact.AddHours(7), null);
            world.Standing(11, WhatAnotherRuleMade, Listed, Vouched, NowForImpact.AddHours(8), Beside);
            world.Standing(12, WhatAnotherRuleTakesOver, Listed, Vouched, NowForImpact.AddHours(9), TheRule);

            return world;
        }

        public Rule AsItStands() => Written(TheRule, "keyword=hill", Ahead);

        public Rule Rewritten() => Written(TheRule, "keyword=heather", Ahead);

        public void Rewrite()
            => Rules.Rules
                .Single(rule => rule.Id.Equals(TheRule))
                .Rewrite(
                    "a rule",
                    new RuleQuery("keyword=heather"),
                    new Priority(Ahead),
                    Margin.None,
                    Margin.None,
                    true);

        private static Rule Written(RuleId id, string query, int priority)
            => Rule.Draft(
                id,
                "a rule",
                new RuleQuery(query),
                new Priority(priority),
                true,
                Margin.None,
                Margin.None,
                NowForImpact.AddDays(-30));

        private static BroadcastStream Terrestrial(int stream, int channel, params int[] services)
            => new(
                new NetworkId(Network),
                new TransportStreamId(stream),
                TuningParameters.Terrestrial(channel),
                [.. services.Select(service => new ServiceId(service))]);

        private void Visited(int stream, VisitOutcome outcome)
            => Visits.Visits.Add(StreamVisit.Record(
                new NetworkId(Network),
                new TransportStreamId(stream),
                outcome,
                NowForImpact.AddHours(-1),
                TimeSpan.FromSeconds(30)));

        private Programme Broadcast(int carried, string name, int service, int stream, DateTime startsAt)
            => Programme.Rehydrate(
                new ProgrammeId(new NetworkId(Network), new ServiceId(service), new EventId(carried)),
                new TransportStreamId(stream),
                startsAt,
                startsAt.AddHours(1),
                name,
                "a summary",
                false,
                NowForImpact,
                revision: carried);

        private void Standing(
            int carried,
            string name,
            int service,
            int stream,
            DateTime startsAt,
            RuleId? ruleId,
            DateTime? startedAt = null,
            ReservationState state = ReservationState.Scheduled)
        {
            Programme programme = Broadcast(carried, name, service, stream, startsAt);

            Programmes.Programmes.Add(programme);
            Reservations.Standing(Reservation.Rehydrate(
                new ReservationId(new Guid($"{carried:x8}-0000-0000-0000-00000000000f")),
                new ProgrammeRef(programme.NetworkId, programme.ServiceId, programme.EventId, programme.StartsAt),
                ruleId,
                Priority.Default,
                programme.StartsAt,
                programme.StartsAt.AddHours(1),
                true,
                Margin.None,
                Margin.None,
                new ProgrammeSnapshot(
                    name,
                    "a summary",
                    string.Empty,
                    [],
                    NowForImpact,
                    AudioMode.Undetermined,
                    ProgrammeSnapshot.SoundsUnannounced),
                null,
                BroadcastGroupRole.Standalone,
                state,
                startedAt,
                null,
                false,
                [],
                false,
                null,
                false,
                null,
                NowForImpact,
                state is ReservationState.Cancelled ? ReservationCancellation.ByHand : null));
        }
    }

    private sealed class WorldForAReturn
    {
        private long revisions;

        private WorldForAReturn(string query)
        {
            Write = new WatchedWrite();
            Outcomes = new HeldOutcomes(Write);
            Reservations = new HeldReservations(Write, Outcomes);
            Streams = new CountedStreams(
            [
                new BroadcastStream(
                    new NetworkId(Network),
                    new TransportStreamId(Carried),
                    TuningParameters.Terrestrial(27),
                    [new ServiceId(Listed)]),
            ]);
            Rules.Rules.Add(WrittenForAReturn(query));

            var clock = new FixedClock(Now);
            var tuning = new TuningByService
            {
                Otherwise = TuningResolution.Tunable(
                    new CandidateChannelId(Guid.NewGuid()),
                    TuningParameters.Terrestrial(27),
                    impaired: false),
            };

            Scheduling = new ReservationSchedulingService(
                Reservations,
                new NoRecordings(),
                new HeldSeating(new TunerCapacity(
                    [new TunerSeat("seat0", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)],
                    [])),
                tuning,
                Write,
                RollingHorizon.Default,
                new SilentEvents(),
                clock);

            Applying = new RuleApplicationService(
                Rules,
                Programmes,
                Reservations,
                new ReservationOutcomeDomainService(Outcomes, new NoRecordings()),
                new RuleWithdrawalDomainService(Reservations, Streams, new HeldStreamVisits(), new HeldEpochs(), clock),
                new RuleTakingDomainService(Reservations),
                Scheduling,
                new RuleMatcher(new ProgrammeSearchScopeDomainService(Streams, new CountedServices()), clock),
                new RuleApplicationSettings(),
                Write,
                new SilentEvents(),
                clock);

            Guiding = new ReservationGuideService(
                Reservations,
                new ReservationOutcomeDomainService(Outcomes, new NoRecordings()),
                Programmes,
                Scheduling,
                Write,
                new SilentEvents(),
                clock);
        }

        public HeldRules Rules { get; } = new();

        public HeldProgrammes Programmes { get; } = new();

        public HeldReservations Reservations { get; }

        public HeldOutcomes Outcomes { get; }

        public CountedStreams Streams { get; }

        public WatchedWrite Write { get; }

        public ReservationSchedulingService Scheduling { get; }

        public RuleApplicationService Applying { get; }

        public ReservationGuideService Guiding { get; }

        public static WorldForAReturn Taking(string query) => new(query);

        public void Announce(int carried, string name, DateTime? heard = null)
        {
            Programmes.Programmes.RemoveAll(programme => programme.EventId.Value == carried);

            Programme announced = Programme.Rehydrate(
                new ProgrammeId(new NetworkId(Network), new ServiceId(Listed), new EventId(carried)),
                new TransportStreamId(Carried),
                Now.AddHours(2),
                Now.AddHours(3),
                name,
                "a summary",
                false,
                Now,
                lastHeardAt: heard ?? Now);

            announced.MarkRevision(++revisions);
            Programmes.Programmes.Add(announced);
        }

        /// <summary>
        /// A whole reading of the service after the broadcaster stopped announcing this programme: the
        /// programme is gone, and what is still announced beside it carries a later mark than the one that
        /// named it.
        /// </summary>
        public void Dropped(int carried)
        {
            Programmes.Programmes.RemoveAll(programme =>
                programme.EventId.Value == carried || programme.EventId.Value == BesideForAReturn);

            Programme beside = Programme.Rehydrate(
                new ProgrammeId(new NetworkId(Network), new ServiceId(Listed), new EventId(BesideForAReturn)),
                new TransportStreamId(Carried),
                Now.AddHours(4),
                Now.AddHours(5),
                "the one beside it",
                "a summary",
                false,
                Now,
                lastHeardAt: Now.AddHours(1));

            beside.MarkRevision(++revisions);
            Programmes.Programmes.Add(beside);
        }

        /// <summary>
        /// A reservation a rule made and the guide then took out, as it would be read back from the ledger.
        /// </summary>
        public Reservation StandingCancelled(int carried, DateTime startAt, DateTime endAt)
        {
            Programme announced = Programmes.Programmes.Single(programme => programme.EventId.Value == carried);
            Reservation cancelled = Reservation.Rehydrate(
                ReservationId.New(),
                new ProgrammeRef(
                    announced.NetworkId,
                    announced.ServiceId,
                    announced.EventId,
                    announced.StartsAt),
                Rules.Rules[0].Id,
                Priority.Default,
                startAt,
                endAt,
                true,
                Margin.None,
                Margin.None,
                new ProgrammeSnapshot(
                    announced.Name,
                    announced.Summary,
                    string.Empty,
                    [],
                    Now,
                    AudioMode.Undetermined,
                    ProgrammeSnapshot.SoundsUnannounced),
                null,
                BroadcastGroupRole.Standalone,
                ReservationState.Cancelled,
                null,
                null,
                false,
                [],
                true,
                null,
                false,
                null,
                Now,
                ReservationCancellation.ProgrammeGone);

            Reservations.Standing(cancelled);

            return cancelled;
        }

        public Reservation BookedByHand(int carried)
        {
            Programme announced = Programmes.Programmes.Single(programme => programme.EventId.Value == carried);
            Reservation booked = ReservationFixtures.Rehydrated(
                ReservationState.Scheduled,
                programme: new ProgrammeRef(
                    announced.NetworkId,
                    announced.ServiceId,
                    announced.EventId,
                    announced.StartsAt),
                startAt: announced.StartsAt,
                endAt: announced.EndsAt);

            Reservations.Standing(booked);

            return booked;
        }
    }

    private static Rule WrittenForAReturn(string query)
        => Rule.Draft(
            new RuleId(new Guid("00000001-0000-0000-0000-000000000000")),
            "a rule",
            new RuleQuery(query),
            Priority.Default,
            true,
            Margin.None,
            Margin.None,
            Now.AddDays(-30));
}
