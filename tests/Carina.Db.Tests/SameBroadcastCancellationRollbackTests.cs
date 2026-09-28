using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SameBroadcastCancellationRollbackTests
{
    private const string ScratchDatabase = "carina_same_broadcast_rollback_test";

    private const string BeforeAReservationStoodAside = "20260928085857_ProgrammesKeepTheirRunningStatus";

    private static readonly DateTime Now = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task AReservationThatStoodAsideForTheSameBroadcastGoesBackAsCancelledByHand()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(cancellationToken: Cancel);

        Reservation aside = Planned();
        aside.Cancel(ReservationCancellation.SameBroadcast);

        await using (CarinaDbContext writing = CarinaDbContextFactory.Create(Scratch()))
        {
            await new ReservationRepository(writing).AddAsync(aside, Cancel);
        }

        await migrator.MigrateAsync(BeforeAReservationStoodAside, Cancel);

        await using NpgsqlConnection reading = new(Scratch());
        await reading.OpenAsync(Cancel);
        await using NpgsqlCommand asking = new(
            $"SELECT cancelled_because FROM reservation WHERE id = '{aside.Id.Value}'",
            reading);

        Assert.Equal("ByHand", (string?)await asking.ExecuteScalarAsync(Cancel));
    }

    private static Reservation Planned()
    {
        ProgrammeRef programme = new(new NetworkId(32001), new ServiceId(1024), new EventId(4001), Now.AddDays(1));

        return Reservation.Plan(
            ReservationId.New(),
            programme,
            null,
            Priority.Default,
            programme.StartsAt,
            programme.StartsAt.AddHours(1),
            true,
            Margin.None,
            Margin.None,
            new ProgrammeSnapshot(
                "A programme",
                "What it is about",
                string.Empty,
                [],
                Now,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            Now);
    }

    private static string Scratch()
    {
        string? configured = Environment.GetEnvironmentVariable(CarinaDbContextFactory.ConnectionStringVariable);

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"DbIntegration tests need {CarinaDbContextFactory.ConnectionStringVariable} pointing at the compose db service.");
        }

        return new NpgsqlConnectionStringBuilder(configured) { Database = ScratchDatabase }.ConnectionString;
    }
}
