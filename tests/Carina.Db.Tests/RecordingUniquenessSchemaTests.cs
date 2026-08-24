using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingUniquenessSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string Now = "timestamptz '2026-08-24 12:00:00+00'";

    private const string OneReason = """
        '[{"fault":"DriverLost","tuneFailure":null,"note":"","noticedAt":"2026-08-24T20:10:00Z"}]'::jsonb
        """;

    [Fact]
    public async Task OneFileIsClaimedByOneRowAndNoMore()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await Record(connection, 85001, first, Named(first));

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 85001, second, Named(first), eventId: 4002));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_file_name", refusal.ConstraintName);
        Assert.Equal(1L, await Count(connection, 85001));
    }

    [Fact]
    public async Task TheFileIndexStandsBehindThatEvenSo()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            "CREATE UNIQUE INDEX ux_recording_file ON public.recording "
            + "USING btree (output_root, file_name)",
            await Scalar(connection, "SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_recording_file'"));
    }

    [Fact]
    public async Task TheReservationIndexReachesEveryRecordingAReservationEverHad()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            "CREATE UNIQUE INDEX ux_recording_reservation ON public.recording "
            + "USING btree (reservation_id) WHERE (reservation_id IS NOT NULL)",
            await Scalar(
                connection,
                "SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_recording_reservation'"));
    }

    [Fact]
    public async Task AReservationHasOneRecordingAndNoMoreWhileItRuns()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = await Reserve(connection, 85003);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await Record(connection, 85003, first, Named(first), reservationId: reservation);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 85003, second, Named(second), eventId: 4002, reservationId: reservation));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refusal.SqlState);
        Assert.Equal("ux_recording_reservation", refusal.ConstraintName);
        Assert.Equal(1L, await Count(connection, 85003));
    }

    [Theory]
    [InlineData("Complete", "3400000000", 85101)]
    [InlineData("Truncated", "1200000", 85102)]
    [InlineData("Failed", "0", 85103)]
    public async Task AReservationIsNotTriedAgainOnceItsRecordingHasEnded(
        string outcome,
        string size,
        int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = await Reserve(connection, networkId);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await Record(connection, networkId, first, Named(first), reservationId: reservation);
        await Settle(connection, first, outcome, size);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, networkId, second, Named(second), eventId: 4002, reservationId: reservation));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refusal.SqlState);
        Assert.Equal("ux_recording_reservation", refusal.ConstraintName);
        Assert.Equal(1L, await Count(connection, networkId));
    }

    [Fact]
    public async Task ACompletedRecordingIsNotDraggedBackToRecordingByASecondRow()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = await Reserve(connection, 85104);
        var first = Guid.NewGuid();
        await Record(connection, 85104, first, Named(first), reservationId: reservation);
        await Settle(connection, first, "Complete", "3400000000");

        Assert.Equal("Complete", await Composite(connection, reservation));

        var second = Guid.NewGuid();
        await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 85104, second, Named(second), eventId: 4002, reservationId: reservation));

        Assert.Equal("Complete", await Composite(connection, reservation));
    }

    [Fact]
    public async Task ARecordingKeepsTheReservationItWasStartedFor()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid mine = await Reserve(connection, 85011);
        Guid theirs = await Reserve(connection, 85011, eventId: 4002);
        var id = Guid.NewGuid();
        await Record(connection, 85011, id, Named(id), reservationId: mine);

        PostgresException moved = await Assert.ThrowsAsync<PostgresException>(
            () => Execute(connection, $"UPDATE recording SET reservation_id = '{theirs}' WHERE id = '{id}'"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, moved.SqlState);
        Assert.Equal(mine, await Scalar(connection, $"SELECT reservation_id FROM recording WHERE id = '{id}'"));
    }

    [Fact]
    public async Task ARecordingIsNotDetachedFromItsReservationLater()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = await Reserve(connection, 85012);
        var id = Guid.NewGuid();
        await Record(connection, 85012, id, Named(id), reservationId: reservation);

        await Assert.ThrowsAsync<PostgresException>(
            () => Execute(connection, $"UPDATE recording SET reservation_id = NULL WHERE id = '{id}'"));

        Assert.Equal(reservation, await Scalar(connection, $"SELECT reservation_id FROM recording WHERE id = '{id}'"));
    }

    [Fact]
    public async Task ARecordingNobodyReservedIsNotAttachedToOneLater()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = await Reserve(connection, 85013);
        var id = Guid.NewGuid();
        await Record(connection, 85013, id, Named(id));

        await Assert.ThrowsAsync<PostgresException>(
            () => Execute(connection, $"UPDATE recording SET reservation_id = '{reservation}' WHERE id = '{id}'"));

        Assert.Null(await Scalar(connection, $"SELECT reservation_id FROM recording WHERE id = '{id}'"));
    }

    [Fact]
    public async Task AWriteThatLeavesTheReservationWhereItIsIsNotRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = await Reserve(connection, 85014);
        var id = Guid.NewGuid();
        await Record(connection, 85014, id, Named(id), reservationId: reservation);

        await Execute(
            connection,
            $"UPDATE recording SET reservation_id = '{reservation}', written_duration_ms = 600000 WHERE id = '{id}'");

        Assert.Equal(600_000L, await Scalar(connection, $"SELECT written_duration_ms FROM recording WHERE id = '{id}'"));
    }

    [Fact]
    public async Task ARecordingWithNoReservationDoesNotCollideWithAnother()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await Record(connection, 85005, first, Named(first));

        await Record(connection, 85005, second, Named(second), eventId: 4002);

        Assert.Equal(2L, await Count(connection, 85005));
    }

    [Fact]
    public async Task AFileThatNamesAnotherRecordingIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        var id = Guid.NewGuid();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 85006, id, Named(Guid.NewGuid())));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_file_name", refusal.ConstraintName);
        Assert.Equal(0L, await Count(connection, 85006));
    }

    [Fact]
    public async Task AFileNamedForItsRecordingIsAccepted()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        var id = Guid.NewGuid();

        await Record(connection, 85007, id, $"carina-{Named(id)}");

        Assert.Equal(1L, await Count(connection, 85007));
    }

    [Fact]
    public async Task RenamingARowOntoAnotherRecordingsNameIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        var id = Guid.NewGuid();
        await Record(connection, 85008, id, Named(id));

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Execute(
                connection,
                $"UPDATE recording SET file_name = '{Named(Guid.NewGuid())}' WHERE id = '{id}'"));

        Assert.Equal("ck_recording_file_name", refusal.ConstraintName);
    }

    private static string Named(Guid id) => id.ToString("N") + ".m2ts";

    private static Task Settle(NpgsqlConnection connection, Guid recording, string outcome, string size)
        => Execute(
            connection,
            $"""
            UPDATE recording
            SET recording_outcome = '{outcome}', stopped_at_actual = {Ends}, observed_at = {Ends},
                aborted_at = {Ends}, file_size_observed = {size},
                outcome_detail = {(outcome == "Complete" ? "'[]'::jsonb" : OneReason)}
            WHERE id = '{recording}'
            """);

    private static async Task<string?> Composite(NpgsqlConnection connection, Guid reservation)
        => await Scalar(connection, $"SELECT composite_state FROM reservation WHERE id = '{reservation}'") as string;

    private static async Task<Guid> Reserve(NpgsqlConnection connection, int networkId, int eventId = 4001)
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
                '{id}', {networkId}, 1024, {eventId}, {Airs}, NULL, 10,
                {Airs}, {Ends}, true, 0, 0,
                'A programme', 'What it is about', '', '[]'::jsonb, {Now},
                false, '[]'::jsonb, false, NULL,
                NULL, 'Standalone', 'Scheduled', {Airs}, NULL, {Now})
            """);

        return id;
    }

    private static async Task Record(
        NpgsqlConnection connection,
        int networkId,
        Guid id,
        string fileName,
        int eventId = 4001,
        string outputRoot = "bulk",
        Guid? reservationId = null)
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
                cc_measured, cc_dropped_packets, cc_total_packets,
                pcr_anchor, drop_positions, pcr_reanchors, tuner_device_id, thumbnail_state)
            VALUES (
                '{id}', {(reservationId is { } held ? $"'{held}'" : "NULL")},
                {networkId}, 1024, {eventId}, {Airs},
                '{outputRoot}', '{fileName}', NULL, NULL,
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
