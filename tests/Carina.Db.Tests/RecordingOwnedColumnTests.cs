using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingOwnedColumnTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SavingAReservationCannotPutTheClaimOrTheOutcomeIntoTheDatabase()
    {
        Reservation reservation = Reservation.Rehydrate(
            ReservationId.New(),
            Programme(61001),
            null,
            Priority.Default,
            Now.AddHours(2),
            Now.AddHours(3),
            true,
            Margin.None,
            Margin.None,
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            ReservationState.Scheduled,
            Now,
            RecordingOutcome.Complete,
            false,
            [],
            false,
            null,
            false,
            null,
            Now);

        await using (CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString))
        {
            context.Add(reservation);
            await context.SaveChangesAsync();
        }

        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Null(await Read(connection, reservation.Id, "started_at"));
        Assert.Null(await Read(connection, reservation.Id, "recording_outcome"));
    }

    [Fact]
    public async Task AClaimWrittenBesideTheChangeTrackerSurvivesTheNextSave()
    {
        Reservation reservation = Reservation.Plan(
            ReservationId.New(),
            Programme(61002),
            null,
            Priority.Default,
            Now.AddHours(2),
            Now.AddHours(3),
            true,
            Margin.None,
            Margin.None,
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Now);

        await using (CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString))
        {
            context.Add(reservation);
            await context.SaveChangesAsync();
        }

        await using NpgsqlConnection connection = await database.OpenAsync();
        await using (var claim = new NpgsqlCommand(
            $"UPDATE reservation SET started_at = timestamptz '2026-08-24 21:59:50+00' "
            + $"WHERE id = '{reservation.Id.Value}' AND started_at IS NULL AND state = 'Scheduled'",
            connection))
        {
            Assert.Equal(1, await claim.ExecuteNonQueryAsync());
        }

        await using (CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString))
        {
            Reservation loaded = await context.Set<Reservation>()
                .SingleAsync(entity => entity.Id == reservation.Id);

            loaded.Reprioritise(new Priority(20));
            await context.SaveChangesAsync();
        }

        Assert.NotNull(await Read(connection, reservation.Id, "started_at"));
        Assert.Equal(20, Convert.ToInt32(await Read(connection, reservation.Id, "priority")));
        Assert.Equal("Recording", await Read(connection, reservation.Id, "composite_state"));
    }

    private static ProgrammeRef Programme(int eventId)
        => new(new NetworkId(32736), new ServiceId(1024), new EventId(eventId), Now.AddHours(2));

    private static ProgrammeSnapshot Snapshot()
        => new("A programme", "What it is about", string.Empty, [new ProgrammeGenre(7, 1)], Now);

    private static async Task<object?> Read(NpgsqlConnection connection, ReservationId id, string column)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT {column} FROM reservation WHERE id = '{id.Value}'",
            connection);
        object? read = await command.ExecuteScalarAsync();

        return read is DBNull ? null : read;
    }
}
