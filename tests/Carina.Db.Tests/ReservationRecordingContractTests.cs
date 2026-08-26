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
        Assert.Equal(reservation.Programme, tick.Programme);
    }

    [Fact]
    public async Task TheProgrammeTheReservationCopiedIsHandedToTheRecorderWhole()
    {
        await Clear();
        Reservation reservation = Build(21702, Airs, Margin.None, Margin.None);

        await using (CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString))
        {
            context.Add(reservation);
            await context.SaveChangesAsync();
        }

        RecordingTick tick = Assert.Single(await Ticks(Tick));

        Assert.Equal("A programme", tick.Snapshot.Name);
        Assert.Equal("What it is about", tick.Snapshot.Summary);
        Assert.Equal(string.Empty, tick.Snapshot.Extended);
        Assert.Equal([new ProgrammeGenre(7, 1)], tick.Snapshot.Genres);
        Assert.Equal(Made, tick.Snapshot.CapturedAt);
        Assert.Equal(DateTimeKind.Utc, tick.Snapshot.CapturedAt.Kind);
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

    [Fact]
    public async Task ADriverThatReachesForAClaimAnotherIsHoldingLosesIt()
    {
        await Clear();
        ReservationId airing = await Plan(21901, ReservationState.Scheduled);

        await using NpgsqlConnection holder = await database.OpenAsync();
        await using NpgsqlTransaction held = await holder.BeginTransactionAsync();
        await using (var winner = new NpgsqlCommand(
            $"UPDATE reservation SET started_at = '{Sql(Tick)}' WHERE id = '{airing.Value}' "
            + "AND started_at IS NULL AND state = 'Scheduled'",
            holder,
            held))
        {
            Assert.Equal(1, await winner.ExecuteNonQueryAsync());
        }

        Task<bool> latecomer = Claim(airing, Tick.AddSeconds(1));

        Assert.True(
            await QueuedBehindTheHolder(),
            "The second claim never queued behind the first, so the database was not the one deciding it.");

        await held.CommitAsync();

        Assert.False(await latecomer);
        Assert.Equal(Tick, await Read(airing, "started_at"));
    }

    [Fact]
    public async Task TheTickIsOverAtTheInstantTheEffectiveWindowEnds()
    {
        await Clear();
        await Plan(22001, ReservationState.Scheduled, airs: Tick.AddHours(-1));

        Assert.Single(await DueAt(Tick.AddSeconds(-1)));
        Assert.Empty(await DueAt(Tick));
    }

    [Fact]
    public async Task WhatIsDueComesBackInTheOrderTheRecorderCanPlanAgainst()
    {
        await Clear();
        ReservationId last = await Plan(22103, ReservationState.Scheduled, airs: Airs.AddMinutes(20));
        ReservationId first = await Plan(22101, ReservationState.Scheduled, airs: Airs);
        ReservationId middle = await Plan(22102, ReservationState.Scheduled, airs: Airs.AddMinutes(10));

        Assert.Equal([first.Value, middle.Value, last.Value], await DueAt(Tick));
    }

    [Fact]
    public async Task ATickIsAskedForAsAUtcInstantOrNotAtAll()
    {
        await Clear();
        await Plan(22201, ReservationState.Scheduled);

        foreach (DateTimeKind kind in new[] { DateTimeKind.Unspecified, DateTimeKind.Local })
        {
            DateTime ambiguous = DateTime.SpecifyKind(Tick, kind);

            await Assert.ThrowsAsync<ArgumentException>(() => Ticks(ambiguous));
            await Assert.ThrowsAsync<ArgumentException>(() => Claim(ReservationId.New(), ambiguous));
        }
    }

    [Fact]
    public async Task AClaimThatStartedNothingIsGivenBack()
    {
        await Clear();
        ReservationId airing = await Plan(22301, ReservationState.Scheduled);

        Assert.True(await Claim(airing, Tick));
        Assert.True(await Release(airing, Tick));
        Assert.Null(await Read(airing, "started_at"));
        Assert.Equal("Scheduled", await Read(airing, "composite_state"));
        Assert.True(await Claim(airing, Tick.AddSeconds(1)));
    }

    [Fact]
    public async Task AClaimIsOnlyGivenBackByTheRecorderHoldingIt()
    {
        await Clear();
        ReservationId airing = await Plan(22401, ReservationState.Scheduled);

        Assert.True(await Claim(airing, Tick));
        Assert.False(await Release(airing, Tick.AddSeconds(1)));
        Assert.Equal(Tick, await Read(airing, "started_at"));
    }

    [Fact]
    public async Task ARecordingThatAlreadyEndedKeepsTheClaimThatStartedIt()
    {
        await Clear();
        ReservationId finished = await Plan(22501, ReservationState.Scheduled, claimed: true, outcome: "Complete");

        Assert.False(await Release(finished, Airs));
        Assert.Equal(Airs, await Read(finished, "started_at"));
    }

    [Fact]
    public async Task AClaimIsGivenBackAsAUtcInstantOrNotAtAll()
    {
        await Clear();

        foreach (DateTimeKind kind in new[] { DateTimeKind.Unspecified, DateTimeKind.Local })
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => Release(ReservationId.New(), DateTime.SpecifyKind(Tick, kind)));
        }
    }

    private async Task<bool> Release(ReservationId id, DateTime claimedAt)
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);

        return await new ReservationRecordingContract(context).ReleaseAsync(id, claimedAt, CancellationToken.None);
    }

    private async Task<bool> QueuedBehindTheHolder()
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            await using NpgsqlConnection watcher = await database.OpenAsync();
            await using var waiting = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity "
                + "WHERE wait_event_type = 'Lock' AND query LIKE '%UPDATE reservation SET started_at%'",
                watcher);

            if (Convert.ToInt64(await waiting.ExecuteScalarAsync(), null) > 0)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
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
