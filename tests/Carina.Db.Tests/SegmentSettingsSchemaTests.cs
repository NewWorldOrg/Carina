using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SegmentSettingsSchemaTests
{
    private const string ScratchDatabase = "carina_segment_settings_test";

    private const string BeforeTheThreshold = "20261018102824_TheChaptersOfAnArtefactSayWhichDetectionTheyCameFrom";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CV-002: a row written without the threshold stands at 0.70, changed by nobody, and learning need not have been switched")]
    public async Task ARowWrittenWithoutTheThresholdStandsAtItsDefault()
    {
        await MigratedAsync();
        await using NpgsqlConnection connection = await OpenAsync();

        await RunAsync(connection, "INSERT INTO segment_settings (id, learning) VALUES (1, false)");

        Assert.Equal(
            "0.70 NULL NULL",
            await ScalarAsync(connection, "SELECT concat_ws(' ', skip_threshold::text, coalesce(skip_threshold_changed_at::text, 'NULL'), coalesce(learning_changed_at::text, 'NULL')) FROM segment_settings"));
    }

    [Theory(DisplayName = "BR-CV-002: the table keeps a threshold from 0.50 to 0.95, in hundredths, and refuses one outside")]
    [InlineData("0.50", "0.50")]
    [InlineData("0.95", "0.95")]
    [InlineData("0.654", "0.65")]
    [InlineData("0.49", null)]
    [InlineData("0.96", null)]
    [InlineData("0", null)]
    public async Task TheTableKeepsAThresholdFromHalfToNineteenTwentieths(string written, string? kept)
    {
        await MigratedAsync();
        await using NpgsqlConnection connection = await OpenAsync();
        string insert = $"INSERT INTO segment_settings (id, learning, skip_threshold, skip_threshold_changed_at) VALUES (1, false, {written}, now())";

        if (kept is null)
        {
            PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => RunAsync(connection, insert));

            Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
            Assert.Equal("ck_segment_settings_skip_threshold", refused.ConstraintName);

            return;
        }

        await RunAsync(connection, insert);

        Assert.Equal(kept, await ScalarAsync(connection, "SELECT skip_threshold::text FROM segment_settings"));
    }

    [Fact(DisplayName = "the settings go back to before the threshold, keeping a row where learning was switched and letting go of one where only the threshold was changed, and forward again")]
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

        Assert.Equal("true 0.70 NULL", await ScalarAsync(forward, "SELECT concat_ws(' ', learning::text, skip_threshold::text, coalesce(skip_threshold_changed_at::text, 'NULL')) FROM segment_settings"));
    }

    private static async Task MigratedAsync()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        await context.GetService<IMigrator>().MigrateAsync(cancellationToken: Cancel);
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
