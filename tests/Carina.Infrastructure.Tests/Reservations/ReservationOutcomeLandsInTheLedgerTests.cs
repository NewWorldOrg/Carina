using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Reservations;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Tests.Reservations;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationOutcomeLandsInTheLedgerTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task WhatLostTheContestLandsWithTheNameOfWhatWasRecordedInstead()
    {
        DateTime opens = ReservationFixtures.Now.AddHours(50);
        Reservation lost = await LaidDownAsync(ReservationState.Conflict, opens, opens.AddHours(1));
        Reservation won = await LaidDownAsync(
            ReservationState.Scheduled,
            opens.AddMinutes(30),
            opens.AddMinutes(90),
            claimedAt: opens.AddMinutes(30),
            outcome: RecordingOutcome.Complete);
        Reservation elsewhere = await LaidDownAsync(
            ReservationState.Scheduled,
            opens.AddHours(5),
            opens.AddHours(6),
            claimedAt: opens.AddHours(5),
            outcome: RecordingOutcome.Complete);

        ReservationOutcomeRun run = await RecordingAsync(opens.AddHours(2));

        Assert.Equal(
            [new ReservationOutcomeRecord(lost.Id, ReservationOutcomeKind.Competing)],
            run.Recorded);

        ReservationOutcome held = Assert.Single(await ForAsync(lost.Id));

        Assert.Equal(ReservationOutcomeKind.Competing, held.Kind);
        Assert.Equal([won.Id.Value], held.RecordedInstead);
        Assert.Equal(lost.EffectiveStartAt, held.EffectiveStartAt);
        Assert.Equal(lost.EffectiveEndAt, held.EffectiveEndAt);
        Assert.Equal(lost.SnapshotName, held.SnapshotName);
        Assert.Equal(lost.Priority, held.Priority);
        Assert.Equal(opens.AddHours(2), held.OccurredAt);
        Assert.Null(held.TuneFailure);
        Assert.Null(held.RecordingOutcome);
        Assert.Empty(await ForAsync(won.Id));
        Assert.Empty(await ForAsync(elsewhere.Id));
        Assert.Equal(ReservationState.Missed, await StateOfAsync(lost.Id));
        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(won.Id));
    }

    [Fact]
    public async Task AReservationNothingClaimedLandsAsMissedAndStopsSayingItIsSecured()
    {
        DateTime opens = ReservationFixtures.Now.AddHours(60);
        Reservation gone = await LaidDownAsync(ReservationState.Scheduled, opens, opens.AddHours(1));

        Assert.Equal(
            [new ReservationOutcomeRecord(gone.Id, ReservationOutcomeKind.Missed)],
            (await RecordingAsync(opens.AddHours(2))).Recorded);

        Assert.Equal(ReservationOutcomeKind.Missed, Assert.Single(await ForAsync(gone.Id)).Kind);
        Assert.Equal(ReservationState.Missed, await StateOfAsync(gone.Id));
    }

    [Fact]
    public async Task ARecordingTheLedgerSaysFailedLandsWithoutMovingTheReservation()
    {
        DateTime opens = ReservationFixtures.Now.AddHours(70);
        Reservation broke = await LaidDownAsync(
            ReservationState.Scheduled,
            opens,
            opens.AddHours(1),
            claimedAt: opens,
            outcome: RecordingOutcome.Failed);

        Assert.Equal(
            [new ReservationOutcomeRecord(broke.Id, ReservationOutcomeKind.RecordingFailure)],
            (await RecordingAsync(opens.AddHours(2))).Recorded);

        ReservationOutcome held = Assert.Single(await ForAsync(broke.Id));

        Assert.Equal(ReservationOutcomeKind.RecordingFailure, held.Kind);
        Assert.Equal(RecordingOutcome.Failed, held.RecordingOutcome);
        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(broke.Id));
    }

    [Fact]
    public async Task AReservationStillInsideItsWindowIsNotOfferedUntilItCloses()
    {
        DateTime opens = ReservationFixtures.Now.AddHours(80);
        Reservation running = await LaidDownAsync(ReservationState.Scheduled, opens, opens.AddHours(1));

        Assert.Empty((await RecordingAsync(opens.AddMinutes(59))).Recorded);
        Assert.Empty(await ForAsync(running.Id));
        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(running.Id));

        Assert.Equal(
            [new ReservationOutcomeRecord(running.Id, ReservationOutcomeKind.Missed)],
            (await RecordingAsync(opens.AddHours(1))).Recorded);
    }

    [Fact]
    public async Task AReservationTheLedgerAlreadyHoldsIsNotOfferedAgain()
    {
        DateTime opens = ReservationFixtures.Now.AddHours(90);
        Reservation broke = await LaidDownAsync(
            ReservationState.Scheduled,
            opens,
            opens.AddHours(1),
            claimedAt: opens,
            outcome: RecordingOutcome.Failed);

        Assert.Single((await RecordingAsync(opens.AddHours(2))).Recorded);
        Assert.Empty((await RecordingAsync(opens.AddHours(3))).Recorded);
        Assert.Single(await ForAsync(broke.Id));

        await using CarinaDbContext context = database.Open();

        Assert.Empty(
            await new ReservationRepository(context).ListAwaitingOutcomeAsync(opens.AddHours(3), Cancel));
    }

    [Fact]
    public async Task TheTableItselfRefusesASecondRowOfTheSameKind()
    {
        DateTime opens = ReservationFixtures.Now.AddHours(100);
        Reservation gone = await LaidDownAsync(ReservationState.Scheduled, opens, opens.AddHours(1));

        await AddAsync(gone, ReservationOutcomeKind.Missed, opens.AddHours(2));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => AddAsync(gone, ReservationOutcomeKind.Missed, opens.AddHours(3)));
    }

    [Fact]
    public async Task TheTableTakesASecondRowOfADifferentKind()
    {
        DateTime opens = ReservationFixtures.Now.AddHours(110);
        Reservation gone = await LaidDownAsync(ReservationState.Scheduled, opens, opens.AddHours(1));

        await AddAsync(gone, ReservationOutcomeKind.Missed, opens.AddHours(2));
        await AddAsync(gone, ReservationOutcomeKind.Competing, opens.AddHours(3));

        Assert.Equal(
            [ReservationOutcomeKind.Competing, ReservationOutcomeKind.Missed],
            [.. (await ForAsync(gone.Id)).Select(outcome => outcome.Kind).Order()]);
    }

    [Fact]
    public async Task WhatWasClaimedOverAWindowIsWhatTheQueryHandsBack()
    {
        DateTime opens = ReservationFixtures.Now.AddHours(120);
        Reservation claimed = await LaidDownAsync(
            ReservationState.Scheduled,
            opens,
            opens.AddHours(1),
            claimedAt: opens);
        Reservation never = await LaidDownAsync(ReservationState.Scheduled, opens, opens.AddHours(1));
        Reservation far = await LaidDownAsync(
            ReservationState.Scheduled,
            opens.AddHours(10),
            opens.AddHours(11),
            claimedAt: opens.AddHours(10));

        await using CarinaDbContext context = database.Open();
        IReadOnlyList<Reservation> over = await new ReservationRepository(context).ListClaimedOverAsync(
            new ReservationWindow(opens.AddMinutes(-1), opens.AddMinutes(1)),
            Cancel);
        List<Guid> found = [.. over.Select(reservation => reservation.Id.Value)];

        Assert.Contains(claimed.Id.Value, found);
        Assert.DoesNotContain(never.Id.Value, found);
        Assert.DoesNotContain(far.Id.Value, found);
    }

    private async Task<ReservationOutcomeRun> RecordingAsync(DateTime at)
    {
        await using CarinaDbContext context = database.Open();

        return await new ReservationOutcomeService(
                new ReservationRepository(context),
                new ReservationOutcomeRepository(context),
                new DatabaseAtomicWrite(context),
                new ReservationOutcomeSettings { Grace = Grace },
                new FixedClock(at))
            .RecordAsync(Cancel)
            .WaitAsync(TimeSpan.FromSeconds(60), Cancel);
    }

    private async Task AddAsync(Reservation reservation, ReservationOutcomeKind kind, DateTime at)
    {
        await using CarinaDbContext context = database.Open();

        await new ReservationOutcomeRepository(context).AddAsync(
            ReservationOutcome.Record(ReservationOutcomeId.New(), reservation, kind, null, null, [], at),
            Cancel);
    }

    private async Task<IReadOnlyList<ReservationOutcome>> ForAsync(ReservationId id)
    {
        await using CarinaDbContext context = database.Open();

        return await new ReservationOutcomeRepository(context).ListForReservationAsync(id, Cancel);
    }

    private async Task<ReservationState> StateOfAsync(ReservationId id)
    {
        await using CarinaDbContext context = database.Open();

        return (await new ReservationRepository(context).FindAsync(id, Cancel))!.State;
    }

    private async Task<Reservation> LaidDownAsync(
        ReservationState state,
        DateTime startAt,
        DateTime endAt,
        DateTime? claimedAt = null,
        RecordingOutcome? outcome = null)
    {
        Reservation held = ReservationFixtures.Rehydrated(
            state,
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), startsAt: startAt),
            startAt: startAt,
            endAt: endAt);

        await using (CarinaDbContext writing = database.Open())
        {
            await new ReservationRepository(writing).AddAsync(held, Cancel);
        }

        if (claimedAt is { } moment)
        {
            await RunAsync(
                $"UPDATE reservation SET started_at = timestamptz '{moment:yyyy-MM-dd HH:mm:ss}+00' "
                + $"WHERE id = '{held.Id.Value}'");
        }

        if (outcome is { } written)
        {
            await RunAsync(
                $"UPDATE reservation SET recording_outcome = '{written}' WHERE id = '{held.Id.Value}'");
        }

        return held;
    }

    private async Task RunAsync(string sql)
    {
        await using CarinaDbContext context = database.Open();
        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync(Cancel);

        await using var running = new NpgsqlCommand(sql, connection);
        await running.ExecuteNonQueryAsync(Cancel);
    }
}
