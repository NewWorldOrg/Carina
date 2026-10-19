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
public sealed class CatalogWeightsMigrationTests
{
    private const string ScratchDatabase = "carina_catalog_weights_test";

    private const string BeforeTheCatalogWasTakenOut = "20261019102212_SegmentsAreNotAnsweredByHand";

    private const string Learned = """{"recordings": 12, "boundaries": 148, "labelsUntil": null}""";

    private const string LearningOn = "INSERT INTO segment_settings (id, learning, learning_changed_at) VALUES (1, true, now())";

    private const string Catalog = "'{catalogShare,catalogCmShare,catalogProgrammeShare,catalogOpeningShare,catalogEndingShare,catalogOccurrences,catalogSeries,catalogStations,catalogCmMultipleOf15s}'::text[]";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CD-007: every version kept loses the catalog's weights and scales, and while learning is on the version of four kinds made by hand without them takes the place of the one made by hand in use; going back puts that one in use again with its catalog weights, and keeps what viewers did")]
    public async Task TheVersionWithoutTheCatalogTakesThePlaceOfTheOneMadeByHand()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheCatalogWasTakenOut, Cancel);

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await ExecuteAsync(before, LearningOn);
            await ExecuteAsync(before, "UPDATE segment_model_version SET state = 'Retired', retired_at = now() WHERE number = 1");
            await ExecuteAsync(before, "UPDATE segment_model_version SET state = 'Active', adopted_at = now() WHERE number = 2");
            await ExecuteAsync(before, LearnedFrom(2, 3));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (CarinaDbContext reading = CarinaDbContextFactory.Create(Scratch()))
        {
            IReadOnlyList<SegmentModelVersion> kept = await new SegmentModelVersionRepository(reading).ListAsync(Cancel);
            SegmentModelVersion active = await new SegmentModelVersionRepository(reading).ActiveAsync(Cancel);

            Assert.Equal((4, 4, false), (active.Number, active.Kinds, active.ByHand));
            Assert.Null(active.Evaluation);
            Assert.Equal(SegmentModel.FirstOfFourKinds, active.Model);
            Assert.Equal(SegmentModel.First, kept.Single(version => version.Number == 1).Model);
            Assert.Equal(SegmentModel.FirstOfFourKinds, kept.Single(version => version.Number == 2).Model);
            Assert.Equal(SegmentModel.FirstOfFourKinds.Weights, kept.Single(version => version.Number == 3).Weights);
        }

        await using (NpgsqlConnection after = await OpenAsync())
        {
            Assert.Equal("1:Retired 2:Retired 3:Candidate 4:Active", await StatesAsync(after));
            Assert.Equal("0", await ScalarAsync(after, $"SELECT count(*) FROM segment_model_version WHERE standardisation ?| {Catalog} OR weights::text LIKE '%catalog%'"));
            await ExecuteAsync(after, "INSERT INTO segment_detection (recording_id, model_version, state, segments, cm_share, snaps, extraction_version, catalog_until, detected_at) VALUES (gen_random_uuid(), 4, 'NothingFound', '[{\"startsAtSec\": 0, \"endsAtSec\": 1800, \"kind\": \"Programme\", \"probability\": 0.9}]', 0, '[]', 2, now(), now())");
            await ExecuteAsync(after, "INSERT INTO segment_skip_event (recording_id, happened_at, event, kind, from_at, to_at, model_version) VALUES (gen_random_uuid(), now(), 'Skipped', 'Break', interval '30 seconds', interval '60 seconds', 4)");
        }

        await migrator.MigrateAsync(BeforeTheCatalogWasTakenOut, Cancel);

        await using (NpgsqlConnection back = await OpenAsync())
        {
            Assert.Equal("1:Retired 2:Active 3:Candidate", await StatesAsync(back));
            Assert.Equal("NULL", await ScalarAsync(back, "SELECT coalesce(retired_at::text, 'NULL') FROM segment_model_version WHERE number = 2"));
            Assert.Equal("1:t 2:t 3:f", await ScalarAsync(back, $"SELECT string_agg(number || ':' || (standardisation ?| {Catalog})::text::char, ' ' ORDER BY number) FROM segment_model_version"));
            Assert.Equal(
                "4.0 8.0",
                await ScalarAsync(back, "SELECT concat_ws(' ', (weights #>> '{classes,Break,features,catalogCmShare}'), (weights #>> '{classes,Opening,features,catalogOpeningShare}')) FROM segment_model_version WHERE number = 2"));
            Assert.Equal("0", await ScalarAsync(back, "SELECT count(*) FROM segment_detection"));
            Assert.Equal("1 NULL", await ScalarAsync(back, "SELECT concat_ws(' ', count(*), coalesce(max(model_version)::text, 'NULL')) FROM segment_skip_event"));
        }

        await context.Database.EnsureDeletedAsync(Cancel);
    }

    [Fact(DisplayName = "BR-CD-013: while learning is off, the first version stays in use and the version without the catalog waits as a candidate after the first version of four kinds, and turning learning on puts the later one in use")]
    public async Task WhileLearningIsOffTheVersionWithoutTheCatalogWaits()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheCatalogWasTakenOut, Cancel);

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (NpgsqlConnection after = await OpenAsync())
        {
            Assert.Equal("1:Active 2:Candidate 3:Candidate", await StatesAsync(after));
        }

        await using (CarinaDbContext changing = CarinaDbContextFactory.Create(Scratch()))
        {
            SegmentModelVersion? active = await new SegmentModelVersionRepository(changing).ChangeAsync(SegmentVersionChange.LatestMadeByHand, DateTime.UtcNow, Cancel);

            Assert.Equal((3, SegmentModel.FirstOfFourKinds), (active?.Number, active?.Model));
        }

        await context.Database.EnsureDeletedAsync(Cancel);
    }

    [Theory(DisplayName = "BR-CD-008: a version learned or chosen by hand stays in use without the catalog's weights, and the version without the catalog is kept beside it as a candidate")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AVersionLearnedOrChosenByHandStaysInUse(bool byHand)
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheCatalogWasTakenOut, Cancel);

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await ExecuteAsync(before, LearningOn);
            await ExecuteAsync(before, "UPDATE segment_model_version SET state = 'Retired', retired_at = now() WHERE number = 1");
            await ExecuteAsync(before, byHand
                ? "UPDATE segment_model_version SET state = 'Active', adopted_at = now(), by_hand = true, chosen_by_hand_at = now() WHERE number = 2"
                : LearnedFrom(2, 3, "'Active'", "now()"));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (NpgsqlConnection after = await OpenAsync())
        {
            Assert.Equal(byHand ? "1:Retired 2:Active 3:Candidate" : "1:Retired 2:Candidate 3:Active 4:Candidate", await StatesAsync(after));
            Assert.Equal("0", await ScalarAsync(after, $"SELECT count(*) FROM segment_model_version WHERE standardisation ?| {Catalog}"));
        }

        await context.Database.EnsureDeletedAsync(Cancel);
    }

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
