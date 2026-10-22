using Carina.Infrastructure.Persistence.Configurations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SegmentSettingsSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Changed = "timestamptz '2026-10-07 12:00:00+00'";

    [Fact(DisplayName = "the migrated settings table carries the checks that hold it to one row and the threshold to its range, and no other")]
    public async Task TheMigratedSettingsTableCarriesTheSingleRowCheck()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var reading = new NpgsqlCommand(
            $"""
            SELECT conname FROM pg_constraint
            WHERE conrelid = '{SegmentSettingsConfiguration.TableName}'::regclass AND contype = 'c'
            ORDER BY conname
            """,
            connection);

        List<string> names = [];
        await using NpgsqlDataReader row = await reading.ExecuteReaderAsync();

        while (await row.ReadAsync())
        {
            names.Add(row.GetString(0));
        }

        Assert.Equal([SegmentSettingsConfiguration.SingleRowCheck, SegmentSettingsConfiguration.SkipThresholdCheck], names);
    }

    [Fact(DisplayName = "BR-CV-002: a row written without the threshold stands at 0.70, changed by nobody")]
    public async Task ARowWrittenWithoutTheThresholdStandsAtItsDefault()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await RunAsync(connection, $"DELETE FROM {SegmentSettingsConfiguration.TableName}");

        await RunAsync(connection, $"INSERT INTO {SegmentSettingsConfiguration.TableName} (id) VALUES (1)");

        Assert.Equal(
            "0.70 NULL",
            await ScalarAsync(connection, $"SELECT concat_ws(' ', skip_threshold::text, coalesce(skip_threshold_changed_at::text, 'NULL')) FROM {SegmentSettingsConfiguration.TableName}"));
    }

    [Fact(DisplayName = "the migrated settings table holds the threshold and when it changed, and nothing that switches learning")]
    public async Task TheMigratedSettingsTableHoldsNoSwitch()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            "id skip_threshold skip_threshold_changed_at",
            await ScalarAsync(connection, $"SELECT string_agg(column_name::text, ' ' ORDER BY column_name) FROM information_schema.columns WHERE table_name = '{SegmentSettingsConfiguration.TableName}'"));
    }

    [Theory(DisplayName = "BR-CV-002: the migrated settings table keeps a threshold from 0.50 to 0.95, in hundredths, and refuses one outside")]
    [InlineData("0.50", "0.50")]
    [InlineData("0.95", "0.95")]
    [InlineData("0.654", "0.65")]
    [InlineData("0.49", null)]
    [InlineData("0.96", null)]
    [InlineData("0", null)]
    public async Task TheMigratedSettingsTableKeepsTheThresholdInItsRange(string written, string? kept)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await RunAsync(connection, $"DELETE FROM {SegmentSettingsConfiguration.TableName}");
        string inserting = $"INSERT INTO {SegmentSettingsConfiguration.TableName} (id, skip_threshold, skip_threshold_changed_at) VALUES (1, {written}, {Changed})";

        if (kept is null)
        {
            PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => RunAsync(connection, inserting));

            Assert.Equal((PostgresErrorCodes.CheckViolation, SegmentSettingsConfiguration.SkipThresholdCheck), (refused.SqlState, refused.ConstraintName));

            return;
        }

        await RunAsync(connection, inserting);

        Assert.Equal(kept, await ScalarAsync(connection, $"SELECT skip_threshold::text FROM {SegmentSettingsConfiguration.TableName}"));
    }

    [Theory(DisplayName = "the migrated settings table is one row")]
    [InlineData("1, 0.70", null)]
    [InlineData("1, 0.65", null)]
    [InlineData("2, 0.70", SegmentSettingsConfiguration.SingleRowCheck)]
    [InlineData("0, 0.65", SegmentSettingsConfiguration.SingleRowCheck)]
    public async Task TheMigratedSettingsTableIsOneRow(string values, string? refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using (var clearing = new NpgsqlCommand($"DELETE FROM {SegmentSettingsConfiguration.TableName}", connection))
        {
            await clearing.ExecuteNonQueryAsync();
        }

        await using var writing = new NpgsqlCommand(
            $"INSERT INTO {SegmentSettingsConfiguration.TableName} (id, skip_threshold, skip_threshold_changed_at) VALUES ({values}, {Changed})",
            connection);

        if (refusedBy is null)
        {
            Assert.Equal(1, await writing.ExecuteNonQueryAsync());

            return;
        }

        Assert.Equal(refusedBy, (await Assert.ThrowsAsync<PostgresException>(writing.ExecuteNonQueryAsync)).ConstraintName);
    }

    private static async Task RunAsync(NpgsqlConnection connection, string sql)
    {
        await using var running = new NpgsqlCommand(sql, connection);

        await running.ExecuteNonQueryAsync();
    }

    private static async Task<string> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var reading = new NpgsqlCommand(sql, connection);

        return Convert.ToString(await reading.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
