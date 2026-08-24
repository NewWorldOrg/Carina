using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingDropPositionSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const long PcrWrapsAt = 8_589_934_592;

    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string Now = "timestamptz '2026-08-24 12:00:00+00'";

    private const string Counted = "timestamptz '2026-08-24 20:30:00+00'";

    private const string OneBucket = """'[{"second":12,"continuity":3,"scrambled":0}]'::jsonb""";

    [Fact]
    public async Task APositionWithoutAnAnchorCannotBeMappedBackSoItIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 43001, positions: OneBucket, ccMeasured: "true", ccDropped: "3", ccTotal: "1000", measuredAt: Counted));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_drop_positions", refusal.ConstraintName);
        Assert.Equal(0L, await Count(connection, 43001));
    }

    [Fact]
    public async Task AReanchorWithoutAnAnchorIsRefusedTheSameWay()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 43002, reanchors: """'[{"second":2,"before":8589934591,"after":0}]'::jsonb"""));

        Assert.Equal("ck_recording_drop_positions", refusal.ConstraintName);
    }

    [Fact]
    public async Task APositionCannotRideOnAMeasurementNobodyTook()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 43003, anchor: "900000", ccMeasured: "false"));

        Assert.Equal("ck_recording_drop_positions", refusal.ConstraintName);
        Assert.Equal(0L, await Count(connection, 43003));
    }

    [Fact]
    public async Task LocatingNothingIsAStateTheDatabaseKeepsApartFromLocatingNowhere()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, 43004, anchor: "900000", ccMeasured: "true", ccDropped: "0", ccTotal: "1000", measuredAt: Counted);
        await Record(connection, 43004, eventId: 4002, ccMeasured: "true", ccDropped: "0", ccTotal: "1000", measuredAt: Counted);

        Assert.Equal(900_000L, await Scalar(connection, Reads(43004, 4001, "pcr_anchor")));
        Assert.Null(await Scalar(connection, Reads(43004, 4002, "pcr_anchor")));
        Assert.Equal(0, await Scalar(connection, Reads(43004, 4001, "jsonb_array_length(drop_positions)")));
        Assert.Equal(0, await Scalar(connection, Reads(43004, 4002, "jsonb_array_length(drop_positions)")));
    }

    [Fact]
    public async Task AnUnmeasuredRecordingCarriesNoBucketToBeCountedAsZeroPerCent()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, 43005);

        Assert.Equal(
            0L,
            await Scalar(
                connection,
                """
                SELECT count(*) FROM recording
                WHERE NOT cc_measured AND (pcr_anchor IS NOT NULL OR jsonb_array_length(drop_positions) > 0)
                """));
    }

    [Fact]
    public async Task ALostPacketAndAScrambledOneAreKeptApartInTheSameBucket()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(
            connection,
            43006,
            anchor: "900000",
            positions: """'[{"second":12,"continuity":3,"scrambled":0},{"second":40,"continuity":0,"scrambled":188}]'::jsonb""",
            ccMeasured: "true",
            ccDropped: "3",
            ccTotal: "1000",
            measuredAt: Counted);

        Assert.Equal("3", await Scalar(connection, Reads(43006, 4001, "drop_positions -> 0 ->> 'continuity'")));
        Assert.Equal("188", await Scalar(connection, Reads(43006, 4001, "drop_positions -> 1 ->> 'scrambled'")));
    }

    [Theory]
    [InlineData("0", 43011)]
    [InlineData("8589934591", 43012)]
    public async Task TheAnchorFitsTheWholeClock(string anchor, int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, networkId, anchor: anchor, ccMeasured: "true", ccDropped: "0", ccTotal: "1000", measuredAt: Counted);

        Assert.Equal(long.Parse(anchor, System.Globalization.CultureInfo.InvariantCulture),
            await Scalar(connection, Reads(networkId, 4001, "pcr_anchor")));
    }

    [Theory]
    [InlineData("8589934592", 43013)]
    [InlineData("-1", 43014)]
    public async Task AnAnchorOutsideTheClockIsRefused(string anchor, int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, networkId, anchor: anchor, ccMeasured: "true", ccDropped: "0", ccTotal: "1000", measuredAt: Counted));

        Assert.Equal("ck_recording_drop_positions", refusal.ConstraintName);
    }

    [Fact]
    public async Task ARecordingWhoseClockStartedAgainKeepsAMonotoneTimeline()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(
            connection,
            43015,
            anchor: (PcrWrapsAt - 90_000).ToString(System.Globalization.CultureInfo.InvariantCulture),
            positions: """'[{"second":1,"continuity":2,"scrambled":0},{"second":4,"continuity":1,"scrambled":0}]'::jsonb""",
            reanchors: """'[{"second":2,"before":8589934591,"after":0}]'::jsonb""",
            ccMeasured: "true",
            ccDropped: "3",
            ccTotal: "1000",
            measuredAt: Counted);

        Assert.Equal("1", await Scalar(connection, Reads(43015, 4001, "drop_positions -> 0 ->> 'second'")));
        Assert.Equal("4", await Scalar(connection, Reads(43015, 4001, "drop_positions -> 1 ->> 'second'")));
        Assert.Equal("8589934591", await Scalar(connection, Reads(43015, 4001, "pcr_reanchors -> 0 ->> 'before'")));
        Assert.Equal("0", await Scalar(connection, Reads(43015, 4001, "pcr_reanchors -> 0 ->> 'after'")));
    }

    [Fact]
    public async Task AReanchorIsNotAReasonTheRecordingFailed()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(
            connection,
            43016,
            anchor: "900000",
            reanchors: """'[{"second":2,"before":8589934591,"after":0}]'::jsonb""",
            ccMeasured: "true",
            ccDropped: "0",
            ccTotal: "1000",
            measuredAt: Counted);

        Assert.Equal(
            1,
            await Scalar(connection, Reads(43016, 4001, "jsonb_array_length(pcr_reanchors)")));
        Assert.Equal(
            0,
            await Scalar(connection, Reads(43016, 4001, "jsonb_array_length(outcome_detail)")));
    }

    [Fact]
    public async Task TheAnchorIsWideEnoughForTheClockItReads()
        => Assert.Equal(
            "bigint",
            await Scalar(
                await database.OpenAsync(),
                """
                SELECT data_type FROM information_schema.columns
                WHERE table_name = 'recording' AND column_name = 'pcr_anchor'
                """));

    private static string Reads(int networkId, int eventId, string column)
        => $"SELECT {column} FROM recording WHERE network_id = {networkId} AND event_id = {eventId}";

    private static async Task Record(
        NpgsqlConnection connection,
        int networkId,
        int eventId = 4001,
        string? anchor = null,
        string? positions = null,
        string? reanchors = null,
        string ccMeasured = "false",
        string? ccDropped = null,
        string? ccTotal = null,
        string? measuredAt = null,
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
                '{id}', NULL, {networkId}, 1024, {eventId}, {Airs},
                'bulk', '{id:N}.m2ts', NULL, NULL,
                {Airs}, NULL, NULL,
                0, 0, '[]'::jsonb,
                {Airs}, {Ends},
                NULL, '[]'::jsonb,
                200, 0, {measuredAt ?? "NULL"},
                'A programme', 'What it is about', '', '[]'::jsonb, {Now},
                NULL, 'Standalone',
                {ccMeasured}, {ccDropped ?? "NULL"}, {ccTotal ?? "NULL"},
                {anchor ?? "NULL"}, {positions ?? "'[]'::jsonb"}, {reanchors ?? "'[]'::jsonb"}, {tuner ?? "'pt3-0'"}, {thumbnail ?? "'Pending'"})
            """);
    }

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
