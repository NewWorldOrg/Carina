using System.Globalization;

using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class CatalogKindsVersionMigrationTests
{
    private const string ScratchDatabase = "carina_catalog_kinds_version_test";

    private const string BeforeTheVersionWasKept = "20261018192607_LearningTakesOneRoundADay";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CD-005: an entry kept before the version of the estimation was kept is read as estimated with the first, and waits to be estimated again")]
    public async Task AnEntryKeptBeforeIsReadAsEstimatedWithTheFirstVersion()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheVersionWasKept, Cancel);
        Guid kept = Guid.NewGuid();

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await ExecuteAsync(
                before,
                $"""
                INSERT INTO segment_catalog_entry (id, kind, confidence, decided_by, length, fingerprint, occurrences, series_count, station_count, first_seen_at, last_seen_at, updated_at)
                VALUES ('{kept}', 'Cm', 0.5, 'Pattern', interval '59 seconds', '\x01020304'::bytea, 2, 1, 1, timestamptz '2026-10-01T00:00:00Z', timestamptz '2026-10-08T00:00:00Z', timestamptz '2026-10-08T00:00:00Z')
                """);
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (CarinaDbContext reading = CarinaDbContextFactory.Create(Scratch()))
        {
            CatalogEntry? next = await new CatalogRepository(reading).NextToReestimateAsync(Cancel);

            Assert.Equal(kept, next?.Id.Value);
            Assert.Equal(CatalogKinds.FirstVersion, next?.KindsVersion);
            Assert.True(next?.AwaitsReestimation);
        }

        await migrator.MigrateAsync(BeforeTheVersionWasKept, Cancel);

        await using (NpgsqlConnection back = await OpenAsync())
        {
            Assert.Equal("0", await ScalarAsync(back, "SELECT count(*) FROM information_schema.columns WHERE table_name = 'segment_catalog_entry' AND column_name = 'kinds_version'"));
            Assert.Equal("1", await ScalarAsync(back, "SELECT count(*) FROM segment_catalog_entry"));
        }

        await context.Database.EnsureDeletedAsync(Cancel);
    }

    private static string Scratch()
    {
        string connection = Environment.GetEnvironmentVariable(CarinaDbContextFactory.ConnectionStringVariable)
                            ?? throw new InvalidOperationException($"{CarinaDbContextFactory.ConnectionStringVariable} names the database the tests make theirs beside.");

        return new NpgsqlConnectionStringBuilder(connection) { Database = ScratchDatabase }.ConnectionString;
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        NpgsqlConnection connection = new(Scratch());
        await connection.OpenAsync(Cancel);

        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);

        await command.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<string> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand reading = new(sql, connection);

        return Convert.ToString(await reading.ExecuteScalarAsync(Cancel), CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
