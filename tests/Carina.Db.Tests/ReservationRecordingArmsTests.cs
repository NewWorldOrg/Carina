using Carina.Domain.Base;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Db.Tests;

/// <summary>
/// The statements the recorder and the reservation ledger run in the store, held to the reservation's own answer for
/// the same rows: which reservations are due, which are claimed, which claims are given back, which are pending and
/// which are thrown away. Every row of a matrix of states, claims, outcomes and windows goes through both.
/// </summary>
[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationRecordingArmsTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Tick = new(2026, 8, 24, 20, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime Made = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

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

    private static readonly TimeSpan[] Airs =
    [
        TimeSpan.FromHours(-2),
        TimeSpan.FromMinutes(-10),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(40),
        TimeSpan.FromHours(4),
    ];

    private static readonly int[] Margins = [0, 30];

    private static readonly TimeSpan[] Ahead = [TimeSpan.Zero, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    [Fact(DisplayName = "BR-RS-001: the reservations the recorder reads as due are the ones each reservation says is due")]
    public async Task TheReservationsReadAsDueAreTheOnesEachSaysIsDue()
    {
        IReadOnlyList<Reservation> matrix = await LaidAsync();

        foreach (DateTime at in new[] { Tick, Tick.AddMinutes(-30), Tick.AddHours(5) })
        {
            foreach (TimeSpan ahead in Ahead)
            {
                await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);
                IReadOnlyList<RecordingTick> due =
                    await new ReservationRecordingContract(context).DueAtAsync(at, ahead, CancellationToken.None);

                Assert.Equal(
                    Ids(matrix.Where(reservation => reservation.IsDueAt(at, ahead))),
                    Ids(due.Select(tick => tick.Id)));
            }
        }
    }

    [Fact(DisplayName = "BR-RS-001: the claim the store grants is the one each reservation says it is ready for, so a contended reservation never starts")]
    public async Task TheClaimTheStoreGrantsIsTheOneEachReservationIsReadyFor()
    {
        IReadOnlyList<Reservation> matrix = await LaidAsync();

        Assert.Contains(matrix, reservation => reservation.State is ReservationState.Conflict && !reservation.IsPinned);

        foreach (Reservation reservation in matrix)
        {
            await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);

            Assert.Equal(
                reservation.IsReadyToBeClaimed,
                await new ReservationRecordingContract(context).ClaimAsync(reservation.Id, Tick, CancellationToken.None));
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

    [Fact(DisplayName = "BR-RS-001: the reservations the store lists as pending are the ones each reservation says is pending")]
    public async Task TheReservationsListedAsPendingAreTheOnesEachSaysIsPending()
    {
        IReadOnlyList<Reservation> matrix = await LaidAsync();
        ReservationWindow[] windows =
        [
            new(Tick - Margin.Longest, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)),
            new(Tick, Tick.AddHours(1)),
            new(Tick.AddHours(-3), Tick.AddHours(-2)),
        ];

        foreach (ReservationWindow window in windows)
        {
            await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);
            IReadOnlyList<Reservation> pending =
                await new ReservationRepository(context).ListPendingAsync(window, CancellationToken.None);

            Assert.Equal(
                Ids(matrix.Where(reservation => reservation.IsPendingWithin(window))),
                Ids(pending.Select(reservation => reservation.Id)));
        }
    }

    [Fact(DisplayName = "BR-RS-001: what the store does with a reservation asked to be thrown away is what the reservation says it comes to")]
    public async Task WhatTheStoreDoesWithAReservationThrownAwayIsWhatItSaysItComesTo()
    {
        IReadOnlyList<Reservation> matrix = await LaidAsync();

        foreach (Reservation reservation in matrix)
        {
            await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);

            Assert.Equal(
                reservation.Discarding(false, Tick),
                await new ReservationRepository(context).DiscardAsync(reservation.Id, Tick, CancellationToken.None));
        }
    }

    private static IReadOnlyList<Guid> Ids(IEnumerable<Reservation> reservations)
        => Ids(reservations.Select(reservation => reservation.Id));

    private static IReadOnlyList<Guid> Ids(IEnumerable<ReservationId> ids) => [.. ids.Select(id => id.Value).Order()];

    private async Task<bool> ReleaseAsync(ReservationId id, DateTime claimedAt)
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);

        return await new ReservationRecordingContract(context).ReleaseAsync(id, claimedAt, CancellationToken.None);
    }

    /// <summary>
    /// Lays the whole matrix down as rows written straight into the store, then reads every row back as the
    /// reservation it is, so both arms judge the same row.
    /// </summary>
    private async Task<IReadOnlyList<Reservation>> LaidAsync()
    {
        await ExecuteAsync("DELETE FROM reservation");

        int eventId = 30000;

        foreach (ReservationState state in States)
        {
            foreach ((bool claimed, string? outcome) in Claims)
            {
                foreach (TimeSpan airs in Airs)
                {
                    foreach (int margin in Margins)
                    {
                        await LayAsync(++eventId, state, Tick + airs, claimed, outcome, margin);
                    }
                }
            }
        }

        await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);

        return await context.Set<Reservation>().AsNoTracking().ToListAsync();
    }

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

    private static string Sql(DateTime at) => at.ToString("yyyy-MM-dd HH:mm:sszzz", null);

    private async Task ExecuteAsync(string sql)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
