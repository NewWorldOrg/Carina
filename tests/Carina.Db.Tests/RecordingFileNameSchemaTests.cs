using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingFileNameSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string Now = "timestamptz '2026-08-24 12:00:00+00'";

    [Theory]
    [InlineData("../escaped.m2ts", 81011)]
    [InlineData("held..out.m2ts", 81012)]
    [InlineData("a/b.m2ts", 81013)]
    [InlineData("/absolute.m2ts", 81014)]
    [InlineData(".", 81015)]
    [InlineData("", 81016)]
    [InlineData("   ", 81017)]
    [InlineData(" leading.m2ts", 81018)]
    [InlineData("trailing.m2ts ", 81019)]
    public async Task TheDatabaseRefusesAFileNameThatCanLeaveItsRoom(string name, int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, name, networkId));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_file_name", refusal.ConstraintName);
        Assert.Equal(0L, await Count(connection, networkId));
    }

    [Fact]
    public async Task TheDatabaseRefusesABackslashTheSameWay()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, "a" + '\\' + "b.m2ts", 81002));

        Assert.Equal("ck_recording_file_name", refusal.ConstraintName);
        Assert.Equal(0L, await Count(connection, 81002));
    }

    [Fact]
    public async Task TheColumnItselfCannotHoldANulByte()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => Record(connection, "recording\0.m2ts", 81003));

        Assert.Equal(0L, await Count(connection, 81003));
    }

    [Fact]
    public async Task ASingleNameIsAccepted()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, "6f9619ff8b86d011b42d00c04fc964ff.m2ts", 81004);

        Assert.Equal(1L, await Count(connection, 81004));
    }

    [Fact]
    public async Task ThereIsNoColumnThatWouldCarryASeparatorInstead()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            0L,
            await Scalar(
                connection,
                """
                SELECT count(*) FROM information_schema.columns
                WHERE table_name = 'recording' AND column_name IN ('relative_path', 'path', 'directory')
                """));
    }

    private static async Task Record(NpgsqlConnection connection, string fileName, int networkId)
    {
        await using var command = new NpgsqlCommand(
            """
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
                pcr_anchor, drop_positions, pcr_reanchors)
            VALUES (
                gen_random_uuid(), NULL, @networkId, 1024, 4001, @airs,
                'bulk', @fileName, NULL, NULL,
                @airs, NULL, NULL,
                0, 0, '[]'::jsonb,
                @airs, @ends,
                NULL, '[]'::jsonb,
                NULL, 0, NULL,
                'A programme', 'What it is about', '', '[]'::jsonb, @now,
                NULL, 'Standalone',
                false, NULL, NULL,
                NULL, '[]'::jsonb, '[]'::jsonb)
            """.Replace("@airs", Airs, StringComparison.Ordinal)
               .Replace("@ends", Ends, StringComparison.Ordinal)
               .Replace("@now", Now, StringComparison.Ordinal),
            connection);
        command.Parameters.AddWithValue("fileName", fileName);
        command.Parameters.AddWithValue("networkId", networkId);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> Count(NpgsqlConnection connection, int networkId)
        => (long)(await Scalar(connection, $"SELECT count(*) FROM recording WHERE network_id = {networkId}"))!;

    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        object? read = await command.ExecuteScalarAsync();

        return read is DBNull ? null : read;
    }
}
