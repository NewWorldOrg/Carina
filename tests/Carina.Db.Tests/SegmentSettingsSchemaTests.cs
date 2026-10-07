using Carina.Infrastructure.Persistence.Configurations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SegmentSettingsSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Changed = "timestamptz '2026-10-07 12:00:00+00'";

    [Fact(DisplayName = "the migrated settings table carries the check that holds it to one row, and no other")]
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

        Assert.Equal([SegmentSettingsConfiguration.SingleRowCheck], names);
    }

    [Theory(DisplayName = "the migrated settings table is one row, whichever way learning stands")]
    [InlineData("1, true", null)]
    [InlineData("1, false", null)]
    [InlineData("2, true", SegmentSettingsConfiguration.SingleRowCheck)]
    [InlineData("0, false", SegmentSettingsConfiguration.SingleRowCheck)]
    public async Task TheMigratedSettingsTableIsOneRow(string values, string? refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using (var clearing = new NpgsqlCommand($"DELETE FROM {SegmentSettingsConfiguration.TableName}", connection))
        {
            await clearing.ExecuteNonQueryAsync();
        }

        await using var writing = new NpgsqlCommand(
            $"INSERT INTO {SegmentSettingsConfiguration.TableName} (id, learning, learning_changed_at) VALUES ({values}, {Changed})",
            connection);

        if (refusedBy is null)
        {
            Assert.Equal(1, await writing.ExecuteNonQueryAsync());

            return;
        }

        Assert.Equal(refusedBy, (await Assert.ThrowsAsync<PostgresException>(writing.ExecuteNonQueryAsync)).ConstraintName);
    }
}
