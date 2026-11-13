using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests.DbIntegration.Infra.Segments;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class TheSkipThresholdReachesNinetyNineHundredthsMigrationTests
{
    private const string ScratchDatabase = "carina_skip_threshold_reaches_test";

    private const string BeforeTheReach = "20261026110822_RoundsOfLearningKeepOutACandidateThatLosesOpeningsOrEndings";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CV-002: once the migration is in a threshold up to 0.99 is kept, 1.00 is refused, and going back brings one above 0.95 down to 0.95")]
    public async Task AThresholdUpToNinetyNineHundredthsIsKeptOnceTheMigrationIsIn()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheReach, Cancel);

        await using (NpgsqlConnection before = await OpenAsync())
        {
            Assert.Equal("23514", await RefusedAsync(before, Row("0.99")));
            await RunAsync(before, Row("0.95"));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (NpgsqlConnection forward = await OpenAsync())
        {
            await RunAsync(forward, Set("0.99"));
            Assert.Equal("0.99", await ThresholdAsync(forward));
            Assert.Equal("23514", await RefusedAsync(forward, Set("1.00")));
        }

        await migrator.MigrateAsync(BeforeTheReach, Cancel);

        await using (NpgsqlConnection back = await OpenAsync())
        {
            Assert.Equal("0.95", await ThresholdAsync(back));
            Assert.Equal("23514", await RefusedAsync(back, Set("0.99")));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);
        await context.Database.EnsureDeletedAsync(Cancel);
    }

    private static string Row(string threshold)
        => $"INSERT INTO segment_settings (id, skip_threshold) VALUES (1, {threshold})";

    private static string Set(string threshold)
        => $"UPDATE segment_settings SET skip_threshold = {threshold} WHERE id = 1";

    private static async Task<string> ThresholdAsync(NpgsqlConnection connection)
    {
        await using NpgsqlCommand reading = new("SELECT skip_threshold::text FROM segment_settings WHERE id = 1", connection);

        return (string)(await reading.ExecuteScalarAsync(Cancel))!;
    }

    private static async Task<string?> RefusedAsync(NpgsqlConnection connection, string sql)
    {
        try
        {
            await RunAsync(connection, sql);

            return null;
        }
        catch (PostgresException refused)
        {
            return refused.SqlState;
        }
    }

    private static async Task RunAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand running = new(sql, connection);

        await running.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        NpgsqlConnection connection = new(Scratch());
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
