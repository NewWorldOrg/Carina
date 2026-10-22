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
public sealed class LearningIsAlwaysOnMigrationTests
{
    private const string ScratchDatabase = "carina_learning_always_on_test";

    private const string BeforeTheSwitchWasLetGo = "20261022025418_RoundsOfLearningSayWhichVersionTheyMeasured";

    private const string TheSwitchWasLetGo = "20261022152742_LearningIsAlwaysOnAndEncodesLearnNoWatermarks";

    private const string Learned = """{"recordings": 12, "boundaries": 148, "labelsUntil": null}""";

    private const string AWatermark =
        "INSERT INTO encode_watermark (network_id, service_id, learned_from, learned_at, pattern) VALUES (32736, 1024, gen_random_uuid(), now(), decode(repeat('00', 16200), 'hex'))";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CD-013: where learning was off, the latest version of four kinds made by hand takes the place of the first in use as installed, and the setting, its time and the watermarks go")]
    public async Task WhereLearningWasOffTheLatestVersionMadeByHandIsPutInUse()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheSwitchWasLetGo, Cancel);

        await using (NpgsqlConnection before = await OpenAsync())
        {
            Assert.Equal("1:Active 2:Candidate 3:Candidate 4:Candidate 5:Candidate 6:Candidate 7:Candidate", await StatesAsync(before));
            await ExecuteAsync(before, "INSERT INTO segment_settings (id, learning, learning_changed_at, skip_threshold, skip_threshold_changed_at) VALUES (1, false, now(), 0.65, now())");
            await ExecuteAsync(before, AWatermark);
        }

        await migrator.MigrateAsync(TheSwitchWasLetGo, Cancel);

        await using (CarinaDbContext reading = CarinaDbContextFactory.Create(Scratch()))
        {
            SegmentModelVersion active = await new SegmentModelVersionRepository(reading).ActiveAsync(Cancel);

            Assert.Equal((7, false, SegmentModel.FourKindsInPods), (active.Number, active.ByHand, active.Model));
            Assert.NotNull(active.AdoptedAt);
        }

        await using (NpgsqlConnection after = await OpenAsync())
        {
            Assert.Equal("1:Retired 2:Candidate 3:Candidate 4:Candidate 5:Candidate 6:Candidate 7:Active", await StatesAsync(after));
            Assert.Equal("id skip_threshold skip_threshold_changed_at", await ColumnsAsync(after));
            Assert.Equal("0.65", await ScalarAsync(after, "SELECT skip_threshold::text FROM segment_settings"));
            Assert.Equal("NULL", await ScalarAsync(after, "SELECT coalesce(to_regclass('encode_watermark')::text, 'NULL')"));
        }

        await context.Database.EnsureDeletedAsync(Cancel);
    }

    [Theory(DisplayName = "BR-CD-008: a version learned or chosen by hand stays in use, and a version made by hand already in use stays as it is")]
    [InlineData("learned")]
    [InlineData("chosen by hand")]
    [InlineData("made by hand")]
    public async Task AVersionLearnedChosenOrAlreadyMadeByHandStaysInUse(string inUse)
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheSwitchWasLetGo, Cancel);

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await ExecuteAsync(before, "UPDATE segment_model_version SET state = 'Retired', retired_at = now() WHERE number = 1");
            await ExecuteAsync(before, inUse switch
            {
                "learned" => LearnedFrom(7, 8),
                "chosen by hand" => "UPDATE segment_model_version SET state = 'Active', adopted_at = now(), by_hand = true, chosen_by_hand_at = now() WHERE number = 6",
                _ => "UPDATE segment_model_version SET state = 'Active', adopted_at = now() WHERE number = 7",
            });
        }

        string states = await StatesAsync();

        await migrator.MigrateAsync(TheSwitchWasLetGo, Cancel);

        Assert.Equal(states, await StatesAsync());

        await context.Database.EnsureDeletedAsync(Cancel);
    }

    [Fact(DisplayName = "going back brings learning back on, with or without a row of settings, keeps the version in use where it is, and the watermarks come back empty")]
    public async Task GoingBackBringsLearningBackOn()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(TheSwitchWasLetGo, Cancel);

        await migrator.MigrateAsync(BeforeTheSwitchWasLetGo, Cancel);

        await using (NpgsqlConnection back = await OpenAsync())
        {
            Assert.Equal("1 true 0.70", await ScalarAsync(back, "SELECT concat_ws(' ', count(*), bool_and(learning)::text, max(skip_threshold)::text) FROM segment_settings WHERE learning_changed_at IS NOT NULL"));
            Assert.Equal("1:Retired 2:Candidate 3:Candidate 4:Candidate 5:Candidate 6:Candidate 7:Active", await StatesAsync(back));
            Assert.Equal("0", await ScalarAsync(back, "SELECT count(*) FROM encode_watermark"));
            await ExecuteAsync(back, "UPDATE segment_settings SET skip_threshold = 0.65, skip_threshold_changed_at = now()");
        }

        await migrator.MigrateAsync(TheSwitchWasLetGo, Cancel);
        await migrator.MigrateAsync(BeforeTheSwitchWasLetGo, Cancel);

        await using (NpgsqlConnection again = await OpenAsync())
        {
            Assert.Equal("1 true 0.65", await ScalarAsync(again, "SELECT concat_ws(' ', count(*), bool_and(learning)::text, max(skip_threshold)::text) FROM segment_settings WHERE learning_changed_at IS NOT NULL"));
        }

        await context.Database.EnsureDeletedAsync(Cancel);
    }

    private static string LearnedFrom(int from, int number)
        => $"""
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at, adopted_at, evidence)
            SELECT {number}, 'Active', false, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, now(), now(), '{Learned}'
            FROM segment_model_version WHERE number = {from}
            """;

    private static async Task<string> StatesAsync()
    {
        await using NpgsqlConnection connection = await OpenAsync();

        return await StatesAsync(connection);
    }

    private static Task<string> StatesAsync(NpgsqlConnection connection)
        => ScalarAsync(connection, "SELECT string_agg(number || ':' || state, ' ' ORDER BY number) FROM segment_model_version");

    private static Task<string> ColumnsAsync(NpgsqlConnection connection)
        => ScalarAsync(connection, "SELECT string_agg(column_name::text, ' ' ORDER BY column_name) FROM information_schema.columns WHERE table_name = 'segment_settings'");

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
