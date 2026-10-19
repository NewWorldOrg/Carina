using System.Globalization;

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

    private const string TheVersionWasMade = "20261018224946_AVersionMadeByHandTellsOpeningsAndEndingsApart";

    private const string Learned = """{"confirmedRecordings": 12, "boundaries": 148, "labelsUntil": null}""";

    private const string LearningOn = "INSERT INTO segment_settings (id, learning, learning_changed_at) VALUES (1, true, now())";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CD-007: while learning is on the first version of four kinds takes the place of the first version in use after the candidates kept, and going back puts the first version in use again and keeps what viewers did")]
    public async Task TheFirstVersionOfFourKindsTakesThePlaceOfTheFirst()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheVersionWasMade, Cancel);

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await ExecuteAsync(before, LearningOn);
            await ExecuteAsync(before, Version(2, "'Candidate'", "NULL", "NULL", $"'{Learned}'"));
        }

        await migrator.MigrateAsync(TheVersionWasMade, Cancel);

        await using (NpgsqlConnection after = await OpenAsync())
        {
            Assert.Equal("1:Retired 2:Candidate 3:Active", await StatesAsync(after));
            Assert.Equal("4 false NULL", await ScalarAsync(after, "SELECT concat_ws(' ', kinds, by_hand::text, coalesce(evidence::text, 'NULL')) FROM segment_model_version WHERE number = 3"));
            await ExecuteAsync(after, "INSERT INTO segment_detection (recording_id, model_version, state, segments, cm_share, snaps, extraction_version, catalog_until, detected_at) VALUES (gen_random_uuid(), 3, 'NothingFound', '[{\"startsAtSec\": 0, \"endsAtSec\": 1800, \"kind\": \"Programme\", \"probability\": 0.9}]', 0, '[]', 2, now(), now())");
            await ExecuteAsync(after, "INSERT INTO segment_skip_event (recording_id, happened_at, event, kind, from_at, to_at, model_version) VALUES (gen_random_uuid(), now(), 'Skipped', 'Opening', interval '30 seconds', interval '120 seconds', 3)");
        }

        await migrator.MigrateAsync(BeforeTheVersionWasMade, Cancel);

        await using (NpgsqlConnection back = await OpenAsync())
        {
            Assert.Equal("1:Active 2:Candidate", await StatesAsync(back));
            Assert.Equal("NULL", await ScalarAsync(back, "SELECT coalesce(retired_at::text, 'NULL') FROM segment_model_version WHERE number = 1"));
            Assert.Equal("0", await ScalarAsync(back, "SELECT count(*) FROM segment_detection"));
            Assert.Equal("1 NULL", await ScalarAsync(back, "SELECT concat_ws(' ', count(*), coalesce(max(model_version)::text, 'NULL')) FROM segment_skip_event"));
        }

        await context.Database.EnsureDeletedAsync(Cancel);
    }

    [Theory(DisplayName = "BR-CD-013: while learning is off, the first version stays in use and the first version of four kinds waits as a candidate, whether the settings were ever written or not")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhileLearningIsOffTheFirstVersionStays(bool written)
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheVersionWasMade, Cancel);

        if (written)
        {
            await using NpgsqlConnection before = await OpenAsync();
            await ExecuteAsync(before, "INSERT INTO segment_settings (id, learning, learning_changed_at) VALUES (1, false, now())");
        }

        await migrator.MigrateAsync(TheVersionWasMade, Cancel);

        await using (NpgsqlConnection after = await OpenAsync())
        {
            Assert.Equal("1:Active 2:Candidate", await StatesAsync(after));
            Assert.Equal("4 NULL", await ScalarAsync(after, "SELECT concat_ws(' ', kinds, coalesce(adopted_at::text, 'NULL')) FROM segment_model_version WHERE number = 2"));
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
            await ExecuteAsync(before, LearningOn);
            await ExecuteAsync(before, "UPDATE segment_model_version SET state = 'Retired', retired_at = now() WHERE number = 1");
            await ExecuteAsync(before, Version(2, "'Active'", "now()", byHand ? "now()" : "NULL", byHand ? "NULL" : $"'{Learned}'"));
        }

        await migrator.MigrateAsync(TheVersionWasMade, Cancel);

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

    [Fact(DisplayName = "BR-CD-008: the version of four kinds is numbered under the lock a round of learning keeps its candidate under, so a candidate kept the same night takes the number before it")]
    public async Task TheVersionIsNumberedUnderTheLockOfTheRound()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheVersionWasMade, Cancel);
        Task migrated;

        await using (NpgsqlConnection settings = await OpenAsync())
        {
            await ExecuteAsync(settings, LearningOn);
        }

        await using (NpgsqlConnection round = await OpenAsync())
        {
            await using NpgsqlTransaction keeping = await round.BeginTransactionAsync(Cancel);
            await ExecuteAsync(round, $"SELECT pg_advisory_xact_lock({SegmentModelVersionRepository.WriterLock})");
            await ExecuteAsync(round, Version(2, "'Candidate'", "NULL", "NULL", $"'{Learned}'"));

            migrated = migrator.MigrateAsync(TheVersionWasMade, Cancel);
            await WaitingAsync();

            Assert.False(migrated.IsCompleted);
            await keeping.CommitAsync(Cancel);
        }

        await migrated;

        await using (NpgsqlConnection after = await OpenAsync())
        {
            Assert.Equal("1:Retired 2:Candidate 3:Active", await StatesAsync(after));
        }

        await context.Database.EnsureDeletedAsync(Cancel);
    }

    /// <summary>
    /// Waits until a session waits for an advisory lock another holds.
    /// </summary>
    private static async Task WaitingAsync()
    {
        await using NpgsqlConnection watching = await OpenAsync();

        for (int look = 0; look < 200; look++)
        {
            if (await ScalarAsync(watching, "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted") is not "0")
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), Cancel);
        }

        throw new TimeoutException("The migration never waited for the lock the round holds.");
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
