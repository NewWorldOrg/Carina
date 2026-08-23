using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationRecordingContractTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Airs = new(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Tick = new(2026, 8, 24, 20, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime Made = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task OneQueryRebuildsWhatShouldBeRecordingRightNow()
    {
        await Clear();

        ReservationId airing = await Plan(21001, ReservationState.Scheduled);
        ReservationId later = await Plan(21002, ReservationState.Scheduled, airs: Airs.AddHours(4));
        ReservationId over = await Plan(21003, ReservationState.Scheduled, airs: Airs.AddHours(-4));
        ReservationId contested = await Plan(21004, ReservationState.Conflict);
        ReservationId dropped = await Plan(21005, ReservationState.Cancelled);
        ReservationId running = await Plan(21006, ReservationState.Scheduled, airs: Airs.AddHours(-4), claimed: true);
        ReservationId finished = await Plan(21007, ReservationState.Scheduled, claimed: true, outcome: "Complete");
        ReservationId abandoned = await Plan(21008, ReservationState.Cancelled, airs: Airs.AddHours(-4), claimed: true);

        IReadOnlyList<Guid> due = await DueAt(Tick);

        Assert.Equal(Sorted(airing, running, abandoned), Sorted([.. due]));
        Assert.DoesNotContain(later.Value, due);
        Assert.DoesNotContain(over.Value, due);
        Assert.DoesNotContain(contested.Value, due);
        Assert.DoesNotContain(dropped.Value, due);
        Assert.DoesNotContain(finished.Value, due);
    }

    [Fact]
    public async Task AReservationInConflictDoesNotStartWhenItsTickArrives()
    {
        await Clear();
        ReservationId contested = await Plan(21101, ReservationState.Conflict);

        Assert.Empty(await DueAt(Tick));
        Assert.False(await Claim(contested, Tick));
        Assert.Null(await Read(contested, "started_at"));
    }

    [Fact]
    public async Task ARecordingStillInFlightIsThereAfterTheDriverComesBack()
    {
        await Clear();
        ReservationId running = await Plan(21201, ReservationState.Cancelled, airs: Airs.AddDays(-1), claimed: true);

        RecordingTick tick = Assert.Single(await Ticks(Tick));

        Assert.Equal(running, tick.Id);
        Assert.True(tick.InFlight);
    }

    [Fact]
    public async Task ARecordingThatEndedIsNoLongerInFlight()
    {
        await Clear();
        await Plan(21301, ReservationState.Cancelled, airs: Airs.AddDays(-1), claimed: true, outcome: "Truncated");

        Assert.Empty(await DueAt(Tick));
    }

    [Fact]
    public async Task TheClaimIsWonOnce()
    {
        await Clear();
        ReservationId airing = await Plan(21401, ReservationState.Scheduled);

        Assert.True(await Claim(airing, Tick));
        Assert.False(await Claim(airing, Tick.AddSeconds(1)));
        Assert.Equal(Tick, await Read(airing, "started_at"));
    }

    [Fact]
    public async Task TheClaimLeavesEverythingElseOnTheRowWhereItWas()
    {
        await Clear();
        ReservationId airing = await Plan(21501, ReservationState.Scheduled);

        Assert.True(await Claim(airing, Tick));

        Assert.Equal("Scheduled", await Read(airing, "state"));
        Assert.Equal(10, Convert.ToInt32(await Read(airing, "priority")));
        Assert.Equal("Recording", await Read(airing, "composite_state"));
        Assert.Null(await Read(airing, "recording_outcome"));
    }

    [Fact]
    public async Task ACancelledReservationIsNotClaimed()
    {
        await Clear();
        ReservationId dropped = await Plan(21601, ReservationState.Cancelled);

        Assert.False(await Claim(dropped, Tick));
    }

    [Fact]
    public async Task TheWindowTheTickReportsIsTheOneTheReservationComputes()
    {
        await Clear();
        Reservation reservation = Build(21701, Airs, Margin.OfSeconds(10), Margin.OfSeconds(30));

        await using (CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString))
        {
            context.Add(reservation);
            await context.SaveChangesAsync();
        }

        RecordingTick tick = Assert.Single(await Ticks(Tick));

        Assert.Equal(reservation.EffectiveStartAt, tick.EffectiveStartAt);
        Assert.Equal(reservation.EffectiveEndAt, tick.EffectiveEndAt);
        Assert.Equal(reservation.SnapshotName, tick.Name);
        Assert.Equal(reservation.Programme, tick.Programme);
    }

    [Fact]
    public async Task TheMarginsDecideWhetherTheTickHasArrived()
    {
        await Clear();
        ReservationId airing = await Plan(
            21801,
            ReservationState.Scheduled,
            airs: Tick.AddSeconds(10),
            marginBefore: 10);

        Assert.Single(await DueAt(Tick));
        Assert.Empty(await DueAt(Tick.AddSeconds(-1)));
        Assert.True(await Claim(airing, Tick));
    }

    private static IReadOnlyList<Guid> Sorted(params ReservationId[] ids)
        => [.. ids.Select(id => id.Value).Order()];

    private static IReadOnlyList<Guid> Sorted(IReadOnlyList<Guid> ids) => [.. ids.Order()];

    private static Reservation Build(int eventId, DateTime airs, Margin marginBefore, Margin marginAfter)
    {
        var programme = new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(eventId), airs);

        return Reservation.Plan(
            ReservationId.New(),
            programme,
            null,
            Priority.Default,
            airs,
            airs.AddHours(1),
            true,
            marginBefore,
            marginAfter,
            new ProgrammeSnapshot("A programme", "What it is about", string.Empty, [new ProgrammeGenre(7, 1)], Made),
            null,
            BroadcastGroupRole.Standalone,
            Made);
    }

    private async Task<IReadOnlyList<RecordingTick>> Ticks(DateTime at)
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);

        return await new ReservationRecordingContract(context).DueAtAsync(at, CancellationToken.None);
    }

    private async Task<IReadOnlyList<Guid>> DueAt(DateTime at)
        => [.. (await Ticks(at)).Select(tick => tick.Id.Value)];

    private async Task<bool> Claim(ReservationId id, DateTime at)
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);

        return await new ReservationRecordingContract(context).ClaimAsync(id, at, CancellationToken.None);
    }

    private async Task<ReservationId> Plan(
        int eventId,
        ReservationState state,
        DateTime? airs = null,
        bool claimed = false,
        string? outcome = null,
        int marginBefore = 0,
        int marginAfter = 0)
    {
        DateTime starts = airs ?? Airs;
        var id = Guid.NewGuid();

        await Execute(
            $"""
            INSERT INTO reservation (
                id, network_id, service_id, event_id, programme_start_at, rule_id, priority,
                start_at, end_at, end_at_confirmed, margin_before, margin_after,
                snapshot_name, snapshot_summary, snapshot_extended, snapshot_genres, captured_at,
                epg_diverged, epg_diverged_detail, epg_missing, acknowledged_at,
                broadcast_group_key, broadcast_group_role, state, started_at, recording_outcome, created_at)
            VALUES (
                '{id}', 32736, 1024, {eventId}, '{Sql(starts)}', NULL, 10,
                '{Sql(starts)}', '{Sql(starts.AddHours(1))}', true, {marginBefore}, {marginAfter},
                'A programme', 'What it is about', '', '[]'::jsonb, '{Sql(Made)}',
                false, '[]'::jsonb, false, NULL,
                NULL, 'Standalone', '{state}',
                {(claimed ? $"'{Sql(starts)}'" : "NULL")},
                {(outcome is null ? "NULL" : $"'{outcome}'")}, '{Sql(Made)}')
            """);

        return new ReservationId(id);
    }

    private static string Sql(DateTime at) => at.ToString("yyyy-MM-dd HH:mm:sszzz", null);

    private Task Clear() => Execute("DELETE FROM reservation");

    private async Task Execute(string sql)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> Read(ReservationId id, string column)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT {column} FROM reservation WHERE id = '{id.Value}'",
            connection);
        object? read = await command.ExecuteScalarAsync();

        return read is DBNull ? null : read;
    }
}
