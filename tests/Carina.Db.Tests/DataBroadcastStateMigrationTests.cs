using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class DataBroadcastStateMigrationTests
{
    private const string ScratchDatabase = "carina_data_broadcast_state_test";

    private const string BeforeTheDataBroadcastWasKept = "20261007091018_TheLearningDataKeepsWhetherCaptionsAreShown";

    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string OneFault = """
        '[{"fault":"DriverLost","tuneFailure":null,"note":"","noticedAt":"2026-08-24T20:10:00Z"}]'::jsonb
        """;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-BS-001: a recording that had ended before the record of its data broadcast was kept has it coming, and one still being written has none due")]
    public async Task ARecordingThatHadEndedHasItsRecordComing()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheDataBroadcastWasKept, Cancel);

        Guid ended = Guid.NewGuid();
        Guid writing = Guid.NewGuid();

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await RecordAsync(before, ended, true);
            await RecordAsync(before, writing, false);
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection after = await OpenAsync();

        Assert.Equal("Coming|0|true|true", await StandingAsync(after, ended));
        Assert.Equal("None|0|true|true", await StandingAsync(after, writing));
        Assert.Equal(
            1L,
            await ScalarAsync(after, "SELECT count(*) FROM pg_constraint WHERE conname = 'ck_recording_data_broadcast'"));
    }

    private static async Task<object?> StandingAsync(NpgsqlConnection connection, Guid id)
        => await ScalarAsync(
            connection,
            $"SELECT data_broadcast_state || '|' || data_broadcast_attempts || '|' || (data_broadcast_made_at IS NULL) || '|' || (data_broadcast_modules IS NULL) FROM recording WHERE id = '{id}'");

    private static async Task RecordAsync(NpgsqlConnection connection, Guid id, bool ended)
    {
        string outcome = ended ? "'Failed'" : "NULL";
        string size = ended ? "0" : "NULL";
        string at = ended ? Ends : "NULL";
        string detail = ended ? OneFault : "'[]'::jsonb";

        await using NpgsqlCommand writing = new(
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
                pcr_anchor, drop_positions, pcr_reanchors, tuner_device_id, thumbnail_state)
            VALUES (
                '{id}', NULL, 32737, 1024, 4001, {Airs},
                'bulk', '{id:N}.m2ts', {size}, {at},
                {Airs}, {at}, NULL,
                0, 0, '[]'::jsonb,
                {Airs}, {Ends}, {Ends},
                {outcome}, {detail},
                NULL, 0, NULL,
                'A programme', 'What it is about', '', '[]'::jsonb, 'Undetermined', 0, {Airs},
                NULL, 'Standalone',
                false, NULL, NULL,
                NULL, '[]'::jsonb, '[]'::jsonb, 'pt3-0', 'Pending')
            """,
            connection);

        await writing.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand reading = new(sql, connection);
        object? read = await reading.ExecuteScalarAsync(Cancel);

        return read is DBNull ? null : read;
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        NpgsqlConnection connection = new(Scratch());
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
