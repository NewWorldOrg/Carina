using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingProjectionSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string Now = "timestamptz '2026-08-24 12:00:00+00'";

    private const string OneReason = """
        '[{"fault":"DriverLost","tuneFailure":null,"note":"","noticedAt":"2026-08-24T20:10:00Z"}]'::jsonb
        """;

    [Fact]
    public async Task ARecordingThatHasNotEndedLeavesTheReservationSayingNothing()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = await Reserve(connection, 86001);
        await Record(connection, 86001, reservation);

        Assert.Null(await Outcome(connection, reservation));
        Assert.Equal("Recording", await Composite(connection, reservation));
    }

    [Theory]
    [InlineData("Complete", 86011)]
    [InlineData("Truncated", 86012)]
    [InlineData("Failed", 86013)]
    public async Task SettlingTheRecordingIsWhatWritesTheReservation(string outcome, int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = await Reserve(connection, networkId);
        Guid recording = await Record(connection, networkId, reservation);

        await Settle(connection, recording, outcome);

        Assert.Equal(outcome, await Outcome(connection, reservation));
        Assert.Equal(outcome, await Composite(connection, reservation));
    }

    [Fact]
    public async Task TheProjectionRidesOnTheSameStatementSoNothingCanBeLeftHalfWritten()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = await Reserve(connection, 86021);
        Guid recording = await Record(connection, 86021, reservation);

        await using (NpgsqlTransaction rolled = await connection.BeginTransactionAsync())
        {
            await Settle(connection, recording, "Truncated");
            Assert.Equal("Truncated", await Outcome(connection, reservation));
            await rolled.RollbackAsync();
        }

        Assert.Null(await Outcome(connection, reservation));
        Assert.Null(await Scalar(connection, $"SELECT recording_outcome FROM recording WHERE id = '{recording}'"));
    }

    [Fact]
    public async Task NoStateOfTheLedgerLeavesTheTwoSayingDifferentThings()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = await Reserve(connection, 86031);
        Guid recording = await Record(connection, 86031, reservation);
        await Settle(connection, recording, "Truncated");

        Assert.Equal(
            0L,
            await Scalar(
                connection,
                """
                SELECT count(*)
                FROM recording
                JOIN reservation ON reservation.id = recording.reservation_id
                WHERE reservation.recording_outcome IS DISTINCT FROM recording.recording_outcome
                """));
    }

    [Fact]
    public async Task ARecordingNobodyReservedWritesNothingAnywhere()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = await Reserve(connection, 86041);
        Guid recording = await Record(connection, 86041, null, eventId: 4002);

        await Settle(connection, recording, "Complete", size: "3400000000");

        Assert.Null(await Outcome(connection, reservation));
    }

    [Fact]
    public async Task TheProjectionIsTheDatabasesJobAndItSaysSoByName()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            "recording_projects_its_outcome",
            await Scalar(
                connection,
                """
                SELECT trigger.tgname
                FROM pg_trigger AS trigger
                JOIN pg_class AS table_of ON table_of.oid = trigger.tgrelid
                WHERE table_of.relname = 'recording' AND NOT trigger.tgisinternal
                """));
    }

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
                NULL, 'Standalone', 'Scheduled', {Airs}, NULL, {Now})
            """);

        return id;
    }

    private static async Task<Guid> Record(
        NpgsqlConnection connection,
        int networkId,
        Guid? reservationId,
        int eventId = 4001)
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
                '{id}', {(reservationId is { } held ? $"'{held}'" : "NULL")},
                {networkId}, 1024, {eventId}, {Airs},
                'bulk', '{id:N}.m2ts', NULL, NULL,
                {Airs}, NULL, NULL,
                0, 0, '[]'::jsonb,
                {Airs}, {Ends},
                NULL, '[]'::jsonb,
                NULL, 0, NULL,
                'A programme', 'What it is about', '', '[]'::jsonb, {Now},
                NULL, 'Standalone',
                false, NULL, NULL,
                NULL, '[]'::jsonb, '[]'::jsonb, 'pt3-0', 'Pending')
            """);

        return id;
    }

    private static Task Settle(
        NpgsqlConnection connection,
        Guid recording,
        string outcome,
        string size = "1200000")
        => Execute(
            connection,
            $"""
            UPDATE recording
            SET recording_outcome = '{outcome}', stopped_at_actual = {Ends}, observed_at = {Ends},
                aborted_at = {Ends}, file_size_observed = {size},
                outcome_detail = {(outcome == "Complete" ? "'[]'::jsonb" : OneReason)}
            WHERE id = '{recording}'
            """);

    private static async Task<string?> Outcome(NpgsqlConnection connection, Guid reservation)
        => await Scalar(connection, $"SELECT recording_outcome FROM reservation WHERE id = '{reservation}'") as string;

    private static async Task<string?> Composite(NpgsqlConnection connection, Guid reservation)
        => await Scalar(connection, $"SELECT composite_state FROM reservation WHERE id = '{reservation}'") as string;

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
