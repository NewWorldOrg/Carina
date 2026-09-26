using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class QualityTablesDroppedWholeTests(MigratedScratchDatabase database) : IClassFixture<MigratedScratchDatabase>
{
    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string Now = "timestamptz '2026-08-24 12:00:00+00'";

    private const string Taken = "timestamptz '2026-08-24 20:30:00+00'";

    private const string TheQualityTables = "quality\\_%";

    [Fact(DisplayName = "dropping every quality table leaves every other ledger where it was")]
    public async Task DroppingEveryQualityTableLeavesEveryOtherLedgerWhereItWas()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Guid recorded = await RecordAsync(connection);
        await SampleAsync(connection);
        await IncidentAsync(connection);

        IReadOnlyList<string> quality = await QualityTablesAsync(connection);
        IReadOnlyList<string> everythingElse = await EveryOtherTableAsync(connection);

        Assert.NotEmpty(quality);
        Assert.NotEmpty(everythingElse);

        await ExecuteAsync(connection, $"DROP TABLE {string.Join(", ", quality)} RESTRICT");

        Assert.Empty(await QualityTablesAsync(connection));
        Assert.Equal(everythingElse, await EveryOtherTableAsync(connection));
        Assert.Equal(1L, await ScalarAsync(connection, $"SELECT count(*) FROM recording WHERE id = '{recorded}'"));

        await RecordAsync(connection, eventId: 4_002);
    }

    private static Task<IReadOnlyList<string>> QualityTablesAsync(NpgsqlConnection connection)
        => TablesAsync(
            connection,
            $"""
            SELECT tablename FROM pg_tables
            WHERE schemaname = 'public' AND tablename LIKE '{TheQualityTables}'
            ORDER BY tablename
            """);

    private static Task<IReadOnlyList<string>> EveryOtherTableAsync(NpgsqlConnection connection)
        => TablesAsync(
            connection,
            $"""
            SELECT tablename FROM pg_tables
            WHERE schemaname = 'public' AND tablename NOT LIKE '{TheQualityTables}'
            ORDER BY tablename
            """);

    private static async Task<IReadOnlyList<string>> TablesAsync(NpgsqlConnection connection, string sql)
    {
        await using var asking = new NpgsqlCommand(sql, connection);

        List<string> named = [];
        await using NpgsqlDataReader reading = await asking.ExecuteReaderAsync();

        while (await reading.ReadAsync())
        {
            named.Add(reading.GetString(0));
        }

        return named;
    }

    private static async Task<Guid> RecordAsync(NpgsqlConnection connection, int eventId = 4_001)
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            connection,
            $"""
            INSERT INTO recording (
                id, reservation_id, network_id, service_id, event_id, programme_start_at,
                output_root, file_name, file_size_observed, observed_at,
                started_at_actual, stopped_at_actual, aborted_at,
                written_duration_ms, resume_count, interruptions,
                expected_window_start, expected_window_end, promised_window_end,
                recording_outcome, outcome_detail,
                scrambled_packets, eovf_count, measured_updated_at,
                snapshot_name, snapshot_summary, snapshot_extended, snapshot_genres,
                snapshot_audio, snapshot_sounds, captured_at,
                broadcast_group_key, broadcast_group_role,
                cc_measured, cc_dropped_packets, cc_total_packets,
                pcr_anchor, drop_positions, pcr_reanchors, tuner_device_id, thumbnail_state, thumbnail_fault)
            VALUES (
                '{id}', NULL, 32736, 1024, {eventId}, {Airs},
                'bulk', '{id:N}.m2ts', NULL, NULL,
                {Airs}, NULL, NULL,
                0, 0, '[]'::jsonb,
                {Airs}, {Ends}, {Ends},
                NULL, '[]'::jsonb,
                NULL, 0, NULL,
                'A programme', 'What it is about', '', '[]'::jsonb, 'Undetermined', 0, {Now},
                NULL, 'Standalone',
                false, NULL, NULL,
                NULL, '[]'::jsonb, '[]'::jsonb, 'pt3-0', 'Pending', NULL)
            """);

        return id;
    }

    private static Task SampleAsync(NpgsqlConnection connection)
        => ExecuteAsync(
            connection,
            $"""
            INSERT INTO quality_signal_sample (
                driver_instance_id, session_id, taken_at, purpose, tuner_device_id, network_id, service_id,
                locked, lock_read_at, cnr_milli_decibels, cnr_read_at, bit_errors, bit_errors_read_at,
                metrics_not_read, not_taken_because)
            VALUES (
                'driver-7', '{Guid.NewGuid():N}', {Taken}, 'Survey', 'adapter0', 32736, 1024,
                true, {Taken}, 29000, {Taken}, '[]'::jsonb, NULL,
                '[]'::jsonb, NULL)
            """);

    private static Task IncidentAsync(NpgsqlConnection connection)
        => ExecuteAsync(
            connection,
            $"""
            INSERT INTO quality_incident (
                id, detected_at, breached, subject_kind, subject_key, observed, owner, classification, silence,
                applied_default, applied_current, applied_provisional, applied_observations, applied_updated_at,
                state, notified_at, resolved_at)
            VALUES (
                '{Guid.NewGuid()}', {Taken}, 'PacketsLostWarning', 'Recording', '{Guid.NewGuid():N}', 0.004,
                'Quality', NULL, NULL, 0.0002, 0.0002, true, 0, {Taken},
                'Detected', NULL, NULL)
            """);

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var running = new NpgsqlCommand(sql, connection);
        await running.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var asking = new NpgsqlCommand(sql, connection);

        return await asking.ExecuteScalarAsync();
    }
}
