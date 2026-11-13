using System.Globalization;

using Carina.Infrastructure.Persistence;

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

    private const string TwoKinds = """{"classes": {"Break": {"bias": -2.5, "features": {"isMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0}}}}""";

    private const string TwoKindsScales = """{"isMultipleOf15s": {"mean": 0, "sd": 1}, "edgeSilence": {"mean": 0, "sd": 1}, "logoShare": {"mean": 0, "sd": 1}}""";

    private const string Initial = """{"silenceDbfs": -50, "blackLevel": 0.10, "sceneChange": 0.30, "gridToleranceSec": 1.0, "gridPenalty": -3.0, "lambda": 1, "lambdaStation": 10, "lambdaSeries": 10}""";

    private const string AWatermark =
        "INSERT INTO encode_watermark (network_id, service_id, learned_from, learned_at, pattern) VALUES (32736, 1024, gen_random_uuid(), now(), decode(repeat('00', 16200), 'hex'))";

    private const string NoOffsets = "{}";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CD-013: the setting, its time and the watermarks go, and no version of the model moves")]
    public async Task TheSettingAndTheWatermarksGoAndNoVersionMoves()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheSwitchWasLetGo, Cancel);

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await ExecuteAsync(before, MadeByHand(1, "'Active'", "now()"));
            await ExecuteAsync(before, MadeByHand(2, "'Candidate'", "NULL"));
            await ExecuteAsync(before, "INSERT INTO segment_settings (id, learning, learning_changed_at, skip_threshold, skip_threshold_changed_at) VALUES (1, false, now(), 0.65, now())");
            await ExecuteAsync(before, AWatermark);
        }

        await migrator.MigrateAsync(TheSwitchWasLetGo, Cancel);

        await using (NpgsqlConnection after = await OpenAsync())
        {
            Assert.Equal("1:Active 2:Candidate", await StatesAsync(after));
            Assert.Equal("id skip_threshold skip_threshold_changed_at", await ColumnsAsync(after));
            Assert.Equal("0.65", await ScalarAsync(after, "SELECT skip_threshold::text FROM segment_settings"));
            Assert.Equal("NULL", await ScalarAsync(after, "SELECT coalesce(to_regclass('encode_watermark')::text, 'NULL')"));
        }

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
            Assert.Equal("0", await ScalarAsync(back, "SELECT count(*) FROM segment_model_version"));
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

    private static string MadeByHand(int number, string state, string adoptedAt)
        => $"""
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at, adopted_at)
            VALUES ({number}, {state}, false, 2, '{TwoKinds}', '{NoOffsets}', '{NoOffsets}', '{Initial}', '{TwoKindsScales}', now(), {adoptedAt})
            """;

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
