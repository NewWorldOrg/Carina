using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Tests.Reservations;

/// <summary>
/// The counts are over the whole table, and the table is shared with every other test in the
/// collection, so each test reads the counts before and after what it lays down and asserts on
/// the difference.
/// </summary>
[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationHealthTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = ReservationFixtures.Now;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task AReservationThatLostItsContestIsCountedAndOneThatIsSecuredIsNot()
    {
        ReservationHealth before = await HealthAsync(Now);

        Reservation lost = ReservationFixtures.Planned(startAt: Now.AddHours(2));
        lost.Contend();
        await AddAsync(lost, ReservationFixtures.Planned(startAt: Now.AddHours(2)));

        ReservationHealth after = await HealthAsync(Now);

        Assert.Equal(1, after.Contended - before.Contended);
        Assert.Equal(Now, after.AsOf);
    }

    [Fact]
    public async Task AContestAlreadyDecidedByTheClockIsHistoryRatherThanHealth()
    {
        ReservationHealth before = await HealthAsync(Now);

        Reservation over = ReservationFixtures.Planned(startAt: Now.AddHours(-3), endAt: Now.AddHours(-2));
        over.Contend();
        Reservation heldOpenByItsMargin = ReservationFixtures.Planned(
            startAt: Now.AddHours(-2),
            endAt: Now.AddSeconds(-10),
            marginAfter: Margin.OfSeconds(30));
        heldOpenByItsMargin.Contend();
        await AddAsync(over, heldOpenByItsMargin);

        ReservationHealth after = await HealthAsync(Now);

        Assert.Equal(1, after.Contended - before.Contended);
    }

    [Fact]
    public async Task WhatIsCancelledMissedOrAlreadyRecordedStandsInNobodysWay()
    {
        ReservationHealth before = await HealthAsync(Now);

        Reservation cancelled = ReservationFixtures.Planned(startAt: Now.AddHours(2));
        cancelled.Contend();
        cancelled.Cancel();
        Reservation missed = ReservationFixtures.Rehydrated(ReservationState.Missed, startAt: Now.AddHours(2));
        Reservation recorded = ReservationFixtures.Rehydrated(
            ReservationState.Scheduled,
            startAt: Now.AddHours(-2),
            endAt: Now.AddHours(2),
            receptionUnavailable: true,
            receptionUnavailableSince: Now);
        await AddAsync(cancelled, missed, recorded);
        await RecordedAsync(recorded.Id, Now.AddHours(-2), RecordingOutcome.Complete);

        ReservationHealth after = await HealthAsync(Now);

        Assert.Equal(before.Contended, after.Contended);
        Assert.Equal(before.ReceptionUnavailable, after.ReceptionUnavailable);
        Assert.Equal(before.EpgDiverged, after.EpgDiverged);
        Assert.Equal(before.EpgMissing, after.EpgMissing);
    }

    [Fact]
    public async Task AReservationWithNowhereToTuneIsCounted()
    {
        ReservationHealth before = await HealthAsync(Now);

        Reservation nowhere = ReservationFixtures.Planned(startAt: Now.AddHours(2));
        nowhere.LoseReception(Now);
        await AddAsync(nowhere);

        ReservationHealth after = await HealthAsync(Now);

        Assert.Equal(1, after.ReceptionUnavailable - before.ReceptionUnavailable);
        Assert.Equal(before.Contended, after.Contended);
    }

    [Fact]
    public async Task ADivergenceOrAVanishedProgrammeIsCountedUntilSomebodyAcknowledgesIt()
    {
        ReservationHealth before = await HealthAsync(Now);

        Reservation moved = ReservationFixtures.Planned(startAt: Now.AddHours(2));
        moved.Diverge([new EpgDivergence(DivergedField.StartAt, "12:00", "12:05", Now)]);
        Reservation seen = ReservationFixtures.Planned(startAt: Now.AddHours(2));
        seen.Diverge([new EpgDivergence(DivergedField.Name, "Before", "After", Now)]);
        seen.Acknowledge(Now);
        Reservation vanished = ReservationFixtures.Planned(startAt: Now.AddHours(2));
        vanished.Disappear();
        Reservation vanishedAndSeen = ReservationFixtures.Planned(startAt: Now.AddHours(2));
        vanishedAndSeen.Disappear();
        vanishedAndSeen.Acknowledge(Now);
        await AddAsync(moved, seen, vanished, vanishedAndSeen);

        ReservationHealth after = await HealthAsync(Now);

        Assert.Equal(1, after.EpgDiverged - before.EpgDiverged);
        Assert.Equal(1, after.EpgMissing - before.EpgMissing);
    }

    [Fact]
    public async Task TheMomentIsAUtcInstantOrTheCountIsRefused()
    {
        await using CarinaDbContext context = database.Open();

        await Assert.ThrowsAsync<ArgumentException>(() => new ReservationRepository(context).HealthAsync(
            new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Local),
            Cancel));
    }

    private async Task<ReservationHealth> HealthAsync(DateTime at)
    {
        await using CarinaDbContext context = database.Open();

        return await new ReservationRepository(context).HealthAsync(at, Cancel);
    }

    private async Task RecordedAsync(ReservationId id, DateTime claimedAt, RecordingOutcome outcome)
    {
        await using CarinaDbContext context = database.Open();
        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync(Cancel);

        await using var writing = new NpgsqlCommand(
            $"UPDATE reservation SET started_at = timestamptz '{claimedAt:yyyy-MM-dd HH:mm:ss}+00', "
            + $"recording_outcome = '{outcome}' WHERE id = '{id.Value}'",
            connection);

        await writing.ExecuteNonQueryAsync(Cancel);
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
}
