using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Tests.Reservations;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = ReservationFixtures.Now;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly ReservationWindow Everything = new(Now, Now.AddDays(14));

    [Fact]
    public async Task AReservationComesBackWithTheWindowAndTheSnapshotItWasStoredUnder()
    {
        Reservation planned = ReservationFixtures.Planned(
            marginBefore: Margin.OfSeconds(10),
            marginAfter: Margin.OfSeconds(30));
        await AddAsync(planned);

        await using CarinaDbContext context = database.Open();
        Reservation? found = await new ReservationRepository(context).FindAsync(planned.Id, Cancel);

        Assert.NotNull(found);
        Assert.Equal(planned.StartAt, found.StartAt);
        Assert.Equal(planned.EndAt, found.EndAt);
        Assert.Equal(planned.EffectiveStartAt, found.EffectiveStartAt);
        Assert.Equal(planned.EffectiveEndAt, found.EffectiveEndAt);
        Assert.Equal("A programme", found.SnapshotName);
        Assert.Equal(ReservationState.Scheduled, found.State);
        Assert.False(found.IsPinned);
    }

    [Fact]
    public async Task AProgrammeIsFoundByTheFourThingsThatNameIt()
    {
        Reservation planned = ReservationFixtures.Planned();
        await AddAsync(planned);

        await using CarinaDbContext context = database.Open();
        Reservation? found = await new ReservationRepository(context)
            .FindByProgrammeAsync(planned.Programme, Cancel);

        Assert.NotNull(found);
        Assert.Equal(planned.Id, found.Id);
    }

    [Fact]
    public async Task WhatIsPendingLeavesOutWhatWasCancelledOrMissed()
    {
        Reservation standing = ReservationFixtures.Planned();
        Reservation cancelled = ReservationFixtures.Rehydrated(ReservationState.Cancelled);
        Reservation missed = ReservationFixtures.Rehydrated(ReservationState.Missed);
        Reservation contended = ReservationFixtures.Rehydrated(ReservationState.Conflict);
        await AddAsync(standing, cancelled, missed, contended);

        IReadOnlyList<ReservationId> pending = await PendingAsync(Everything);

        Assert.Contains(standing.Id, pending);
        Assert.Contains(contended.Id, pending);
        Assert.DoesNotContain(cancelled.Id, pending);
        Assert.DoesNotContain(missed.Id, pending);
    }

    [Fact]
    public async Task ARecordingThatHasEndedStopsBeingPendingEvenThoughItsStateStillReadsScheduled()
    {
        Reservation finished = ReservationFixtures.Planned();
        await AddAsync(finished);
        await ClaimAsync(finished.Id, Now);
        await SettleAsync(finished.Id, RecordingOutcome.Complete);

        IReadOnlyList<ReservationId> pending = await PendingAsync(Everything);

        Assert.DoesNotContain(finished.Id, pending);

        await using CarinaDbContext context = database.Open();
        Reservation? read = await new ReservationRepository(context).FindAsync(finished.Id, Cancel);

        Assert.Equal(ReservationState.Scheduled, read!.State);
        Assert.Equal(RecordingOutcome.Complete, read.RecordingOutcome);
    }

    [Fact]
    public async Task ARecordingStillRunningIsPendingEvenWhenItsWindowIsBehindTheOneAskedFor()
    {
        Reservation running = ReservationFixtures.Planned(
            startAt: Now.AddHours(-9),
            endAt: Now.AddHours(-8));
        await AddAsync(running);
        await ClaimAsync(running.Id, Now.AddHours(-9));

        IReadOnlyList<ReservationId> pending = await PendingAsync(Everything);

        Assert.Contains(running.Id, pending);
    }

    [Fact]
    public async Task AReservationWhollyOutsideTheWindowIsNotPending()
    {
        Reservation past = ReservationFixtures.Planned(
            startAt: Now.AddHours(-9),
            endAt: Now.AddHours(-8));
        Reservation ahead = ReservationFixtures.Planned(
            startAt: Now.AddDays(30),
            endAt: Now.AddDays(30).AddHours(1));
        await AddAsync(past, ahead);

        IReadOnlyList<ReservationId> pending = await PendingAsync(Everything);

        Assert.DoesNotContain(past.Id, pending);
        Assert.DoesNotContain(ahead.Id, pending);
    }

    [Fact]
    public async Task SavingThemAllCarriesEveryChangeToTheLedger()
    {
        Reservation first = ReservationFixtures.Planned();
        Reservation second = ReservationFixtures.Planned();
        await AddAsync(first, second);

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new ReservationRepository(writing);
            Reservation one = (await repository.FindAsync(first.Id, Cancel))!;
            Reservation other = (await repository.FindAsync(second.Id, Cancel))!;
            one.Contend();
            other.LoseReception(Now);

            await repository.SaveAllAsync([one, other], Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        var read = new ReservationRepository(reading);

        Assert.Equal(ReservationState.Conflict, (await read.FindAsync(first.Id, Cancel))!.State);
        Assert.True((await read.FindAsync(second.Id, Cancel))!.ReceptionUnavailable);
    }

    [Fact]
    public async Task WithdrawingARuleBornReservationLeavesNoRowBehind()
    {
        Reservation withdrawn = ReservationFixtures.Planned();
        await AddAsync(withdrawn);

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new ReservationRepository(writing);
            Reservation loaded = (await repository.FindAsync(withdrawn.Id, Cancel))!;

            await repository.WithdrawAsync([loaded], Cancel);
        }

        await using CarinaDbContext reading = database.Open();

        Assert.Null(await new ReservationRepository(reading).FindAsync(withdrawn.Id, Cancel));
    }

    private static async Task<NpgsqlConnection> OpenAsync(CarinaDbContext context)
    {
        var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();

        return connection;
    }

    private async Task<IReadOnlyList<ReservationId>> PendingAsync(ReservationWindow window)
    {
        await using CarinaDbContext context = database.Open();

        return
        [
            .. (await new ReservationRepository(context).ListPendingAsync(window, Cancel))
                .Select(reservation => reservation.Id),
        ];
    }

    private async Task AddAsync(params Reservation[] reservations)
    {
        await using CarinaDbContext context = database.Open();
        var repository = new ReservationRepository(context);

        foreach (Reservation reservation in reservations)
        {
            await repository.AddAsync(reservation, Cancel);
        }
    }

    private async Task ClaimAsync(ReservationId id, DateTime at)
    {
        await using CarinaDbContext context = database.Open();

        Assert.True(await new ReservationRecordingContract(context).ClaimAsync(id, at, Cancel));
    }

    private async Task SettleAsync(ReservationId id, RecordingOutcome outcome)
    {
        await using CarinaDbContext context = database.Open();
        await using NpgsqlConnection connection = await OpenAsync(context);
        await using var settling = new NpgsqlCommand(
            $"UPDATE reservation SET recording_outcome = '{outcome}' WHERE id = '{id.Value}'",
            connection);

        await settling.ExecuteNonQueryAsync(Cancel);
    }
}
