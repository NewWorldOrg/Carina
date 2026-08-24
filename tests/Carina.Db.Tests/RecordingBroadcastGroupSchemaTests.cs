using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingBroadcastGroupSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string Now = "timestamptz '2026-08-24 12:00:00+00'";

    [Theory]
    [InlineData("Standalone", 82011)]
    [InlineData("MovementPrimary", 82012)]
    [InlineData("MovementSuppressed", 82013)]
    [InlineData("RelaySegment", 82014)]
    public async Task ARecordingHoldsTheRolesThisDomainKnows(string role, int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, networkId, groupKey: "'32736-1024-4001'", groupRole: role);

        Assert.Equal(role, await Scalar(connection, Reads(networkId, "broadcast_group_role")));
    }

    [Fact]
    public async Task ARoleBorrowedFromSomewhereElseIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 82021, groupKey: "'32736-1024-4001'", groupRole: "Primary"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_broadcast_group", refusal.ConstraintName);
    }

    [Fact]
    public async Task ARoleInAGroupNamesTheBroadcastItBelongsTo()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 82022, groupRole: "RelaySegment"));

        Assert.Equal("ck_recording_broadcast_group", refusal.ConstraintName);
    }

    [Fact]
    public async Task DeletingTheReservationLeavesTheRecordingAndItsGroupBehind()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = await Reserve(connection, 82031);
        await Record(
            connection,
            82031,
            reservationId: reservation,
            groupKey: "'32736-1024-4001'",
            groupRole: "MovementPrimary");

        await Execute(connection, $"DELETE FROM reservation WHERE id = '{reservation}'");

        Assert.Equal(1L, await Count(connection, 82031));
        Assert.Equal("32736-1024-4001", await Scalar(connection, Reads(82031, "broadcast_group_key")));
        Assert.Equal("MovementPrimary", await Scalar(connection, Reads(82031, "broadcast_group_role")));
        Assert.Equal(reservation, await Scalar(connection, Reads(82031, "reservation_id")));
    }

    [Fact]
    public async Task RegroupingTheReservationDoesNotRewriteWhatWasRecorded()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = await Reserve(connection, 82032);
        await Record(
            connection,
            82032,
            reservationId: reservation,
            groupKey: "'32736-1024-4001'",
            groupRole: "MovementPrimary");

        await Execute(
            connection,
            $"""
            UPDATE reservation
            SET broadcast_group_key = '32737-2048-9999', broadcast_group_role = 'MovementSuppressed'
            WHERE id = '{reservation}'
            """);

        Assert.Equal("32736-1024-4001", await Scalar(connection, Reads(82032, "broadcast_group_key")));
        Assert.Equal("MovementPrimary", await Scalar(connection, Reads(82032, "broadcast_group_role")));
    }

    private static string Reads(int networkId, string column)
        => $"SELECT {column} FROM recording WHERE network_id = {networkId}";

    private static async Task<Guid> Reserve(NpgsqlConnection connection, int networkId)
    {
        var id = Guid.NewGuid();

        await Execute(
            connection,
            $"""
            INSERT INTO reservation (
                id, network_id, service_id, event_id, programme_start_at, rule_id, priority,
                start_at, end_at, end_at_confirmed, margin_before, margin_after,
                snapshot_name, snapshot_summary, snapshot_extended, snapshot_genres, captured_at,
                epg_diverged, epg_diverged_detail, epg_missing, acknowledged_at,
                broadcast_group_key, broadcast_group_role, state, started_at, recording_outcome, created_at)
            VALUES (
                '{id}', {networkId}, 1024, 4001, {Airs}, NULL, 10,
                {Airs}, {Ends}, true, 0, 0,
                'A programme', 'What it is about', '', '[]'::jsonb, {Now},
                false, '[]'::jsonb, false, NULL,
                '32736-1024-4001', 'MovementPrimary', 'Scheduled', NULL, NULL, {Now})
            """);

        return id;
    }

    private static async Task Record(
        NpgsqlConnection connection,
        int networkId,
        Guid? reservationId = null,
        string? groupKey = null,
        string groupRole = "Standalone")
        => await Execute(
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
                cc_measured, cc_dropped_packets, cc_total_packets)
            VALUES (
                gen_random_uuid(), {(reservationId is { } held ? $"'{held}'" : "NULL")},
                {networkId}, 1024, 4001, {Airs},
                'bulk', 'recording.m2ts', NULL, NULL,
                {Airs}, NULL, NULL,
                0, 0, '[]'::jsonb,
                {Airs}, {Ends},
                NULL, '[]'::jsonb,
                NULL, 0, NULL,
                'A programme', 'What it is about', '', '[]'::jsonb, {Now},
                {groupKey ?? "NULL"}, '{groupRole}',
                false, NULL, NULL)
            """);

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
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
