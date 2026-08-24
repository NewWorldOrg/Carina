using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string Now = "timestamptz '2026-08-24 12:00:00+00'";

    private const string Counted = "timestamptz '2026-08-24 20:30:00+00'";

    private const string OneFault = """
        '[{"fault":"DriverLost","tuneFailure":null,"note":"","noticedAt":"2026-08-24T12:00:00Z"}]'::jsonb
        """;

    [Fact]
    public async Task ACompleteNobodyAskedForIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 80001, outcome: "'Complete'", size: "3400000000", observedAt: Ends, stoppedAt: Ends));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_complete_was_asked_for", refusal.ConstraintName);

        await Record(
            connection,
            80001,
            outcome: "'Complete'",
            size: "3400000000",
            observedAt: Ends,
            stoppedAt: Ends,
            abortedAt: Ends);
    }

    [Fact]
    public async Task AnEmptyFileIsAFailureWhateverElseWasObserved()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        foreach (string wishful in new[] { "'Complete'", "'Truncated'" })
        {
            PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
                () => Record(
                    connection,
                    80002,
                    outcome: wishful,
                    size: "0",
                    observedAt: Ends,
                    stoppedAt: Ends,
                    abortedAt: Ends,
                    detail: OneFault));

            Assert.Equal("ck_recording_empty_file_failed", refusal.ConstraintName);
        }

        await Record(
            connection,
            80002,
            outcome: "'Failed'",
            size: "0",
            observedAt: Ends,
            stoppedAt: Ends,
            detail: OneFault);
    }

    [Fact]
    public async Task AnEndingThatIsNotACompleteSaysWhyInClasses()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 80003, outcome: "'Truncated'", size: "12", observedAt: Ends, stoppedAt: Ends));

        Assert.Equal("ck_recording_outcome_detail", refusal.ConstraintName);

        Guid id = await Record(
            connection,
            80003,
            outcome: "'Truncated'",
            size: "12",
            observedAt: Ends,
            stoppedAt: Ends,
            detail: OneFault);

        Assert.Equal(
            "DriverLost",
            await Scalar(connection, $"SELECT outcome_detail -> 0 ->> 'fault' FROM recording WHERE id = '{id}'"));
    }

    [Fact]
    public async Task TheReasonIsAListRatherThanOneSentence()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            "jsonb",
            await Scalar(
                connection,
                """
                SELECT data_type FROM information_schema.columns
                WHERE table_name = 'recording' AND column_name = 'outcome_detail'
                """));
    }

    [Fact]
    public async Task CountersNobodyTookCarryNoNumberAtAll()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException zeroed = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 80004, ccMeasured: "false", ccDropped: "0", ccTotal: "0"));

        Assert.Equal("ck_recording_measurement", zeroed.ConstraintName);

        PostgresException halfCounted = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 80004, ccMeasured: "true", ccDropped: "0", measuredAt: Counted));

        Assert.Equal("ck_recording_measurement", halfCounted.ConstraintName);

        PostgresException undated = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 80004, ccMeasured: "true", ccDropped: "0", ccTotal: "1000"));

        Assert.Equal("ck_recording_measurement", undated.ConstraintName);

        PostgresException impossible = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 80004, ccMeasured: "true", ccDropped: "11", ccTotal: "10", measuredAt: Counted));

        Assert.Equal("ck_recording_measurement", impossible.ConstraintName);

        Guid counted = await Record(
            connection,
            80004,
            ccMeasured: "true",
            ccDropped: "0",
            ccTotal: "1000",
            measuredAt: Counted);
        Guid uncounted = await Record(connection, 80004, eventId: 4002);

        Assert.Equal(0L, await Scalar(connection, $"SELECT cc_dropped_packets FROM recording WHERE id = '{counted}'"));
        Assert.Null(await Scalar(connection, $"SELECT cc_dropped_packets FROM recording WHERE id = '{uncounted}'"));
    }

    [Fact]
    public async Task TheDropIndexCannotReachARecordingNobodyMeasured()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        string definition = await IndexDefinition(connection, "ix_recording_cc_dropped");

        Assert.Contains("cc_dropped_packets", definition, StringComparison.Ordinal);
        Assert.Contains("cc_measured", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheInFlightIndexNarrowsToWhatHasNoOutcomeYet()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Contains(
            "recording_outcome IS NULL",
            await IndexDefinition(connection, "ix_recording_in_flight"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSettledIndexReadsTheOutcomeAndWhenItStopped()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        string definition = await IndexDefinition(connection, "ix_recording_settled");

        Assert.Contains("recording_outcome", definition, StringComparison.Ordinal);
        Assert.Contains("stopped_at_actual", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLedgerHoldsNoForeignKeyAtAll()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            0L,
            await Scalar(
                connection,
                """
                SELECT count(*)
                FROM pg_constraint AS c
                JOIN pg_class AS child ON child.oid = c.conrelid
                WHERE c.contype = 'f' AND child.relname = 'recording'
                """));
    }

    [Fact]
    public async Task ARecordingOutlivesTheGuideAndTheChannelsItWasMadeFrom()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid id = await Record(connection, 80005);

        await Execute(connection, "TRUNCATE broadcast_service, programme CASCADE");

        Assert.Equal(
            "A programme",
            await Scalar(connection, $"SELECT snapshot_name FROM recording WHERE id = '{id}'"));
    }

    [Fact]
    public async Task AnOutcomeWithoutAStopOrASizeIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException unstopped = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 80006, outcome: "'Failed'", size: "0", observedAt: Ends, detail: OneFault));

        Assert.Equal("ck_recording_outcome", unstopped.ConstraintName);

        PostgresException unweighed = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 80006, outcome: "'Failed'", stoppedAt: Ends, detail: OneFault));

        Assert.Equal("ck_recording_outcome", unweighed.ConstraintName);

        PostgresException borrowed = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                80006,
                outcome: "'Recording'",
                size: "12",
                observedAt: Ends,
                stoppedAt: Ends,
                detail: OneFault));

        Assert.Equal("ck_recording_outcome", borrowed.ConstraintName);
    }

    [Fact]
    public async Task ASizeReadOffTheDiskSaysWhenItWasRead()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 80007, size: "12"));

        Assert.Equal("ck_recording_observation", refusal.ConstraintName);
    }

    [Fact]
    public async Task AWindowEndsAfterItStarts()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 80008, windowEnd: Airs));

        Assert.Equal("ck_recording_window", refusal.ConstraintName);
    }

    [Fact]
    public async Task NothingCountsBackwards()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        foreach (string backwards in new[] { "written_duration_ms", "resume_count", "eovf_count" })
        {
            Guid id = await Record(connection, 80009, eventId: 4001 + backwards.Length);

            PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
                () => Execute(connection, $"UPDATE recording SET {backwards} = -1 WHERE id = '{id}'"));

            Assert.Equal("ck_recording_counts", refusal.ConstraintName);
        }
    }

    [Fact]
    public async Task WhatWasWrittenIsAddedToRatherThanReplaced()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid id = await Record(connection, 80010);

        await Execute(connection, $"UPDATE recording SET written_duration_ms = written_duration_ms + 600000 WHERE id = '{id}'");
        await Execute(connection, $"UPDATE recording SET written_duration_ms = written_duration_ms + 720000 WHERE id = '{id}'");

        Assert.Equal(1_320_000L, await Scalar(connection, $"SELECT written_duration_ms FROM recording WHERE id = '{id}'"));
    }

    [Fact]
    public async Task EveryIndexOnTheLedgerIsOneTheModelDeclares()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT indexes.indexname
            FROM pg_indexes AS indexes
            JOIN pg_class AS relation ON relation.relname = indexes.indexname
            LEFT JOIN pg_constraint AS backing ON backing.conindid = relation.oid
            WHERE indexes.schemaname = 'public'
              AND indexes.tablename = 'recording'
              AND backing.oid IS NULL
            ORDER BY indexes.indexname
            """,
            connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        var built = new List<string>();
        while (await reader.ReadAsync())
        {
            built.Add(reader.GetString(0));
        }

        await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);
        IReadOnlyList<string> declared = [.. context.Model
            .GetEntityTypes()
            .Single(entityType => entityType.ClrType == typeof(Recording))
            .GetIndexes()
            .Select(index => index.GetDatabaseName())
            .OfType<string>()
            .Order(StringComparer.Ordinal)];

        Assert.Equal(declared, built);
        Assert.Contains("ix_recording_cc_dropped", declared, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ACountThatCameOffNoTunerIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException counted = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                80011,
                ccMeasured: "true",
                ccDropped: "0",
                ccTotal: "1000",
                measuredAt: Counted,
                tuner: "NULL"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, counted.SqlState);
        Assert.Equal("ck_recording_tuner", counted.ConstraintName);
    }

    [Fact]
    public async Task AnOverflowThatCameOffNoTunerIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid id = await Record(connection, 80012, tuner: "NULL");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Execute(connection, $"UPDATE recording SET eovf_count = 3 WHERE id = '{id}'"));

        Assert.Equal("ck_recording_tuner", refusal.ConstraintName);
    }

    [Fact]
    public async Task ARecordingThatCountedSomethingCanBeTracedBackToTheTunerThatWroteIt()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Record(
            connection,
            80013,
            ccMeasured: "true",
            ccDropped: "4",
            ccTotal: "1000",
            measuredAt: Counted,
            tuner: "'pt3-2'");

        Assert.Equal(
            "pt3-2",
            await Scalar(connection, "SELECT tuner_device_id FROM recording WHERE network_id = 80013"));
    }

    [Fact]
    public async Task ARecordingThatCountedNothingNeedNotNameATuner()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, 80014, tuner: "NULL");

        Assert.Null(await Scalar(connection, "SELECT tuner_device_id FROM recording WHERE network_id = 80014"));
    }

    [Theory]
    [InlineData("stopped_at_actual", 80021)]
    [InlineData("aborted_at", 80022)]
    [InlineData("observed_at", 80023)]
    [InlineData("measured_updated_at", 80024)]
    public async Task NothingAboutARecordingHappensBeforeItStarted(string column, int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid id = await Record(connection, networkId);

        string also = column is "observed_at" ? ", file_size_observed = 12" : string.Empty;

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Execute(
                connection,
                $"UPDATE recording SET {column} = {Airs} - interval '1 second'{also} WHERE id = '{id}'"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_runs_forwards", refusal.ConstraintName);
    }

    [Theory]
    [InlineData("Pending", 80031)]
    [InlineData("Ready", 80032)]
    [InlineData("Failed", 80033)]
    [InlineData("Skipped", 80034)]
    public async Task TheLedgerHoldsTheFourThumbnailStates(string state, int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, networkId, thumbnail: $"'{state}'");

        Assert.Equal(
            state,
            await Scalar(connection, $"SELECT thumbnail_state FROM recording WHERE network_id = {networkId}"));
    }

    [Fact]
    public async Task AThumbnailStateTheLedgerDoesNotHoldIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 80035, thumbnail: "'Generating'"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_thumbnail", refusal.ConstraintName);
    }

    [Fact]
    public async Task ARecordingThatFailedGetsNoPicture()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                80036,
                outcome: "'Failed'",
                size: "0",
                observedAt: Ends,
                stoppedAt: Ends,
                detail: OneFault,
                thumbnail: "'Ready'"));

        Assert.Equal("ck_recording_thumbnail", refusal.ConstraintName);

        await Record(
            connection,
            80036,
            outcome: "'Failed'",
            size: "0",
            observedAt: Ends,
            stoppedAt: Ends,
            detail: OneFault,
            thumbnail: "'Skipped'");
    }

    [Fact]
    public async Task ATruncatedRecordingMayStillHaveAPicture()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(
            connection,
            80037,
            outcome: "'Truncated'",
            size: "1200000",
            observedAt: Ends,
            stoppedAt: Ends,
            detail: OneFault,
            thumbnail: "'Ready'");

        Assert.Equal(
            "Ready",
            await Scalar(connection, "SELECT thumbnail_state FROM recording WHERE network_id = 80037"));
    }

    private static async Task<string> IndexDefinition(NpgsqlConnection connection, string name)
        => (string)(await Scalar(connection, $"SELECT indexdef FROM pg_indexes WHERE indexname = '{name}'"))!;

    private static async Task<Guid> Record(
        NpgsqlConnection connection,
        int networkId,
        int eventId = 4001,
        string? fileName = null,
        string? outcome = null,
        string? size = null,
        string? observedAt = null,
        string? stoppedAt = null,
        string? abortedAt = null,
        string? detail = null,
        string ccMeasured = "false",
        string? ccDropped = null,
        string? ccTotal = null,
        string? measuredAt = null,
        string? windowEnd = null,
        Guid? reservationId = null,
        string? tuner = null,
        string? thumbnail = null)
    {
        var id = Guid.NewGuid();

        await Execute(
            connection,
            $"""
            INSERT INTO recording (
                id, reservation_id, network_id, service_id, event_id, programme_start_at,
                output_root, file_name, file_size_observed, observed_at,
                started_at_actual, stopped_at_actual, aborted_at,
                written_duration_ms, resume_count, interruptions,
                expected_window_start, expected_window_end,
                recording_outcome, outcome_detail,
                scrambled_packets, eovf_count, measured_updated_at,
                snapshot_name, snapshot_summary, snapshot_extended, snapshot_genres, captured_at,
                broadcast_group_key, broadcast_group_role,
                cc_measured, cc_dropped_packets, cc_total_packets,
                pcr_anchor, drop_positions, pcr_reanchors, tuner_device_id, thumbnail_state)
            VALUES (
                '{id}', {(reservationId is { } held ? $"'{held}'" : "NULL")}, {networkId}, 1024, {eventId}, {Airs},
                'bulk', '{fileName ?? $"{id:N}.m2ts"}', {size ?? "NULL"}, {observedAt ?? "NULL"},
                {Airs}, {stoppedAt ?? "NULL"}, {abortedAt ?? "NULL"},
                0, 0, '[]'::jsonb,
                {Airs}, {windowEnd ?? Ends},
                {outcome ?? "NULL"}, {detail ?? "'[]'::jsonb"},
                NULL, 0, {measuredAt ?? "NULL"},
                'A programme', 'What it is about', '', '[]'::jsonb, {Now},
                NULL, 'Standalone',
                {ccMeasured}, {ccDropped ?? "NULL"}, {ccTotal ?? "NULL"},
                NULL, '[]'::jsonb, '[]'::jsonb, {tuner ?? "'pt3-0'"}, {thumbnail ?? "'Pending'"})
            """);

        return id;
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        object? read = await command.ExecuteScalarAsync();

        return read is DBNull ? null : read;
    }
}
