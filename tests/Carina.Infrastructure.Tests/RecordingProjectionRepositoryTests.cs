using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingProjectionRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = new(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SettlingARecordingReachesTheReservationWithoutASecondWrite()
    {
        Reservation reservation = Plan(5101);
        await Add(reservation);
        await Claim(reservation.Id);

        Recording recording = Begin(5101, reservation.Id);
        await Add(recording);

        Assert.Null(await Outcome(reservation.Id));

        await using (CarinaDbContext context = database.Open())
        {
            Recording loaded = await context.Set<Recording>()
                .SingleAsync(entity => entity.Id == recording.Id);
            loaded.Abort(Now.AddHours(1));
            loaded.Settle(RecordingOutcome.Complete, 3_400_000_000, Now.AddHours(1));
            await context.SaveChangesAsync();
        }

        Assert.Equal("Complete", await Outcome(reservation.Id));
    }

    [Fact]
    public async Task ARecordingKeepsTheReservationItWasStartedForHereToo()
    {
        Reservation mine = Plan(5102);
        Reservation theirs = Plan(5103);
        await Add(mine);
        await Add(theirs);

        Recording recording = Begin(5102, mine.Id);
        await Add(recording);

        await using NpgsqlConnection connection = await OpenAsync();
        await using var moving = new NpgsqlCommand(
            $"UPDATE recording SET reservation_id = '{theirs.Id.Value}' WHERE id = '{recording.Id.Value}'",
            connection);

        await Assert.ThrowsAsync<PostgresException>(() => moving.ExecuteNonQueryAsync());
    }

    private static Reservation Plan(int eventId)
        => Reservation.Plan(
            ReservationId.New(),
            Programme(eventId),
            null,
            Priority.Default,
            Now,
            Now.AddHours(1),
            true,
            Margin.None,
            Margin.None,
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Now);

    private static Recording Begin(int eventId, ReservationId reservationId)
    {
        RecordingId id = RecordingId.New();

        return Recording.Begin(
            id,
            reservationId,
            Programme(eventId),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            Now,
            Now.AddHours(1),
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Now);
    }

    private static ProgrammeRef Programme(int eventId)
        => new(new NetworkId(32736), new ServiceId(1024), new EventId(eventId), Now);

    private static ProgrammeSnapshot Snapshot()
        => new("A programme", "What it is about", string.Empty, [new ProgrammeGenre(7, 1)], Now);

    private async Task Add<T>(T entity)
        where T : class
    {
        await using CarinaDbContext context = database.Open();
        context.Add(entity);
        await context.SaveChangesAsync();
    }

    private async Task Claim(ReservationId id)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using var claiming = new NpgsqlCommand(
            $"UPDATE reservation SET started_at = timestamptz '2026-08-24 20:00:00+00' WHERE id = '{id.Value}'",
            connection);
        await claiming.ExecuteNonQueryAsync();
    }

    private async Task<string?> Outcome(ReservationId id)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using var reading = new NpgsqlCommand(
            $"SELECT recording_outcome FROM reservation WHERE id = '{id.Value}'",
            connection);
        object? read = await reading.ExecuteScalarAsync();

        return read is DBNull ? null : read as string;
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        await using CarinaDbContext context = database.Open();
        var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();

        return connection;
    }
}
