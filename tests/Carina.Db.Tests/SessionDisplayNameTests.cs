using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SessionDisplayNameTests
{
    private const string ScratchDatabase = "carina_session_display_name_test";

    private const string BeforeTheColumn = "20260831111140_ReservationOutcomeLedger";

    private const string HeldId = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task BrAu018ASessionThatWasOpenBeforeTheColumnIsShownByItsSubjectRatherThanByNothing()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheColumn, Cancel);

        await using (NpgsqlConnection connection = await OpenAsync())
        {
            await using var inserting = new NpgsqlCommand(
                """
                INSERT INTO auth_session (id, subject, method, created_at, last_used_at, device_label)
                VALUES (@id, 'carina', 'Local', now(), now(), 'a device')
                """,
                connection);
            inserting.Parameters.AddWithValue("id", HeldId);
            await inserting.ExecuteNonQueryAsync(Cancel);
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection reading = await OpenAsync();
        await using var asking = new NpgsqlCommand(
            "SELECT display_name FROM auth_session WHERE id = @id",
            reading);
        asking.Parameters.AddWithValue("id", HeldId);

        Assert.Equal("carina", await asking.ExecuteScalarAsync(Cancel));
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(Scratch());
        await connection.OpenAsync(Cancel);

        return connection;
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
