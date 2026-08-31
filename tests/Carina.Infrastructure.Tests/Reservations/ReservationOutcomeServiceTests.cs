using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Reservations;

namespace Carina.Infrastructure.Tests.Reservations;

public sealed class ReservationOutcomeServiceTests
{
    private static readonly DateTime Opens = ReservationFixtures.Now.AddHours(2);

    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    private static readonly DateTime AfterItAll = Opens.AddHours(3);

    [Fact]
    public async Task AReservationNothingEverClaimedStopsSayingItsTunerIsSecured()
    {
        Reservation waiting = ReservationFixtures.Rehydrated(ReservationState.Scheduled, startAt: Opens);
        Held held = Standing(AfterItAll, waiting);

        ReservationOutcomeRun run = await Run(held);

        Assert.Equal([new ReservationOutcomeRecord(waiting.Id, ReservationOutcomeKind.Missed)], run.Recorded);
        Assert.Equal(ReservationState.Missed, waiting.State);

        ReservationOutcome recorded = Assert.Single(held.Outcomes.Held);

        Assert.Equal(ReservationOutcomeKind.Missed, recorded.Kind);
        Assert.Equal(waiting.Id, recorded.ReservationId);
        Assert.Equal(waiting.SnapshotName, recorded.SnapshotName);
        Assert.Equal(waiting.EffectiveStartAt, recorded.EffectiveStartAt);
        Assert.Equal(waiting.EffectiveEndAt, recorded.EffectiveEndAt);
        Assert.Equal(AfterItAll, recorded.OccurredAt);
        Assert.Empty(recorded.RecordedInstead);
        Assert.Null(recorded.TuneFailure);
        Assert.Null(recorded.RecordingOutcome);
    }

    [Fact]
    public async Task AReservationThatLostTheContestNamesWhatWasRecordedInstead()
    {
        Reservation lost = ReservationFixtures.Rehydrated(ReservationState.Conflict, startAt: Opens);
        Reservation won = ReservationFixtures.Rehydrated(
            ReservationState.Scheduled,
            startedAt: Opens,
            outcome: RecordingOutcome.Complete,
            startAt: Opens.AddMinutes(30));
        Reservation elsewhere = ReservationFixtures.Rehydrated(
            ReservationState.Scheduled,
            startedAt: Opens.AddHours(2),
            outcome: RecordingOutcome.Complete,
            startAt: Opens.AddHours(2));
        Held held = Standing(AfterItAll, lost, won, elsewhere);

        ReservationOutcomeRun run = await Run(held);

        Assert.Equal([new ReservationOutcomeRecord(lost.Id, ReservationOutcomeKind.Competing)], run.Recorded);
        Assert.Equal(ReservationState.Missed, lost.State);

        ReservationOutcome recorded = Assert.Single(held.Outcomes.Held);

        Assert.Equal(ReservationOutcomeKind.Competing, recorded.Kind);
        Assert.Equal([won.Id.Value], recorded.RecordedInstead);
    }

    [Fact]
    public async Task ARecordingThatFailedIsRecordedWithoutMovingTheReservation()
    {
        Reservation failed = ReservationFixtures.Rehydrated(
            ReservationState.Scheduled,
            startedAt: Opens,
            outcome: RecordingOutcome.Failed,
            startAt: Opens);
        Held held = Standing(AfterItAll, failed);

        ReservationOutcomeRun run = await Run(held);

        Assert.Equal(
            [new ReservationOutcomeRecord(failed.Id, ReservationOutcomeKind.RecordingFailure)],
            run.Recorded);
        Assert.Equal(ReservationState.Scheduled, failed.State);

        ReservationOutcome recorded = Assert.Single(held.Outcomes.Held);

        Assert.Equal(ReservationOutcomeKind.RecordingFailure, recorded.Kind);
        Assert.Equal(RecordingOutcome.Failed, recorded.RecordingOutcome);
        Assert.Empty(recorded.RecordedInstead);
    }

