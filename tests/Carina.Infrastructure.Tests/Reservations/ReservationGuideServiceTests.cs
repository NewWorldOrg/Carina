using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Reservations;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Reservations;

public sealed class ReservationGuideServiceTests
{
    private const int Network = 32736;

    private const int Listed = 1024;

    private const int Carried = 4001;

    private static readonly DateTime Now = ReservationFixtures.Now;

    private static readonly DateTime Opens = Now.AddDays(4);

    private static readonly string Titled = ReservationFixtures.Snapshot().Name;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ABroadcastThatMovedTakesItsReservationWithIt()
    {
        Held held = Standing();
        Reservation booked = held.Book();
        held.Announce(Opens.AddMinutes(40), Opens.AddMinutes(100));

        GuideRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Equal([booked.Id], run.Followed);
        Assert.Empty(run.Cancelled);
        Assert.Equal(Opens.AddMinutes(40), booked.StartAt);
        Assert.Equal(Opens.AddMinutes(100), booked.EndAt);
        Assert.Equal(Opens.AddMinutes(40), booked.ProgrammeStartsAt);
        Assert.Equal(ReservationState.Scheduled, booked.State);
    }

    [Fact]
    public async Task WhatMovedIsOnTheReservationForSomebodyToReadAfterwards()
    {
        Held held = Standing();
        Reservation booked = held.Book();
        held.Announce(Opens.AddMinutes(40), Opens.AddMinutes(100));

        await held.Service.ReconcileAsync(Cancel);

        EpgDivergence moved = Assert.Single(
            booked.EpgDivergences,
            divergence => divergence.Field is DivergedField.StartAt);

        Assert.True(booked.EpgDiverged);
        Assert.Equal(Opens.ToString("O"), moved.Before);
        Assert.Equal(Opens.AddMinutes(40).ToString("O"), moved.After);
    }

    [Fact]
    public async Task ABroadcastThatMovedIsWrittenDownInTheLedgerOnce()
    {
        Held held = Standing();
        Reservation booked = held.Book();
        held.Announce(Opens.AddMinutes(40), Opens.AddMinutes(100));

        await held.Service.ReconcileAsync(Cancel);

        ReservationOutcome recorded = Assert.Single(held.Outcomes.Held);

        Assert.Equal(ReservationOutcomeKind.ProgrammeMoved, recorded.Kind);
        Assert.Equal(booked.Id, recorded.ReservationId);

        held.Announce(Opens.AddMinutes(80), Opens.AddMinutes(140));

        Assert.Equal([booked.Id], (await held.Service.ReconcileAsync(Cancel)).Followed);
        Assert.Single(held.Outcomes.Held);
    }

    [Fact]
    public async Task ABroadcastThatHasNotMovedSinceTheLastPassIsLeftExactlyWhereItIs()
    {
        Held held = Standing();
        held.Book();
        held.Announce(Opens.AddMinutes(40), Opens.AddMinutes(100));

        await held.Service.ReconcileAsync(Cancel);
        GuideRun again = await held.Service.ReconcileAsync(Cancel);

        Assert.Empty(again.Followed);
        Assert.Empty(again.Cancelled);
        Assert.Single(held.Outcomes.Held);
    }

    [Fact]
    public async Task ABroadcastSlippingByLessThanAMinuteMovesNothingAndSaysNothing()
    {
        Held held = Standing();
        held.Book();
        held.Announce(Opens.AddSeconds(45), Opens.AddHours(1).AddSeconds(45));

        GuideRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Empty(run.Followed);
        Assert.Empty(held.Outcomes.Held);
    }

    [Fact]
    public async Task ABroadcastTheGuideNoLongerAnnouncesTakesItsReservationOutOfTheRunning()
    {
        Held held = Standing();
        Reservation booked = held.Book();
        held.Dropped();

        GuideRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Equal([booked.Id], run.Cancelled);
        Assert.Equal(ReservationState.Cancelled, booked.State);
        Assert.True(booked.EpgMissing);
        Assert.Equal(ReservationOutcomeKind.ProgrammeGone, Assert.Single(held.Outcomes.Held).Kind);
    }

    [Fact]
    public async Task AReservationTakenOutBecauseTheBroadcastWentIsNotWrittenDownTwice()
    {
        Held held = Standing();
        held.Book();
        held.Dropped();

        await held.Service.ReconcileAsync(Cancel);
        GuideRun again = await held.Service.ReconcileAsync(Cancel);

        Assert.Empty(again.Cancelled);
        Assert.Single(held.Outcomes.Held);
    }

    [Fact]
    public async Task AServiceNoReadingHasEverHeardWholeSaysNothingAboutWhatIsMissingFromIt()
    {
        Held held = Standing();
        held.Book();
        held.Programmes.Programmes.Clear();

        GuideRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Empty(run.Cancelled);
        Assert.Empty(held.Outcomes.Held);
    }

    [Fact]
    public async Task AProgrammeAReadingHasNotYetConfirmedIsNotTakenForOneThatWentAway()
    {
        Held held = Standing();
        held.Book();
        held.Announce(Opens, Opens.AddHours(1), heardWhole: false);
        held.AnnounceBeside(heardWhole: true);

        GuideRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Empty(run.Cancelled);
        Assert.Empty(held.Outcomes.Held);
    }

    [Fact]
    public async Task AProgrammeLeftBehindByAWholeReadingOfItsServiceIsOneThatWentAway()
    {
        Held held = Standing();
        Reservation booked = held.Book();
        held.AnnounceBeside(heardWhole: true);

        Assert.Equal([booked.Id], (await held.Service.ReconcileAsync(Cancel)).Cancelled);
    }

