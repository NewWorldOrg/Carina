using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class QualityThresholdByHandMigrationTests
{
    private const string ScratchDatabase = "carina_quality_threshold_by_hand_test";

    private const string BeforeALevelCouldBeMeasured = "20261003190316_AnArtefactKeepsWhatBecameOfItsTextTrackOfCaptions";

    private const string Updated = "timestamptz '2026-10-01 03:00:00+00'";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-QD-023: a level kept before a level could be measured was set by hand, so no measurement moves it")]
    public async Task ALevelKeptBeforeALevelCouldBeMeasuredWasSetByHand()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeALevelCouldBeMeasured, Cancel);

        await using (NpgsqlConnection writing = await OpenAsync())
        {
            await using var inserting = new NpgsqlCommand(
                $"""
                INSERT INTO quality_threshold (
                    threshold_key, default_value, current_value, provisional, observations, updated_at, updated_by)
                VALUES ('CarrierToNoiseFloor', 15000, 12000, true, 0, {Updated}, NULL)
                """,
                writing);

            await inserting.ExecuteNonQueryAsync(Cancel);
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection reading = await OpenAsync();
        await using var asking = new NpgsqlCommand(
            "SELECT by_hand, provisional, current_value, measured_value IS NULL FROM quality_threshold",
            reading);
        await using NpgsqlDataReader row = await asking.ExecuteReaderAsync(Cancel);

        Assert.True(await row.ReadAsync(Cancel));
        Assert.True(row.GetBoolean(0));
        Assert.True(row.GetBoolean(1));
        Assert.Equal(12000, row.GetDouble(2));
        Assert.True(row.GetBoolean(3));
        Assert.False(await row.ReadAsync(Cancel));
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
