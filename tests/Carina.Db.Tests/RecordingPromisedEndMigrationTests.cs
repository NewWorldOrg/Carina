using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingPromisedEndMigrationTests
{
    private const string ScratchDatabase = "carina_recording_promised_end_test";

    private const string BeforeTheEndWasPromised = "20260914135852_WhenASupplyGoesQuiet";

    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string FollowedTo = "timestamptz '2026-08-24 21:25:00+00'";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ARecordingWrittenBeforeTheEndWasPromisedIsPromisedTheEndItAlreadyHad()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheEndWasPromised, Cancel);

        Guid unfollowed = Guid.NewGuid();
        Guid followed = Guid.NewGuid();

        await using (NpgsqlConnection writing = await OpenAsync())
        {
            await RecordAsync(writing, unfollowed, Ends);
            await RecordAsync(writing, followed, FollowedTo);
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection reading = await OpenAsync();

        Assert.Equal(
            2L,
            await CountAsync(
                reading,
                $"SELECT count(*) FROM recording WHERE id IN ('{unfollowed}', '{followed}') AND promised_window_end = expected_window_end"));
        Assert.Equal(
            1L,
            await CountAsync(reading, $"SELECT count(*) FROM recording WHERE id = '{followed}' AND promised_window_end = {FollowedTo}"));
        Assert.Equal(
            1L,
            await CountAsync(
                reading,
                "SELECT count(*) FROM information_schema.columns WHERE table_name = 'recording' AND column_name = 'promised_window_end' AND is_nullable = 'NO'"));
        Assert.Equal(
            1L,
            await CountAsync(reading, "SELECT count(*) FROM pg_constraint WHERE conname = 'ck_recording_window_promised'"));
    }

    private static async Task RecordAsync(NpgsqlConnection connection, Guid id, string windowEnd)
    {
        await using var writing = new NpgsqlCommand(
            $"""
            INSERT INTO recording (
                id, reservation_id, network_id, service_id, event_id, programme_start_at,
                output_root, file_name, file_size_observed, observed_at,
                started_at_actual, stopped_at_actual, aborted_at,
                written_duration_ms, resume_count, interruptions,
                expected_window_start, expected_window_end,
                recording_outcome, outcome_detail,
                scrambled_packets, eovf_count, measured_updated_at,
                snapshot_name, snapshot_summary, snapshot_extended, snapshot_genres,
                snapshot_audio, snapshot_sounds, captured_at,
                broadcast_group_key, broadcast_group_role,
                cc_measured, cc_dropped_packets, cc_total_packets,
                pcr_anchor, drop_positions, pcr_reanchors, tuner_device_id, thumbnail_state)
            VALUES (
                '{id}', NULL, 32736, 1024, 4001, {Airs},
                'bulk', '{id:N}.m2ts', NULL, NULL,
                {Airs}, NULL, NULL,
                0, 0, '[]'::jsonb,
                {Airs}, {windowEnd},
                NULL, '[]'::jsonb,
                NULL, 0, NULL,
                'A programme', 'What it is about', '', '[]'::jsonb, 'Undetermined', 0, {Airs},
                NULL, 'Standalone',
                false, NULL, NULL,
                NULL, '[]'::jsonb, '[]'::jsonb, 'pt3-0', 'Pending')
            """,
            connection);

        await writing.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var reading = new NpgsqlCommand(sql, connection);

        return (long)(await reading.ExecuteScalarAsync(Cancel))!;
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(Scratch());
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
