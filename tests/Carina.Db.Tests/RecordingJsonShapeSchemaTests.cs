using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingJsonShapeSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string Now = "timestamptz '2026-08-24 12:00:00+00'";

    private const string Counted = "timestamptz '2026-08-24 20:30:00+00'";

    private const string OneReason = """
        '[{"fault":"DriverLost","tuneFailure":null,"note":"","noticedAt":"2026-08-24T20:10:00Z"}]'::jsonb
        """;

    [Theory]
    [InlineData("""'{"not":"an array"}'::jsonb""", 84011)]
    [InlineData("'\"a string\"'::jsonb", 84012)]
    [InlineData("'7'::jsonb", 84013)]
    public async Task TheHistoryIsAnArrayOrItIsNothing(string shape, int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, networkId, interruptions: shape));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_history", refusal.ConstraintName);
    }

    [Fact]
    public async Task TheReasonsAreAnArrayOrTheyAreNothing()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, 84014, detail: """'{"not":"an array"}'::jsonb"""));

        Assert.Equal("ck_recording_reasons", refusal.ConstraintName);
    }

    [Fact]
    public async Task AFaultTheLedgerDoesNotHoldIsRefusedInTheHistory()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                84021,
                interruptions: """'[{"fault":"MeteorStrike","occurredAt":"2026-08-24T20:10:00Z","resumedAt":null}]'::jsonb"""));

        Assert.Equal("ck_recording_history", refusal.ConstraintName);
    }

    [Fact]
    public async Task AFaultTheLedgerDoesNotHoldIsRefusedInTheReasons()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                84022,
                detail: """'[{"fault":"MeteorStrike","tuneFailure":null,"note":"","noticedAt":"2026-08-24T20:10:00Z"}]'::jsonb"""));

        Assert.Equal("ck_recording_reasons", refusal.ConstraintName);
    }

    [Fact]
    public async Task ATuneFailureTheLedgerDoesNotHoldIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                84023,
                detail: """'[{"fault":"TuneFailed","tuneFailure":"Sunspots","note":"","noticedAt":"2026-08-24T20:10:00Z"}]'::jsonb"""));

        Assert.Equal("ck_recording_reasons", refusal.ConstraintName);
    }

    [Theory]
    [InlineData("""'[{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00+09:00","resumedAt":null}]'::jsonb""", 84031)]
    [InlineData("""'[{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00","resumedAt":null}]'::jsonb""", 84032)]
    [InlineData("""'[{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00Z","resumedAt":"2026-08-24T20:11:00+09:00"}]'::jsonb""", 84033)]
    public async Task ATimeInsideTheHistoryThatIsNotUtcIsRefused(string history, int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, networkId, interruptions: history, resumeCount: history.Contains("20:11", StringComparison.Ordinal) ? 1 : 0));

        Assert.Equal("ck_recording_history", refusal.ConstraintName);
    }

    [Fact]
    public async Task ATimeInsideTheReasonsThatIsNotUtcIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                84034,
                detail: """'[{"fault":"DriverLost","tuneFailure":null,"note":"","noticedAt":"2026-08-24T20:10:00+09:00"}]'::jsonb"""));

        Assert.Equal("ck_recording_reasons", refusal.ConstraintName);
    }

    [Fact]
    public async Task ARecordingResumesAfterItWasInterrupted()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                84041,
                interruptions: """'[{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00Z","resumedAt":"2026-08-24T20:09:00Z"}]'::jsonb""",
                resumeCount: 1));

        Assert.Equal("ck_recording_history", refusal.ConstraintName);
    }

    [Fact]
    public async Task TheHistoryIsKeptInTheOrderItHappened()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                84042,
                interruptions: """
                    '[{"fault":"DriverLost","occurredAt":"2026-08-24T20:30:00Z","resumedAt":"2026-08-24T20:31:00Z"},
                      {"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00Z","resumedAt":"2026-08-24T20:11:00Z"}]'::jsonb
                    """,
                resumeCount: 2));

        Assert.Equal("ck_recording_history", refusal.ConstraintName);
    }

    [Fact]
    public async Task OnlyTheLastInterruptionIsStillOpen()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                84043,
                interruptions: """
                    '[{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00Z","resumedAt":null},
                      {"fault":"DriverLost","occurredAt":"2026-08-24T20:30:00Z","resumedAt":"2026-08-24T20:31:00Z"}]'::jsonb
                    """,
                resumeCount: 1));

        Assert.Equal("ck_recording_history", refusal.ConstraintName);
    }

    [Fact]
    public async Task TheResumeCountIsTheNumberOfInterruptionsThatWereClosed()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                84044,
                interruptions: """'[{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00Z","resumedAt":"2026-08-24T20:11:00Z"}]'::jsonb""",
                resumeCount: 2));

        Assert.Equal("ck_recording_history", refusal.ConstraintName);

        await Record(
            connection,
            84044,
            eventId: 4002,
            interruptions: """'[{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00Z","resumedAt":"2026-08-24T20:11:00Z"}]'::jsonb""",
            resumeCount: 1);
    }

    [Fact]
    public async Task AHistoryThatHoldsUpIsAccepted()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(
            connection,
            84051,
            interruptions: """
                '[{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00Z","resumedAt":"2026-08-24T20:11:00Z"},
                  {"fault":"DiskExhausted","occurredAt":"2026-08-24T20:30:00Z","resumedAt":null}]'::jsonb
                """,
            resumeCount: 1,
            detail: OneReason);

        Assert.Equal(2, await Scalar(connection, "SELECT jsonb_array_length(interruptions) FROM recording WHERE network_id = 84051"));
    }

    [Theory]
    [InlineData("""'[{"second":12,"continuity":3,"scrambled":0},{"second":12,"continuity":1,"scrambled":0}]'::jsonb""", 84061)]
    [InlineData("""'[{"second":40,"continuity":1,"scrambled":0},{"second":12,"continuity":1,"scrambled":0}]'::jsonb""", 84062)]
    [InlineData("""'[{"second":-1,"continuity":1,"scrambled":0}]'::jsonb""", 84063)]
    [InlineData("""'[{"second":12,"continuity":0,"scrambled":0}]'::jsonb""", 84064)]
    [InlineData("""'{"not":"an array"}'::jsonb""", 84065)]
    public async Task ATimelineIsSparseAndReadsForwards(string positions, int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                networkId,
                anchor: "900000",
                positions: positions,
                ccMeasured: "true",
                ccDropped: "1000",
                ccTotal: "100000",
                measuredAt: Counted));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_positions", refusal.ConstraintName);
    }

    [Fact]
    public async Task ATimelineCannotPlaceMoreLostPacketsThanWereCounted()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                84071,
                anchor: "900000",
                positions: """'[{"second":12,"continuity":3,"scrambled":0}]'::jsonb""",
                ccMeasured: "true",
                ccDropped: "2",
                ccTotal: "1000",
                measuredAt: Counted));

        Assert.Equal("ck_recording_positions", refusal.ConstraintName);
    }

    [Fact]
    public async Task ATimelineCannotPlaceScrambledPacketsThatWereNeverCounted()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                84072,
                anchor: "900000",
                positions: """'[{"second":12,"continuity":0,"scrambled":5}]'::jsonb""",
                ccMeasured: "true",
                ccDropped: "0",
                ccTotal: "1000",
                measuredAt: Counted,
                scrambled: "NULL"));

        Assert.Equal("ck_recording_positions", refusal.ConstraintName);
    }

    [Theory]
    [InlineData("""'[{"second":2,"before":8589934592,"after":0}]'::jsonb""", 84081)]
    [InlineData("""'[{"second":-1,"before":1,"after":0}]'::jsonb""", 84082)]
    [InlineData("""'[{"second":4,"before":1,"after":0},{"second":4,"before":1,"after":0}]'::jsonb""", 84083)]
    [InlineData("""'{"not":"an array"}'::jsonb""", 84084)]
    public async Task AReanchorStaysInsideTheClockAndReadsForwards(string reanchors, int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                networkId,
                anchor: "900000",
                reanchors: reanchors,
                ccMeasured: "true",
                ccDropped: "0",
                ccTotal: "1000",
                measuredAt: Counted));

        Assert.Equal("ck_recording_reanchors", refusal.ConstraintName);
    }

    [Fact]
    public async Task ATimelineThatHoldsUpIsAccepted()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(
            connection,
            84091,
            anchor: "900000",
            positions: """'[{"second":12,"continuity":3,"scrambled":0},{"second":40,"continuity":0,"scrambled":188}]'::jsonb""",
            reanchors: """'[{"second":20,"before":8589934591,"after":0}]'::jsonb""",
            ccMeasured: "true",
            ccDropped: "3",
            ccTotal: "1000",
            measuredAt: Counted,
            scrambled: "188");

        Assert.Equal(2, await Scalar(connection, "SELECT jsonb_array_length(drop_positions) FROM recording WHERE network_id = 84091"));
    }

    private static async Task Record(
        NpgsqlConnection connection,
        int networkId,
        int eventId = 4001,
        string? interruptions = null,
        int resumeCount = 0,
        string? detail = null,
        string? anchor = null,
        string? positions = null,
        string? reanchors = null,
        string ccMeasured = "false",
        string? ccDropped = null,
        string? ccTotal = null,
        string? measuredAt = null,
        string? scrambled = null,
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
                0, {resumeCount}, {interruptions ?? "'[]'::jsonb"},
                {Airs}, {Ends},
                NULL, {detail ?? "'[]'::jsonb"},
                {scrambled ?? "200"}, 0, {measuredAt ?? "NULL"},
                'A programme', 'What it is about', '', '[]'::jsonb, {Now},
                NULL, 'Standalone',
                {ccMeasured}, {ccDropped ?? "NULL"}, {ccTotal ?? "NULL"},
                {anchor ?? "NULL"}, {positions ?? "'[]'::jsonb"}, {reanchors ?? "'[]'::jsonb"}, 'pt3-0', {thumbnail ?? "'Pending'"})
            """);
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
