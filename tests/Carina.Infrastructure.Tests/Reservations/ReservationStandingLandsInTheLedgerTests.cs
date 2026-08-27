using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Configurations;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Tests.Reservations;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationStandingLandsInTheLedgerTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly StandingCase[] Reachable =
    [
        new(ReservationStanding.Scheduled, ReservationState.Scheduled, false, null),
        new(ReservationStanding.Conflict, ReservationState.Conflict, false, null),
        new(ReservationStanding.Cancelled, ReservationState.Cancelled, false, null),
        new(ReservationStanding.Missed, ReservationState.Missed, false, null),
        new(ReservationStanding.Recording, ReservationState.Scheduled, true, null),
        new(ReservationStanding.Complete, ReservationState.Scheduled, true, RecordingOutcome.Complete),
        new(ReservationStanding.Truncated, ReservationState.Scheduled, true, RecordingOutcome.Truncated),
        new(ReservationStanding.Failed, ReservationState.Scheduled, true, RecordingOutcome.Failed),
    ];

    public static TheoryData<ReservationStanding, ReservationState, bool, RecordingOutcome?> EachOfThem
    {
        get
        {
            var data = new TheoryData<ReservationStanding, ReservationState, bool, RecordingOutcome?>();

            foreach (StandingCase reachable in Reachable)
            {
                data.Add(reachable.Standing, reachable.State, reachable.Claimed, reachable.Outcome);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EachOfThem))]
    public async Task WhatAReservationSaysItStandsAsIsWhatTheLedgerWorkedOutForIt(
        ReservationStanding standing,
        ReservationState state,
        bool claimed,
        RecordingOutcome? outcome)
    {
        Reservation held = await LaidDownAsync(state, claimed, outcome);

        await using CarinaDbContext context = database.Open();
        Reservation found = (await new ReservationRepository(context).FindAsync(held.Id, Cancel))!;

        Assert.Equal(standing, found.Standing);
        Assert.Equal(standing.ToString(), await ComputedAsync(context, found));
    }

    [Fact]
    public void EveryStandingAReservationCanShowIsOneTheLedgerCanBeMadeToShow()
    {
        Assert.Equal(
            [.. Enum.GetValues<ReservationStanding>().Order()],
            [.. Reachable.Select(reachable => reachable.Standing).Distinct().Order()]);
    }

    [Fact]
    public async Task TheStandingAskedForIsTheOnlyOneTheListHandsBack()
    {
        var laid = new List<(ReservationStanding Standing, ReservationId Id)>();

        foreach (StandingCase reachable in Reachable)
        {
            Reservation held = await LaidDownAsync(reachable.State, reachable.Claimed, reachable.Outcome);

            laid.Add((reachable.Standing, held.Id));
        }

        foreach ((ReservationStanding standing, ReservationId id) in laid)
        {
            await using CarinaDbContext context = database.Open();
            ReservationQuery query = ReservationQuery.For(
                null,
                null,
                perPage: ReservationQuery.MostPerPage,
                conditions: new ReservationConditions { Standings = [standing] })!;

            IReadOnlyList<Reservation> found =
                (await new ReservationRepository(context).ListAsync(query, Cancel)).Items;

            Assert.Contains(id, found.Select(reservation => reservation.Id));
            Assert.All(found, reservation => Assert.Equal(standing, reservation.Standing));
        }
    }

    public sealed record StandingCase(
        ReservationStanding Standing,
        ReservationState State,
        bool Claimed,
        RecordingOutcome? Outcome);

    private static async Task<string> ComputedAsync(CarinaDbContext context, Reservation found)
        => await context.Set<Reservation>()
            .Where(reservation => reservation.Id == found.Id)
            .Select(reservation => EF.Property<string>(reservation, ReservationConfiguration.CompositeState))
            .SingleAsync(Cancel);

    private async Task<Reservation> LaidDownAsync(
        ReservationState state,
        bool claimed,
        RecordingOutcome? outcome)
    {
        Reservation held = ReservationFixtures.Rehydrated(state);

        await using (CarinaDbContext writing = database.Open())
        {
            await new ReservationRepository(writing).AddAsync(held, Cancel);
        }

        if (claimed)
        {
            await RunAsync(
                $"UPDATE reservation SET started_at = timestamptz '2026-08-24 12:00:00+00' WHERE id = '{held.Id.Value}'");
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
