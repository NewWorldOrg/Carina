using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SegmentSettingsRollbackTests
{
    private const string ScratchDatabase = "carina_segment_settings_rollback_test";

    private const string BeforeTheThreshold = "20261018165013_TheAnswersAndTheSkipsAreKeptWithoutAKeyIntoTheRecording";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "the settings go back to before the threshold, letting go of a row where only the threshold was changed, and forward again with the threshold at its default")]
    public async Task TheSettingsGoBackToBeforeTheThresholdAndForwardAgain()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (NpgsqlConnection writing = await OpenAsync())
        {
            await RunAsync(writing, "INSERT INTO segment_settings (id, learning, skip_threshold, skip_threshold_changed_at) VALUES (1, false, 0.65, now())");
        }

        await migrator.MigrateAsync(BeforeTheThreshold, Cancel);

        await using (NpgsqlConnection reading = await OpenAsync())
        {
            Assert.Equal("0", await ScalarAsync(reading, "SELECT count(*) FROM segment_settings"));
            await RunAsync(reading, "INSERT INTO segment_settings (id, learning, learning_changed_at) VALUES (1, true, now())");
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection forward = await OpenAsync();

        Assert.Equal(
            "true 0.70 NULL",
            await ScalarAsync(forward, "SELECT concat_ws(' ', learning::text, skip_threshold::text, coalesce(skip_threshold_changed_at::text, 'NULL')) FROM segment_settings"));
    }

    private static async Task RunAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand running = new NpgsqlCommand(sql, connection);

        await running.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<string> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand reading = new NpgsqlCommand(sql, connection);

        return Convert.ToString(await reading.ExecuteScalarAsync(Cancel), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        NpgsqlConnection connection = new NpgsqlConnection(Scratch());
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
