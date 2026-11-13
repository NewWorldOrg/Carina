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
public sealed class VersionsBuiltInMigrationTests
{
    private const string ScratchDatabase = "carina_versions_built_in_test";

    private const string BeforeTheMark = "20261022152742_LearningIsAlwaysOnAndEncodesLearnNoWatermarks";

    private const string TheMark = "20261023075840_VersionsBuiltInAreMarkedAndThoseNeverUsedLetGo";

    private const string TwoKinds = """{"classes": {"Break": {"bias": -2.5, "features": {"isMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0}}}}""";

    private const string TwoKindsScales = """{"isMultipleOf15s": {"mean": 0, "sd": 1}, "edgeSilence": {"mean": 0, "sd": 1}, "logoShare": {"mean": 0, "sd": 1}}""";

    private const string Initial = """{"silenceDbfs": -50, "blackLevel": 0.10, "sceneChange": 0.30, "gridToleranceSec": 1.0, "gridPenalty": -3.0, "lambda": 1, "lambdaStation": 10, "lambdaSeries": 10}""";

    private const string InPods =
        """{"classes": {"Break": {"bias": -2.5, "features": {"isShortMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0, "advertBeside": 1.5}}, "Opening": {"bias": -6.0, "features": {"openingWindow": 4.8, "isThemeLength": 0.5, "loudnessRange": -0.4, "isShortMultipleOf15s": -2.8, "pastOpeningShare": 4.0}}, "Ending": {"bias": -6.0, "features": {"endingWindow": 4.8, "isThemeLength": 0.5, "loudnessRange": -0.4, "isShortMultipleOf15s": -2.8, "pastEndingShare": 4.0}}}}""";

    private const string InPodsScales =
        """{"isMultipleOf15s": {"mean": 0, "sd": 1}, "edgeSilence": {"mean": 0, "sd": 1}, "logoShare": {"mean": 0, "sd": 1}, "pastOpeningShare": {"mean": 0, "sd": 1}, "pastEndingShare": {"mean": 0, "sd": 1}, "openingWindow": {"mean": 0, "sd": 1}, "endingWindow": {"mean": 0, "sd": 1}, "isThemeLength": {"mean": 0, "sd": 1}, "loudnessRange": {"mean": 10, "sd": 1}, "isShortMultipleOf15s": {"mean": 0, "sd": 1}, "advertBeside": {"mean": 0, "sd": 1}}""";

    private const string BelowPauses = """{"silenceDbfs": -55, "blackLevel": 0.10, "sceneChange": 0.30, "gridToleranceSec": 1.0, "gridPenalty": -3.0, "lambda": 1, "lambdaStation": 10, "lambdaSeries": 10}""";

    private const string Learned = """{"recordings": 12, "boundaries": 148, "labelsUntil": null}""";

    private const string Profile = "4d5e6f70-8192-4a3b-b4c5-d6e7f8091a2b";

    private const string Destination = "5e6f7081-92a3-4b4c-85d6-e7f8091a2b3c";

    private const string Recording = "6f708192-a3b4-4c5d-96e7-f8091a2b3c4d";

    private const string NoOffsets = "{}";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CS-003: the versions made by hand are marked built in, those never put in use and referred to by nothing are let go, the first used before, the one in use and a learned candidate stay under their numbers, and the built-in version of this release is the one in use")]
    public async Task VersionsNeverUsedAreLetGo()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        IMigrator migrator = await BeforeTheMarkAsync(context);

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await ExecuteAsync(before, Version(1, "Retired", TwoKinds, Initial, TwoKindsScales, adopted: true, retired: true));
            await ExecuteAsync(before, Detected(1));

            for (int number = 2; number <= 6; number++)
            {
                await ExecuteAsync(before, Version(number, "Candidate", TwoKinds, BelowPauses, TwoKindsScales));
            }

