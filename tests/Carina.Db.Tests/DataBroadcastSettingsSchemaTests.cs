using Carina.Infrastructure.Persistence.Configurations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class DataBroadcastSettingsSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Changed = "timestamptz '2026-10-23 12:00:00+00'";

    [Fact(DisplayName = "BR-BD-016: the migrated settings table holds the postal code, the prefecture and when they changed, under the checks that keep it one row of values it can hold")]
    public async Task TheMigratedSettingsTableHoldsWhereTheReceiverIs()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            "changed_at id postal_code prefecture",
            await ScalarAsync(connection, $"SELECT string_agg(column_name::text, ' ' ORDER BY column_name) FROM information_schema.columns WHERE table_name = '{DataBroadcastSettingsConfiguration.TableName}'"));
        Assert.Equal(
            string.Join(
                ' ',
                new[]
                {
                    DataBroadcastSettingsConfiguration.PostalCodeCheck,
                    DataBroadcastSettingsConfiguration.PrefectureCheck,
                    DataBroadcastSettingsConfiguration.SingleRowCheck,
                }.Order(StringComparer.Ordinal)),
            await ScalarAsync(connection, $"SELECT string_agg(conname::text, ' ' ORDER BY conname) FROM pg_constraint WHERE conrelid = '{DataBroadcastSettingsConfiguration.TableName}'::regclass AND contype = 'c'"));
    }

    [Theory(DisplayName = "BR-BD-016: the migrated settings table keeps one row of a seven digit postal code and a prefecture from 0 to 49, either of them none")]
    [InlineData("1, '0000000', 0", null)]
    [InlineData("1, NULL, 49", null)]
    [InlineData("1, NULL, NULL", null)]
    [InlineData("2, NULL, NULL", DataBroadcastSettingsConfiguration.SingleRowCheck)]
    [InlineData("1, '000000', NULL", DataBroadcastSettingsConfiguration.PostalCodeCheck)]
    [InlineData("1, '000000a', NULL", DataBroadcastSettingsConfiguration.PostalCodeCheck)]
    [InlineData("1, '', NULL", DataBroadcastSettingsConfiguration.PostalCodeCheck)]
    [InlineData("1, NULL, 50", DataBroadcastSettingsConfiguration.PrefectureCheck)]
    [InlineData("1, NULL, -1", DataBroadcastSettingsConfiguration.PrefectureCheck)]
    public async Task TheMigratedSettingsTableKeepsWhatItCanHold(string values, string? refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await RunAsync(connection, $"DELETE FROM {DataBroadcastSettingsConfiguration.TableName}");
        string inserting = $"INSERT INTO {DataBroadcastSettingsConfiguration.TableName} (id, postal_code, prefecture, changed_at) VALUES ({values}, {Changed})";

        if (refusedBy is null)
        {
            await RunAsync(connection, inserting);

            Assert.Equal("1", await ScalarAsync(connection, $"SELECT count(*) FROM {DataBroadcastSettingsConfiguration.TableName}"));

            return;
        }

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => RunAsync(connection, inserting));

        Assert.Equal((PostgresErrorCodes.CheckViolation, refusedBy), (refused.SqlState, refused.ConstraintName));
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