    [Fact]
    public async Task ARecordingThatEndedWellIsNotRecordedAtAll()
    {
        Reservation complete = ReservationFixtures.Rehydrated(
            ReservationState.Scheduled,
            startedAt: Opens,
            outcome: RecordingOutcome.Complete,
            startAt: Opens);
        Held held = Standing(AfterItAll, complete);

        ReservationOutcomeRun run = await Run(held);

        Assert.Empty(run.Recorded);
        Assert.Empty(held.Outcomes.Held);
    }

    [Fact]
    public async Task AReservationIsLeftAloneUntilItsWindowHasClosedAndIsRecordedOnceItHas()
    {
        Reservation waiting = ReservationFixtures.Rehydrated(ReservationState.Scheduled, startAt: Opens);
        Held early = Standing(waiting.EffectiveEndAt.AddSeconds(-1), waiting);

        Assert.Empty((await Run(early)).Recorded);
        Assert.Empty(early.Outcomes.Held);
        Assert.Equal(ReservationState.Scheduled, waiting.State);

        Held late = Standing(waiting.EffectiveEndAt, waiting);
        ReservationOutcomeRun run = await Run(late);

        Assert.Equal([new ReservationOutcomeRecord(waiting.Id, ReservationOutcomeKind.Missed)], run.Recorded);
        Assert.Equal(ReservationState.Missed, waiting.State);
    }

    [Fact]
    public async Task AReservationWhoseGraceHasNotRunOutIsLeftAloneEvenThoughItsWindowHasClosed()
    {
        Reservation brief = ReservationFixtures.Rehydrated(
            ReservationState.Scheduled,
            startAt: Opens,
            endAt: Opens.AddMinutes(2));
        Held early = Standing(brief.EffectiveStartAt + Grace - TimeSpan.FromSeconds(1), brief);

        Assert.Empty((await Run(early)).Recorded);
        Assert.Equal(ReservationState.Scheduled, brief.State);

        Held late = Standing(brief.EffectiveStartAt + Grace, brief);

        Assert.Equal(
            [new ReservationOutcomeRecord(brief.Id, ReservationOutcomeKind.Missed)],
            (await Run(late)).Recorded);
    }

    [Fact]
    public async Task WhatIsAlreadyInTheLedgerIsNotRecordedAgain()
    {
        Reservation failed = ReservationFixtures.Rehydrated(
            ReservationState.Scheduled,
            startedAt: Opens,
            outcome: RecordingOutcome.Failed,
            startAt: Opens);
        Reservation waiting = ReservationFixtures.Rehydrated(ReservationState.Scheduled, startAt: Opens);
        Held held = Standing(AfterItAll, failed, waiting);

        Assert.Equal(2, (await Run(held)).Recorded.Count);
        Assert.Empty((await Run(held)).Recorded);
        Assert.Equal(2, held.Outcomes.Held.Count);
    }

    [Fact]
    public async Task EverythingARunWritesIsWrittenInsideOneWrite()
    {
        Reservation waiting = ReservationFixtures.Rehydrated(ReservationState.Scheduled, startAt: Opens);
        Held held = Standing(AfterItAll, waiting);

        await Run(held);

        Assert.Equal(1, held.Write.Opened);
        Assert.Equal(1, held.Write.Committed);
        Assert.Empty(held.Outcomes.WroteOutsideAWrite);
        Assert.Empty(held.Reservations.WroteOutsideAWrite);
    }

    [Fact]
    public async Task ARunWithNothingToSayOpensNoWrite()
    {
        Held held = Standing(AfterItAll);

        Assert.Empty((await Run(held)).Recorded);
        Assert.Equal(0, held.Write.Opened);
    }

    private static Task<ReservationOutcomeRun> Run(Held held)
        => held.Service.RecordAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

    private static Held Standing(DateTime at, params Reservation[] reservations)
    {
        var write = new WatchedWrite();
        var outcomes = new HeldOutcomes(write);
        var ledger = new HeldReservations(write, outcomes);
        ledger.Standing(reservations);

        return new Held(
            new ReservationOutcomeService(
                ledger,
                outcomes,
                write,
                new ReservationOutcomeSettings { Grace = Grace },
                new FixedClock(at)),
            ledger,
            outcomes,
            write);
    }

    private sealed record Held(
        ReservationOutcomeService Service,
        HeldReservations Reservations,
        HeldOutcomes Outcomes,
        WatchedWrite Write);
}