    [Fact]
    public async Task AReservationAlreadyHoldingATunerIsLeftAloneWhateverTheGuideSays()
    {
        Held held = Standing();
        Reservation recording = held.Book(startedAt: Now);
        held.Dropped();

        GuideRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Empty(run.Cancelled);
        Assert.Empty(run.Followed);
        Assert.Equal(ReservationState.Scheduled, recording.State);
    }

    [Fact]
    public async Task AReservationWhoseWindowHasAlreadyOpenedIsLeftAloneToo()
    {
        Held held = Standing(at: Opens.AddMinutes(1));
        Reservation booked = held.Book();
        held.Announce(Opens.AddMinutes(40), Opens.AddMinutes(100));

        GuideRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Empty(run.Followed);
        Assert.Equal(Opens, booked.StartAt);
    }

    [Fact]
    public async Task ABroadcastThatOnlyChangedItsTitleIsStillTheBroadcastThatWasReserved()
    {
        Held held = Standing();
        Reservation booked = held.Book();
        held.Announce(Opens, Opens.AddHours(1), name: "その後の題名");

        Assert.Equal([booked.Id], (await held.Service.ReconcileAsync(Cancel)).Followed);
        Assert.Equal("その後の題名", booked.SnapshotName);
        Assert.Equal(Opens, booked.StartAt);
    }

    [Fact]
    public async Task AShadowOfSomebodyElsesBroadcastIsNotTheEntryToFollow()
    {
        Held held = Standing();
        held.Book();
        held.Announce(Opens.AddMinutes(40), Opens.AddMinutes(100), isShadow: true);

        Assert.Empty((await held.Service.ReconcileAsync(Cancel)).Followed);
    }

    [Fact]
    public async Task APassThatChangedNothingRingsNoBellAndOpensNoWrite()
    {
        Held held = Standing();
        held.Book();
        held.Announce(Opens, Opens.AddHours(1));

        await held.Service.ReconcileAsync(Cancel);

        Assert.Empty(held.Events.Signalled);
        Assert.Equal(0, held.Write.Opened);
    }

    [Fact]
    public async Task AReservationThatMovedRingsTheBellSoTheScreensReadItAgain()
    {
        Held held = Standing();
        held.Book();
        held.Announce(Opens.AddMinutes(40), Opens.AddMinutes(100));

        await held.Service.ReconcileAsync(Cancel);

        Assert.Equal(AppEventName.Reservations, Assert.Single(held.Events.Signalled));
    }

    private static Held Standing(DateTime? at = null)
    {
        var write = new WatchedWrite();
        var outcomes = new HeldOutcomes(write);
        var ledger = new HeldReservations(write, outcomes);
        var programmes = new HeldProgrammes();
        var events = new SilentEvents();
        var clock = new FixedClock(at ?? Now);
        var directory = new TuningByService();

        directory.Answer(Listed, TuningParameters.Terrestrial(27));

        var scheduling = new ReservationSchedulingService(
            ledger,
            new HeldSeating(new TunerCapacity(
                [new TunerSeat("seat0", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)],
                [])),
            directory,
            write,
            RollingHorizon.Default,
            clock);

        return new Held(
            new ReservationGuideService(ledger, outcomes, programmes, scheduling, write, events, clock),
            ledger,
            outcomes,
            programmes,
            events,
            write);
    }

    private sealed record Held(
        ReservationGuideService Service,
        HeldReservations Reservations,
        HeldOutcomes Outcomes,
        HeldProgrammes Programmes,
        SilentEvents Events,
        WatchedWrite Write)
    {
        public Reservation Book(DateTime? startedAt = null)
        {
            Reservation booked = ReservationFixtures.Rehydrated(
                ReservationState.Scheduled,
                startedAt: startedAt,
                programme: ReservationFixtures.Programme(Carried, Listed, Opens),
                startAt: Opens,
                endAt: Opens.AddHours(1));

            Reservations.Standing(booked);
            Announce(Opens, Opens.AddHours(1));

            return booked;
        }

        public void Announce(
            DateTime startsAt,
            DateTime endsAt,
            bool heardWhole = true,
            bool isShadow = false,
            string? name = null)
        {
            Programmes.Programmes.RemoveAll(programme => programme.EventId.Value == Carried);
            Programmes.Programmes.Add(Programme.Rehydrate(
                new ProgrammeId(new NetworkId(Network), new ServiceId(Listed), new EventId(Carried)),
                new TransportStreamId(Network),
                startsAt,
                endsAt,
                name ?? Titled,
                "What it is about",
                isShadow,
                Now,
                lastHeardAt: heardWhole ? Now : null));
        }

        /// <summary>
        /// Another broadcast on the same service, named by a reading later than anything the
        /// reserved one carries. That is what a whole reading looks like from the outside: every
        /// broadcast still announced comes away with the same fresh mark.
        /// </summary>
        public void AnnounceBeside(bool heardWhole)
            => Programmes.Programmes.Add(Programme.Rehydrate(
                new ProgrammeId(new NetworkId(Network), new ServiceId(Listed), new EventId(Carried + 1)),
                new TransportStreamId(Network),
                Opens.AddHours(2),
                Opens.AddHours(3),
                "The one beside it",
                "What it is about",
                false,
                Now,
                lastHeardAt: heardWhole ? Now.AddHours(1) : null));

        public void Dropped()
        {
            Programmes.Programmes.RemoveAll(programme => programme.EventId.Value == Carried);
            AnnounceBeside(heardWhole: true);
        }
    }
}
