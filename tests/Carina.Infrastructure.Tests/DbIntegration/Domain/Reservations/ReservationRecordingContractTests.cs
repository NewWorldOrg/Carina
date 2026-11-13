using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Reservations;

[Trait("Category", "DbIntegration")]
public sealed class ReservationRecordingContractTests(MigratedRepositoryDatabase database)
    : IClassFixture<MigratedRepositoryDatabase>, IAsyncLifetime
{
    private static readonly DateTime Airs = new(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Tick = new(2026, 8, 24, 20, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime Made = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan[] Ahead = [TimeSpan.Zero, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    private static readonly ReservationState[] States =
    [
        ReservationState.Scheduled,
        ReservationState.Conflict,
        ReservationState.Cancelled,
        ReservationState.Missed,
    ];

    private static readonly (bool Claimed, string? Outcome)[] Claims =
    [
        (false, null),
        (true, null),
        (true, "Complete"),
        (true, "Failed"),
    ];

    private static readonly TimeSpan[] MatrixStarts =
    [
        TimeSpan.FromHours(-2),
        TimeSpan.FromMinutes(-10),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(40),
        TimeSpan.FromHours(4),
    ];

    private static readonly int[] Margins = [0, 30];

    private readonly ServiceProvider migrated = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await migrated.DisposeAsync();

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

        await using (AsyncServiceScope scope = migrated.CreateAsyncScope())
        {
            Context(scope).Add(reservation);
            await Context(scope).SaveChangesAsync();
        }

        RecordingTick tick = Assert.Single(await Ticks(Tick));

        Assert.Equal(reservation.EffectiveStartAt, tick.EffectiveStartAt);
        Assert.Equal(reservation.EffectiveEndAt, tick.EffectiveEndAt);
        Assert.Equal(reservation.Programme, tick.Programme);
    }

    [Fact]
    public async Task TheMarginTheRecordingRunsPastTheProgrammeIsHandedOnAsWell()
    {
        await Clear();
        Reservation reservation = Build(21705, Airs, Margin.OfSeconds(10), Margin.OfSeconds(45));

        await using (AsyncServiceScope scope = migrated.CreateAsyncScope())
        {
            Context(scope).Add(reservation);
            await Context(scope).SaveChangesAsync();
        }

        RecordingTick tick = Assert.Single(await Ticks(Tick));

        Assert.Equal(TimeSpan.FromSeconds(45), tick.MarginAfter);
        Assert.Equal(tick.EffectiveEndAt, reservation.EndAt + tick.MarginAfter);
    }

    [Fact]
    public async Task AReservationMadeWhileItsBroadcastIsRunningIsDueTheMomentItWasMade()
    {
        await Clear();
        DateTime asking = Airs.AddMinutes(50);
        Reservation reservation = Reservation.Plan(
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(21703), Airs),
            null,
            Priority.Default,
            Airs,
            Airs.AddHours(3),
            true,
            Margin.OfSeconds(120),
            Margin.OfSeconds(180),
            new ProgrammeSnapshot(
                "A programme",
                "What it is about",
                string.Empty,
                [new ProgrammeGenre(7, 1)],
                Made,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            asking);

        await using (AsyncServiceScope scope = migrated.CreateAsyncScope())
        {
            Context(scope).Add(reservation);
            await Context(scope).SaveChangesAsync();
        }

        RecordingTick tick = Assert.Single(await Ticks(asking));

        Assert.Equal(asking, tick.EffectiveStartAt);
        Assert.Equal(Airs.AddHours(3).AddSeconds(180), tick.EffectiveEndAt);
        Assert.Equal(Airs, tick.ProgrammeStartsAt);
        Assert.Empty(await DueAt(asking.AddSeconds(-1)));
    }

    [Fact]
    public async Task TheProgrammeTheReservationCopiedIsHandedToTheRecorderWhole()
    {
        await Clear();
        Reservation reservation = Build(21702, Airs, Margin.None, Margin.None);

        await using (AsyncServiceScope scope = migrated.CreateAsyncScope())
        {
            Context(scope).Add(reservation);
            await Context(scope).SaveChangesAsync();
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
    public async Task TheSoundTheReservationCopiedIsHandedToTheRecorderWithTheRest()
    {
        await Clear();
        Reservation reservation = Build(21703, Airs, Margin.None, Margin.None, AudioMode.DualMono, 2);

        await using (AsyncServiceScope scope = migrated.CreateAsyncScope())
        {
            Context(scope).Add(reservation);
            await Context(scope).SaveChangesAsync();
        }

        RecordingTick tick = Assert.Single(await Ticks(Tick));

        Assert.Equal(AudioMode.DualMono, tick.Snapshot.Audio);
        Assert.Equal(2, tick.Snapshot.Sounds);
    }

    [Fact]
    public async Task AReservationOfABroadcastThatAnnouncedNoSoundHandsOnThatItSaidNothing()
    {
        await Clear();
        await Plan(21704, ReservationState.Scheduled);

        RecordingTick tick = Assert.Single(await Ticks(Tick));

        Assert.Equal(AudioMode.Undetermined, tick.Snapshot.Audio);
        Assert.Equal(ProgrammeSnapshot.SoundsUnannounced, tick.Snapshot.Sounds);
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
    public async Task AReservationIsDueAsFarAheadOfItsStartAsTheRecorderPrepares()
    {
        await Clear();
        ReservationId soon = await Plan(21851, ReservationState.Scheduled, airs: Tick.AddSeconds(10));

        Assert.Empty(await DueAt(Tick, TimeSpan.FromSeconds(9)));
        Assert.Equal(soon.Value, Assert.Single(await DueAt(Tick, TimeSpan.FromSeconds(10))));
        Assert.True(await Claim(soon, Tick));
    }

    [Fact]
    public async Task PreparingAheadNeverBringsBackAWindowThatHasClosed()
    {
        await Clear();
        await Plan(21852, ReservationState.Scheduled, airs: Tick.AddHours(-1));

        Assert.Empty(await DueAt(Tick, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task ADriverThatReachesForAClaimAnotherIsHoldingLosesIt()
    {
        await Clear();
        ReservationId airing = await Plan(21901, ReservationState.Scheduled);

        await using NpgsqlConnection holder = await OpenAsync();
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

    [Fact(DisplayName = "BR-RS-001: the reservations the recorder reads as due are the ones each reservation says is due")]
    public async Task TheReservationsReadAsDueAreTheOnesEachSaysIsDue()
    {
        IReadOnlyList<Reservation> matrix = await LaidAsync();

        foreach (DateTime at in new[] { Tick, Tick.AddMinutes(-30), Tick.AddHours(5) })
        {
            foreach (TimeSpan ahead in Ahead)
            {
                await using AsyncServiceScope scope = migrated.CreateAsyncScope();
                IReadOnlyList<RecordingTick> due =
                    await Contract(scope).DueAtAsync(at, ahead, CancellationToken.None);

                Assert.Equal(
                    Ids(matrix.Where(reservation => reservation.IsDueAt(at, ahead))),
                    Ids(due.Select(tick => tick.Id)));
            }
        }
    }

    [Fact(DisplayName = "BR-RS-001: on the instant a window opens, as far ahead as the recorder prepares, and on the instant it closes, the store and each reservation say the same")]
    public async Task OnTheEdgesOfEveryWindowTheStoreAndEachReservationSayTheSame()
    {
        IReadOnlyList<Reservation> matrix = await LaidAsync();

        foreach ((DateTime at, TimeSpan ahead) in Edges(matrix))
        {
            await using AsyncServiceScope scope = migrated.CreateAsyncScope();
            IReadOnlyList<RecordingTick> due =
                await Contract(scope).DueAtAsync(at, ahead, CancellationToken.None);

            Assert.Equal(
                Ids(matrix.Where(reservation => reservation.IsDueAt(at, ahead))),
                Ids(due.Select(tick => tick.Id)));
        }
    }

    [Fact(DisplayName = "BR-RS-001: the claim the store grants is the one each reservation says it is ready for, so a contended reservation never starts")]
    public async Task TheClaimTheStoreGrantsIsTheOneEachReservationIsReadyFor()
    {
        IReadOnlyList<Reservation> matrix = await LaidAsync();

        Assert.Contains(matrix, reservation => reservation.State is ReservationState.Conflict && !reservation.IsPinned);

        foreach (Reservation reservation in matrix)
        {
            await using AsyncServiceScope scope = migrated.CreateAsyncScope();

            Assert.Equal(
                reservation.IsReadyToBeClaimed,
                await Contract(scope).ClaimAsync(reservation.Id, Tick, CancellationToken.None));
        }
    }

    [Fact(DisplayName = "BR-RS-002: the claim the store gives back is the one each reservation says may be given back")]
    public async Task TheClaimTheStoreGivesBackIsTheOneEachReservationAllows()
    {
        IReadOnlyList<Reservation> matrix = await LaidAsync();

        foreach (Reservation reservation in matrix.Where(reservation => reservation.IsPinned))
        {
            DateTime wrong = reservation.StartedAt!.Value.AddSeconds(1);
            DateTime right = reservation.StartedAt!.Value;

            Assert.Equal(reservation.MayBeReleasedFrom(wrong), await ReleaseAsync(reservation.Id, wrong));
            Assert.Equal(reservation.MayBeReleasedFrom(right), await ReleaseAsync(reservation.Id, right));
        }
    }

    private async Task<bool> Release(ReservationId id, DateTime claimedAt)
    {
        await using AsyncServiceScope scope = migrated.CreateAsyncScope();

        return await Contract(scope).ReleaseAsync(id, claimedAt, CancellationToken.None);
    }

    private async Task<bool> QueuedBehindTheHolder()
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            await using NpgsqlConnection watcher = await OpenAsync();
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

    private static Reservation Build(
        int eventId,
        DateTime airs,
        Margin marginBefore,
        Margin marginAfter,
        AudioMode audio = AudioMode.Undetermined,
        int sounds = ProgrammeSnapshot.SoundsUnannounced)
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
            new ProgrammeSnapshot(
                "A programme",
                "What it is about",
                string.Empty,
                [new ProgrammeGenre(7, 1)],
                Made,
                audio,
                sounds),
            null,
            BroadcastGroupRole.Standalone,
            Made);
    }

    private async Task<IReadOnlyList<RecordingTick>> Ticks(DateTime at, TimeSpan? ahead = null)
    {
        await using AsyncServiceScope scope = migrated.CreateAsyncScope();

        return await Contract(scope).DueAtAsync(
            at,
            ahead ?? TimeSpan.Zero,
            CancellationToken.None);
    }

    private async Task<IReadOnlyList<Guid>> DueAt(DateTime at, TimeSpan? ahead = null)
        => [.. (await Ticks(at, ahead)).Select(tick => tick.Id.Value)];

    private async Task<bool> Claim(ReservationId id, DateTime at)
    {
        await using AsyncServiceScope scope = migrated.CreateAsyncScope();

        return await Contract(scope).ClaimAsync(id, at, CancellationToken.None);
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
                snapshot_name, snapshot_summary, snapshot_extended, snapshot_genres,
                snapshot_audio, snapshot_sounds, captured_at,
                epg_diverged, epg_diverged_detail, epg_missing, acknowledged_at,
                broadcast_group_key, broadcast_group_role, state, cancelled_because,
                started_at, recording_outcome, created_at)
            VALUES (
                '{id}', 32736, 1024, {eventId}, '{Sql(starts)}', NULL, 10,
                '{Sql(starts)}', '{Sql(starts.AddHours(1))}', true, {marginBefore}, {marginAfter},
                'A programme', 'What it is about', '', '[]'::jsonb, 'Undetermined', 0, '{Sql(Made)}',
                false, '[]'::jsonb, false, NULL,
                NULL, 'Standalone', '{state}',
                {(state is ReservationState.Cancelled ? "'ByHand'" : "NULL")},
                {(claimed ? $"'{Sql(starts)}'" : "NULL")},
                {(outcome is null ? "NULL" : $"'{outcome}'")}, '{Sql(Made)}')
            """);

        return new ReservationId(id);
    }

    private static string Sql(DateTime at) => at.ToString("yyyy-MM-dd HH:mm:sszzz", null);

    private Task Clear() => Execute("DELETE FROM reservation");

    private async Task Execute(string sql)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> Read(ReservationId id, string column)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT {column} FROM reservation WHERE id = '{id.Value}'",
            connection);
        object? read = await command.ExecuteScalarAsync();

        return read is DBNull ? null : read;
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        await using AsyncServiceScope scope = migrated.CreateAsyncScope();
        var connection = new NpgsqlConnection(Context(scope).Database.GetConnectionString());
        await connection.OpenAsync();

        return connection;
    }

    private static CarinaDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();

    private static IReservationRecordingContract Contract(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IReservationRecordingContract>();

    /// <summary>
    /// Every instant a window of the matrix opens, looked at from as far ahead as the recorder prepares, and every
    /// instant one closes, each with the second before it.
    /// </summary>
    private static IReadOnlyList<(DateTime At, TimeSpan Ahead)> Edges(IReadOnlyList<Reservation> matrix)
    {
        IEnumerable<(DateTime At, TimeSpan Ahead)> opening =
            from reservation in matrix
            from ahead in Ahead
            from at in new[] { reservation.EffectiveStartAt - ahead, reservation.EffectiveStartAt - ahead - TimeSpan.FromSeconds(1) }
            select (at, ahead);
        IEnumerable<(DateTime At, TimeSpan Ahead)> closing =
            from reservation in matrix
            from ahead in Ahead
            from at in new[] { reservation.EffectiveEndAt, reservation.EffectiveEndAt - TimeSpan.FromSeconds(1) }
            select (at, ahead);

        return [.. opening.Concat(closing).Distinct()];
    }

    private static IReadOnlyList<Guid> Ids(IEnumerable<Reservation> reservations)
        => Ids(reservations.Select(reservation => reservation.Id));

    private static IReadOnlyList<Guid> Ids(IEnumerable<ReservationId> ids) => [.. ids.Select(id => id.Value).Order()];

    private async Task<bool> ReleaseAsync(ReservationId id, DateTime claimedAt)
    {
        await using AsyncServiceScope scope = migrated.CreateAsyncScope();

        return await Contract(scope).ReleaseAsync(id, claimedAt, CancellationToken.None);
    }

    /// <summary>
    /// Lays the whole matrix down as rows written straight into the store, then reads every row back as the
    /// reservation it is, so both arms judge the same row.
    /// </summary>
    private async Task<IReadOnlyList<Reservation>> LaidAsync()
    {
        await ExecuteAsync("DELETE FROM reservation");

        int eventId = 30000;

        foreach (Row row in Matrix())
        {
            await LayAsync(++eventId, row.State, Tick + row.Airs, row.Claimed, row.Outcome, row.Margin);
        }

        await using AsyncServiceScope scope = migrated.CreateAsyncScope();

        return await Context(scope).Set<Reservation>().AsNoTracking().ToListAsync();
    }

    private static IEnumerable<Row> Matrix()
        => from state in States
           from claim in Claims
           from airs in MatrixStarts
           from margin in Margins
           select new Row(state, claim.Claimed, claim.Outcome, airs, margin);

    private Task LayAsync(int eventId, ReservationState state, DateTime starts, bool claimed, string? outcome, int margin)
        => ExecuteAsync(
            $"""
            INSERT INTO reservation (
                id, network_id, service_id, event_id, programme_start_at, rule_id, priority,
                start_at, end_at, end_at_confirmed, margin_before, margin_after,
                snapshot_name, snapshot_summary, snapshot_extended, snapshot_genres,
                snapshot_audio, snapshot_sounds, captured_at,
                epg_diverged, epg_diverged_detail, epg_missing, acknowledged_at,
                broadcast_group_key, broadcast_group_role, state, cancelled_because,
                started_at, recording_outcome, created_at)
            VALUES (
                '{Guid.NewGuid()}', 32736, 1024, {eventId}, '{Sql(starts)}', NULL, 10,
                '{Sql(starts)}', '{Sql(starts.AddMinutes(30))}', true, {margin}, {margin},
                'A programme', 'What it is about', '', '[]'::jsonb, 'Undetermined', 0, '{Sql(Made)}',
                false, '[]'::jsonb, false, NULL,
                NULL, 'Standalone', '{state}',
                {(state is ReservationState.Cancelled ? "'ByHand'" : "NULL")},
                {(claimed ? $"'{Sql(starts)}'" : "NULL")},
                {(outcome is null ? "NULL" : $"'{outcome}'")}, '{Sql(Made)}')
            """);

    private async Task ExecuteAsync(string sql)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private readonly record struct Row(ReservationState State, bool Claimed, string? Outcome, TimeSpan Airs, int Margin);
}