            await ExecuteAsync(before, Version(7, "Active", InPods, BelowPauses, InPodsScales, adopted: true));
            await ExecuteAsync(before, Version(8, "Candidate", InPods, Initial, InPodsScales, evidence: Learned));
        }

        await migrator.MigrateAsync(TheMark, Cancel);

        await using (NpgsqlConnection after = await OpenAsync())
        {
            Assert.Equal("1:Retired:t 7:Active:t 8:Candidate:f", await StatesAsync(after));
        }

        await using (CarinaDbContext keeping = CarinaDbContextFactory.Create(Scratch()))
        {
            Assert.Null(await new SegmentModelVersionRepository(keeping).KeepBuiltInAsync(SegmentModel.BuiltIn, DateTime.UtcNow, Cancel));
        }

        await DropAsync();
    }

    [Theory(DisplayName = "BR-CS-003: a version made by hand never put in use stays while a detection, the chapters of an encode, a viewer's skip or a round of learning refers to it")]
    [InlineData("detection")]
    [InlineData("encode")]
    [InlineData("skip")]
    [InlineData("round")]
    public async Task AVersionReferredToStays(string reference)
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        IMigrator migrator = await BeforeTheMarkAsync(context);

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await ExecuteAsync(before, Version(1, "Active", TwoKinds, Initial, TwoKindsScales, adopted: true));
            await ExecuteAsync(before, Version(2, "Candidate", InPods, BelowPauses, InPodsScales));
            await ExecuteAsync(before, Version(3, "Candidate", InPods, BelowPauses, InPodsScales));
            await ExecuteAsync(before, reference switch
            {
                "detection" => Detected(2),
                "encode" => Encoded(2),
                "skip" => "INSERT INTO segment_skip_event (recording_id, happened_at, event, kind, from_at, to_at, model_version) VALUES (gen_random_uuid(), now(), 'Skipped', 'Break', interval '30 seconds', interval '60 seconds', 2)",
                _ => "INSERT INTO segment_learning_round (day, taken_at, candidate, outcome, in_use_version) VALUES ('2026-10-01', '2026-10-01T00:00:00Z', 2, 'Kept', 1)",
            });
        }

        await migrator.MigrateAsync(TheMark, Cancel);

        await using (NpgsqlConnection after = await OpenAsync())
        {
            Assert.Equal("1:Active:t 2:Candidate:t", await StatesAsync(after));
        }

        await DropAsync();
    }

    [Fact(DisplayName = "BR-CS-003: a version made by hand that was put in use stays though nothing refers to it, and going back takes the mark away and brings nothing back")]
    public async Task AVersionPutInUseStaysAndGoingBackTakesTheMarkAway()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        IMigrator migrator = await BeforeTheMarkAsync(context);

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await ExecuteAsync(before, Version(1, "Retired", TwoKinds, Initial, TwoKindsScales, adopted: true, retired: true));
            await ExecuteAsync(before, Version(2, "Candidate", TwoKinds, BelowPauses, TwoKindsScales));
            await ExecuteAsync(before, Version(3, "Active", InPods, BelowPauses, InPodsScales, adopted: true));
        }

        await migrator.MigrateAsync(TheMark, Cancel);
        await migrator.MigrateAsync(BeforeTheMark, Cancel);

        await using (NpgsqlConnection back = await OpenAsync())
        {
            Assert.Equal("1:Retired 3:Active", await ScalarAsync(back, "SELECT string_agg(number || ':' || state, ' ' ORDER BY number) FROM segment_model_version"));
            Assert.Equal("0", await ScalarAsync(back, "SELECT count(*) FROM information_schema.columns WHERE table_name = 'segment_model_version' AND column_name = 'built_in'"));
        }

        await DropAsync();
    }

    [Fact(DisplayName = "BR-CS-003: a database made from nothing keeps no version after every migration, so the built-in version of this release is the first")]
    public async Task ADatabaseMadeFromNothingKeepsNoVersion()
    {
        await using (CarinaDbContext context = CarinaDbContextFactory.Create(Scratch()))
        {
            await context.Database.EnsureDeletedAsync(Cancel);
            await context.GetService<IMigrator>().MigrateAsync(cancellationToken: Cancel);
        }

        await using (NpgsqlConnection made = await OpenAsync())
        {
            Assert.Equal("0", await ScalarAsync(made, "SELECT count(*) FROM segment_model_version"));
        }

        await DropAsync();
    }

    private static async Task<IMigrator> BeforeTheMarkAsync(CarinaDbContext context)
    {
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheMark, Cancel);

        return migrator;
    }

    private static async Task DropAsync()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
    }

    private static string Version(int number, string state, string weights, string thresholds, string scales, bool adopted = false, bool retired = false, string? evidence = null)
    {
        string kinds = weights.Contains("Opening", StringComparison.Ordinal) ? "4" : "2";
        string adoptedAt = adopted ? $"timestamptz '2026-10-01 00:00:00+00' + interval '{number} hours'" : "NULL";
        string retiredAt = retired ? $"timestamptz '2026-10-01 00:00:00+00' + interval '{number + 1} hours'" : "NULL";
        string evaluated = evidence is null ? "NULL" : $"'{evidence}'";

        return $"""
                INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at, adopted_at, retired_at, evidence)
                VALUES ({number}, '{state}', false, {kinds}, '{weights}', '{NoOffsets}', '{NoOffsets}', '{thresholds}', '{scales}', timestamptz '2026-10-01 00:00:00+00', {adoptedAt}, {retiredAt}, {evaluated})
                """;
    }

    private static string Detected(int version)
        => $"INSERT INTO segment_detection (recording_id, model_version, state, segments, cm_share, snaps, extraction_version, catalog_until, detected_at) VALUES (gen_random_uuid(), {version}, 'NothingFound', '[{{\"startsAtSec\": 0, \"endsAtSec\": 1800, \"kind\": \"Programme\", \"probability\": 0.9}}]', 0, '[]', 2, now(), now())";

    private static string Encoded(int version)
        => $"""
            INSERT INTO encode_profile (id, label, codec, resolution, deinterlace, rate_factor, quantiser, defined_at)
            VALUES ('{Profile}', 'Viewing', 'H264', 'AsSource', 'Leave', 23, 24, timestamptz '2026-10-01 00:00:00+00');
            INSERT INTO encode_destination (id, label, output_root, default_profile_id, defined_at)
            VALUES ('{Destination}', 'Shelf', 'encodes', '{Profile}', timestamptz '2026-10-01 00:00:00+00');
            INSERT INTO encode_job (
                id, recording_id, profile_id, destination_id, output_root, status, attempt,
                queued_at, started_at, ended_at, artefact_name,
                chapters_detector, chapters_verdict, chapters_break_share, chapters_decided_at, chapters_model_version)
            VALUES (
                gen_random_uuid(), '{Recording}', '{Profile}', '{Destination}', 'encodes', 'Completed', 1,
                timestamptz '2026-10-01 03:00:00+00', timestamptz '2026-10-01 03:00:05+00', timestamptz '2026-10-01 03:30:00+00',
                '{Recording.Replace("-", string.Empty, StringComparison.Ordinal)}.{Profile.Replace("-", string.Empty, StringComparison.Ordinal)}.mp4',
                'Segments', 'Marked', 0.2, timestamptz '2026-10-01 05:00:00+00', {version});
            """;

    private static Task<string> StatesAsync(NpgsqlConnection connection)
        => ScalarAsync(connection, "SELECT string_agg(number || ':' || state || ':' || built_in::text::char, ' ' ORDER BY number) FROM segment_model_version");

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
