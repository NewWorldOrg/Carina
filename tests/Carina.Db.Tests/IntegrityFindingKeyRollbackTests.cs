using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class IntegrityFindingKeyRollbackTests
{
    private const string ScratchDatabase = "carina_integrity_rollback_test";

    private const string BeforeAFindingKeptItsName = "20260913150647_ThePictureABroadcastAnnounces";

    private static readonly Guid Named = new("6f1d0b3a-1111-4222-8333-444444444444");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ANameTwoSweepsBothCarriedDoesNotStopTheKeyFromGoingBackToTheNameAlone()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(cancellationToken: Cancel);

        Guid first;
        Guid again;

        await using (NpgsqlConnection writing = await OpenAsync())
        {
            first = await CheckAsync(writing, "2026-08-26 05:00:00+00");
            again = await CheckAsync(writing, "2026-08-27 05:00:00+00");
            await FindingAsync(writing, first);
            await FindingAsync(writing, again);
        }

        await migrator.MigrateAsync(BeforeAFindingKeptItsName, Cancel);

        await using NpgsqlConnection reading = await OpenAsync();

        Assert.Equal([again], await ChecksCarryingTheNameAsync(reading));
        Assert.Equal(
            "CREATE UNIQUE INDEX pk_integrity_finding ON public.integrity_finding USING btree (id)",
            await KeyAsync(reading));
    }

    private static async Task<Guid> CheckAsync(NpgsqlConnection connection, string startedAt)
    {
        var id = Guid.NewGuid();

        await using var writing = new NpgsqlCommand(
            "INSERT INTO integrity_check (id, started_at, finished_at, roots_walked, roots_out_of_reach, "
            + "files_read, ledger_rows_read, ledger_rows_judged, ledger_rows_still_writing, "
            + "ledger_rows_in_roots_out_of_reach) VALUES "
            + $"('{id}', timestamptz '{startedAt}', timestamptz '{startedAt}', 1, 0, 1, 1, 1, 0, 0)",
            connection);
        await writing.ExecuteNonQueryAsync(Cancel);

        return id;
    }

    private static async Task FindingAsync(NpgsqlConnection connection, Guid check)
    {
        await using var writing = new NpgsqlCommand(
            "INSERT INTO integrity_finding "
            + "(id, check_id, fault, recording_id, ledger_size, observed_size, output_root, path, noticed_at) "
            + $"VALUES ('{Named}', '{check}', 'NoLedgerRow', NULL, NULL, 1, 'primary', 'stray.m2ts', now())",
            connection);
        await writing.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<IReadOnlyList<Guid>> ChecksCarryingTheNameAsync(NpgsqlConnection connection)
    {
        await using var reading = new NpgsqlCommand(
            $"SELECT check_id FROM integrity_finding WHERE id = '{Named}' ORDER BY check_id",
            connection);
        await using NpgsqlDataReader rows = await reading.ExecuteReaderAsync(Cancel);
        List<Guid> read = [];

        while (await rows.ReadAsync(Cancel))
        {
            read.Add(rows.GetGuid(0));
        }

        return read;
    }

    private static async Task<string> KeyAsync(NpgsqlConnection connection)
    {
        await using var reading = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'pk_integrity_finding'",
            connection);

        return (string)(await reading.ExecuteScalarAsync(Cancel))!;
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
