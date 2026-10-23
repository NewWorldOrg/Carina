using System.Globalization;

using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class CatalogWeightsMigrationTests
{
    private const string ScratchDatabase = "carina_catalog_weights_test";

    private const string BeforeTheCatalogWasTakenOut = "20261019102212_SegmentsAreNotAnsweredByHand";

    private const string TheCatalogWasTakenOut = "20261019121457_VersionsWeighNothingTheCatalogHears";

    private const string Learned = """{"recordings": 12, "boundaries": 148, "labelsUntil": null}""";

    private const string TwoKinds = """{"classes": {"Break": {"bias": -2.5, "features": {"isMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0}}}}""";

    private const string FourKinds =
        """{"classes": {"Break": {"bias": -2.5, "features": {"isMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0}}, "Opening": {"bias": -6.0, "features": {"pastOpeningShare": 4.0}}, "Ending": {"bias": -6.0, "features": {"pastEndingShare": 4.0}}}}""";

    private const string TwoKindsWithTheCatalog =
        """{"classes": {"Break": {"bias": -2.5, "features": {"isMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0, "catalogCmShare": 4.0}}}}""";

    private const string TwoKindsScalesWithTheCatalog =
        """{"isMultipleOf15s": {"mean": 0, "sd": 1}, "edgeSilence": {"mean": 0, "sd": 1}, "logoShare": {"mean": 0, "sd": 1}, "catalogCmShare": {"mean": 0, "sd": 1}}""";

    private const string FourKindsWithTheCatalog =
        """{"classes": {"Break": {"bias": -2.5, "features": {"isMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0, "catalogCmShare": 4.0}}, "Opening": {"bias": -6.0, "features": {"catalogOpeningShare": 8.0, "pastOpeningShare": 4.0}}, "Ending": {"bias": -6.0, "features": {"catalogEndingShare": 8.0, "pastEndingShare": 4.0}}}}""";

    private const string FourKindsScalesWithTheCatalog =
        """{"isMultipleOf15s": {"mean": 0, "sd": 1}, "edgeSilence": {"mean": 0, "sd": 1}, "logoShare": {"mean": 0, "sd": 1}, "catalogCmShare": {"mean": 0, "sd": 1}, "catalogOpeningShare": {"mean": 0, "sd": 1}, "pastOpeningShare": {"mean": 0, "sd": 1}, "catalogEndingShare": {"mean": 0, "sd": 1}, "pastEndingShare": {"mean": 0, "sd": 1}}""";

    private const string Initial = """{"silenceDbfs": -50, "blackLevel": 0.10, "sceneChange": 0.30, "gridToleranceSec": 1.0, "gridPenalty": -3.0, "lambda": 1, "lambdaStation": 10, "lambdaSeries": 10}""";

    private const string Catalog = "'{catalogShare,catalogCmShare,catalogProgrammeShare,catalogOpeningShare,catalogEndingShare,catalogOccurrences,catalogSeries,catalogStations,catalogCmMultipleOf15s}'::text[]";

    private const string NoOffsets = "{}";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CD-007: every version kept loses the catalog's weights and scales and no version is added or moved; going back gives the versions made by hand their catalog weights again, and a learned version stays without them")]
    public async Task EveryVersionLosesTheCatalogsWeights()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheCatalogWasTakenOut, Cancel);

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await ExecuteAsync(before, MadeByHand(1, "'Retired'", 2, TwoKindsWithTheCatalog, TwoKindsScalesWithTheCatalog, "now()", "now()"));
            await ExecuteAsync(before, MadeByHand(2, "'Active'", 4, FourKindsWithTheCatalog, FourKindsScalesWithTheCatalog, "now()", "NULL"));
            await ExecuteAsync(before, LearnedFrom(2, 3));
        }

        await migrator.MigrateAsync(TheCatalogWasTakenOut, Cancel);

        await using (NpgsqlConnection after = await OpenAsync())
        {
            Assert.Equal("1:Retired 2:Active 3:Candidate", await StatesAsync(after));
            Assert.Equal("0", await ScalarAsync(after, $"SELECT count(*) FROM segment_model_version WHERE standardisation ?| {Catalog} OR weights::text LIKE '%catalog%'"));
            Assert.Equal(
                "1:t:t 2:f:t 3:f:t",
                await ScalarAsync(after, $"SELECT string_agg(number || ':' || (weights = '{TwoKinds}'::jsonb)::text::char || ':' || (weights = '{TwoKinds}'::jsonb OR weights = '{FourKinds}'::jsonb)::text::char, ' ' ORDER BY number) FROM segment_model_version"));
        }

        await migrator.MigrateAsync(BeforeTheCatalogWasTakenOut, Cancel);

        await using (NpgsqlConnection back = await OpenAsync())
        {
            Assert.Equal("1:Retired 2:Active 3:Candidate", await StatesAsync(back));
            Assert.Equal("1:t 2:t 3:f", await ScalarAsync(back, $"SELECT string_agg(number || ':' || (standardisation ?| {Catalog})::text::char, ' ' ORDER BY number) FROM segment_model_version"));
            Assert.Equal(
                "4.0 8.0",
                await ScalarAsync(back, "SELECT concat_ws(' ', (weights #>> '{classes,Break,features,catalogCmShare}'), (weights #>> '{classes,Opening,features,catalogOpeningShare}')) FROM segment_model_version WHERE number = 2"));
        }

        await context.Database.EnsureDeletedAsync(Cancel);
    }

    private static string MadeByHand(int number, string state, int kinds, string weights, string scales, string adoptedAt, string retiredAt)
        => $"""
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at, adopted_at, retired_at)
            VALUES ({number}, {state}, false, {kinds}, '{weights}', '{NoOffsets}', '{NoOffsets}', '{Initial}', '{scales}', now(), {adoptedAt}, {retiredAt})
            """;

    private static string LearnedFrom(int from, int number, string state = "'Candidate'", string adoptedAt = "NULL")
        => $"""
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at, adopted_at, evidence)
            SELECT {number}, {state}, false, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, now(), {adoptedAt}, '{Learned}'
            FROM segment_model_version WHERE number = {from}
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
