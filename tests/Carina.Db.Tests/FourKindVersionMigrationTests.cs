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
public sealed class FourKindVersionMigrationTests
{
    private const string ScratchDatabase = "carina_four_kind_version_test";

    private const string BeforeTheVersionWasMade = "20261018215453_CatalogEntriesSayWhichVersionOfTheKindEstimationTheyWereEstimatedWith";

    private const string Learned = """{"confirmedRecordings": 12, "boundaries": 148, "labelsUntil": null}""";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CD-007: the first version of four kinds takes the place of the first version in use after the candidates kept, read back as the model made by hand, and going back puts the first version in use again")]
    public async Task TheFirstVersionOfFourKindsTakesThePlaceOfTheFirst()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheVersionWasMade, Cancel);

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await ExecuteAsync(before, Version(2, "'Candidate'", "NULL", "NULL", $"'{Learned}'"));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (CarinaDbContext reading = CarinaDbContextFactory.Create(Scratch()))
        {
            SegmentModelVersion active = await new SegmentModelVersionRepository(reading).ActiveAsync(Cancel);

            Assert.Equal((3, 4, false), (active.Number, active.Kinds, active.ByHand));
            Assert.Equal(SegmentModel.FirstOfFourKinds, active.Model);
            Assert.Null(active.Evaluation);
        }

        await using (NpgsqlConnection after = await OpenAsync())
        {
            Assert.Equal("1:Retired 2:Candidate 3:Active", await StatesAsync(after));
            await ExecuteAsync(after, "INSERT INTO segment_detection (recording_id, model_version, state, segments, cm_share, snaps, extraction_version, catalog_until, detected_at) VALUES (gen_random_uuid(), 3, 'NothingFound', '[{\"startsAtSec\": 0, \"endsAtSec\": 1800, \"kind\": \"Programme\", \"probability\": 0.9}]', 0, '[]', 2, now(), now())");
        }

        await migrator.MigrateAsync(BeforeTheVersionWasMade, Cancel);

        await using (NpgsqlConnection back = await OpenAsync())
        {
            Assert.Equal("1:Active 2:Candidate", await StatesAsync(back));
            Assert.Equal("NULL", await ScalarAsync(back, "SELECT coalesce(retired_at::text, 'NULL') FROM segment_model_version WHERE number = 1"));
            Assert.Equal("0", await ScalarAsync(back, "SELECT count(*) FROM segment_detection"));
        }

        await context.Database.EnsureDeletedAsync(Cancel);
    }

    [Theory(DisplayName = "BR-CD-008: a version learned or chosen by hand stays in use, and the first version of four kinds is kept beside it as a candidate")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AVersionLearnedOrChosenByHandStaysInUse(bool byHand)
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheVersionWasMade, Cancel);

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await ExecuteAsync(before, "UPDATE segment_model_version SET state = 'Retired', retired_at = now() WHERE number = 1");
            await ExecuteAsync(before, Version(2, "'Active'", "now()", byHand ? "now()" : "NULL", byHand ? "NULL" : $"'{Learned}'"));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (NpgsqlConnection after = await OpenAsync())
        {
            Assert.Equal("1:Retired 2:Active 3:Candidate", await StatesAsync(after));
            Assert.Equal("4", await ScalarAsync(after, "SELECT kinds FROM segment_model_version WHERE number = 3"));
        }

        await migrator.MigrateAsync(BeforeTheVersionWasMade, Cancel);

        await using (NpgsqlConnection back = await OpenAsync())
        {
            Assert.Equal("1:Retired 2:Active", await StatesAsync(back));
        }

        await context.Database.EnsureDeletedAsync(Cancel);
    }

    private static string Version(int number, string state, string adoptedAt, string chosenByHandAt, string evidence)
        => $"""
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at, adopted_at, chosen_by_hand_at, evidence)
            SELECT {number}, {state}, {(chosenByHandAt is "NULL" ? "false" : "true")}, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, now(), {adoptedAt}, {chosenByHandAt}, {evidence}
            FROM segment_model_version WHERE number = 1
            """;

    private static Task<string> StatesAsync(NpgsqlConnection connection)
        => ScalarAsync(connection, "SELECT string_agg(number || ':' || state, ' ' ORDER BY number) FROM segment_model_version");

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
