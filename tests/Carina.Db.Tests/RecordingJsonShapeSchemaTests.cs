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
    [InlineData("""'{"not":"an array"}'::jsonb""", 44011)]
    [InlineData("'\"a string\"'::jsonb", 44012)]
    [InlineData("'7'::jsonb", 44013)]
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
            () => Record(connection, 44014, detail: """'{"not":"an array"}'::jsonb"""));

        Assert.Equal("ck_recording_reasons", refusal.ConstraintName);
    }

    [Fact]
    public async Task AFaultTheLedgerDoesNotHoldIsRefusedInTheHistory()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                44021,
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
                44022,
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
                44023,
                detail: """'[{"fault":"TuneFailed","tuneFailure":"Sunspots","note":"","noticedAt":"2026-08-24T20:10:00Z"}]'::jsonb"""));

        Assert.Equal("ck_recording_reasons", refusal.ConstraintName);
    }

    [Theory]
    [InlineData("""'[{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00+09:00","resumedAt":null}]'::jsonb""", 44031)]
    [InlineData("""'[{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00","resumedAt":null}]'::jsonb""", 44032)]
    [InlineData("""'[{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00Z","resumedAt":"2026-08-24T20:11:00+09:00"}]'::jsonb""", 44033)]
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
                44034,
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
                44041,
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
                44042,
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
                44043,
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
                44044,
                interruptions: """'[{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00Z","resumedAt":"2026-08-24T20:11:00Z"}]'::jsonb""",
                resumeCount: 2));

        Assert.Equal("ck_recording_history", refusal.ConstraintName);

        await Record(
            connection,
            44044,
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
            44051,
            interruptions: """
                '[{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00Z","resumedAt":"2026-08-24T20:11:00Z"},
                  {"fault":"DiskExhausted","occurredAt":"2026-08-24T20:30:00Z","resumedAt":null}]'::jsonb
                """,
            resumeCount: 1,
            detail: OneReason);

        Assert.Equal(2, await Scalar(connection, "SELECT jsonb_array_length(interruptions) FROM recording WHERE network_id = 44051"));
    }

    [Theory]
    [InlineData("""'[{"second":12,"continuity":3,"scrambled":0},{"second":12,"continuity":1,"scrambled":0}]'::jsonb""", 44061)]
    [InlineData("""'[{"second":40,"continuity":1,"scrambled":0},{"second":12,"continuity":1,"scrambled":0}]'::jsonb""", 44062)]
    [InlineData("""'[{"second":-1,"continuity":1,"scrambled":0}]'::jsonb""", 44063)]
    [InlineData("""'[{"second":12,"continuity":0,"scrambled":0}]'::jsonb""", 44064)]
    [InlineData("""'{"not":"an array"}'::jsonb""", 44065)]
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
                44071,
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
                44072,
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
    [InlineData("""'[{"second":2,"before":8589934592,"after":0}]'::jsonb""", 44081)]
    [InlineData("""'[{"second":-1,"before":1,"after":0}]'::jsonb""", 44082)]
    [InlineData("""'[{"second":4,"before":1,"after":0},{"second":4,"before":1,"after":0}]'::jsonb""", 44083)]
    [InlineData("""'{"not":"an array"}'::jsonb""", 44084)]
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
            44091,
            anchor: "900000",
            positions: """'[{"second":12,"continuity":3,"scrambled":0},{"second":40,"continuity":0,"scrambled":188}]'::jsonb""",
            reanchors: """'[{"second":20,"before":8589934591,"after":0}]'::jsonb""",
            ccMeasured: "true",
            ccDropped: "3",
            ccTotal: "1000",
            measuredAt: Counted,
            scrambled: "188");

        Assert.Equal(2, await Scalar(connection, "SELECT jsonb_array_length(drop_positions) FROM recording WHERE network_id = 44091"));
    }

    [Fact]
    public async Task AnInterruptionBeforeTheRecordingStartedIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                44101,
                interruptions: """'[{"fault":"DriverLost","occurredAt":"2026-08-24T19:59:59Z","resumedAt":null}]'::jsonb"""));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_history", refusal.ConstraintName);
    }

    [Fact]
    public async Task AReasonNoticedBeforeTheRecordingStartedIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                44102,
                detail: """
                    '[{"fault":"DiskExhausted","tuneFailure":null,"note":"","noticedAt":"2026-08-24T19:59:59Z"}]'::jsonb
                    """));

        Assert.Equal("ck_recording_reasons", refusal.ConstraintName);
    }

    [Fact]
    public async Task AnInterruptionAtTheVeryMomentItStartedIsAccepted()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(
            connection,
            44103,
            interruptions: """'[{"fault":"DriverLost","occurredAt":"2026-08-24T20:00:00Z","resumedAt":null}]'::jsonb""");

        Assert.Equal(
            1,
            await Scalar(connection, "SELECT jsonb_array_length(interruptions) FROM recording WHERE network_id = 44103"));
    }

    [Theory]
    [InlineData("interruptions", "ck_recording_history", 44201)]
    [InlineData("outcome_detail", "ck_recording_reasons", 44202)]
    [InlineData("drop_positions", "ck_recording_positions", 44203)]
    [InlineData("pcr_reanchors", "ck_recording_reanchors", 44204)]
    public async Task AnElementWithNothingInItIsRefused(string column, string constraint, int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Shaped(connection, networkId, column, "'[{}]'::jsonb"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal(constraint, refusal.ConstraintName);
    }

    [Theory]
    [InlineData("interruptions", "ck_recording_history", "7", 44211)]
    [InlineData("interruptions", "ck_recording_history", "\"a string\"", 44212)]
    [InlineData("outcome_detail", "ck_recording_reasons", "7", 44213)]
    [InlineData("drop_positions", "ck_recording_positions", "7", 44214)]
    [InlineData("pcr_reanchors", "ck_recording_reanchors", "7", 44215)]
    public async Task AnElementThatIsNotAnObjectIsRefused(
        string column,
        string constraint,
        string element,
        int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Shaped(connection, networkId, column, $"'[{element}]'::jsonb"));

        Assert.Equal(constraint, refusal.ConstraintName);
    }

    [Theory]
    [InlineData("interruptions", "ck_recording_history", """{"occurredAt":"2026-08-24T20:10:00Z","resumedAt":null}""", 44221)]
    [InlineData("interruptions", "ck_recording_history", """{"fault":"DriverLost","resumedAt":null}""", 44222)]
    [InlineData("interruptions", "ck_recording_history", """{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00Z"}""", 44223)]
    [InlineData("outcome_detail", "ck_recording_reasons", """{"tuneFailure":null,"note":"","noticedAt":"2026-08-24T20:10:00Z"}""", 44224)]
    [InlineData("outcome_detail", "ck_recording_reasons", """{"fault":"DriverLost","note":"","noticedAt":"2026-08-24T20:10:00Z"}""", 44225)]
    [InlineData("outcome_detail", "ck_recording_reasons", """{"fault":"DriverLost","tuneFailure":null,"noticedAt":"2026-08-24T20:10:00Z"}""", 44226)]
    [InlineData("outcome_detail", "ck_recording_reasons", """{"fault":"DriverLost","tuneFailure":null,"note":""}""", 44227)]
    [InlineData("drop_positions", "ck_recording_positions", """{"continuity":3,"scrambled":0}""", 44228)]
    [InlineData("drop_positions", "ck_recording_positions", """{"second":12,"scrambled":0}""", 44229)]
    [InlineData("drop_positions", "ck_recording_positions", """{"second":12,"continuity":3}""", 44230)]
    [InlineData("pcr_reanchors", "ck_recording_reanchors", """{"before":1,"after":0}""", 44231)]
    [InlineData("pcr_reanchors", "ck_recording_reanchors", """{"second":2,"after":0}""", 44232)]
    [InlineData("pcr_reanchors", "ck_recording_reanchors", """{"second":2,"before":1}""", 44233)]
    public async Task AnElementMissingOneKeyIsRefused(
        string column,
        string constraint,
        string element,
        int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Shaped(connection, networkId, column, $"'[{element}]'::jsonb"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal(constraint, refusal.ConstraintName);
    }

    [Theory]
    [InlineData("""{"fault":"DriverLost","occurredAt":"XYZ","resumedAt":null}""", 44241)]
    [InlineData("""{"fault":"DriverLost","occurredAt":"2026-08-24 20:10:00+00","resumedAt":null}""", 44242)]
    [InlineData("""{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00Z","resumedAt":"nonsense"}""", 44243)]
    public async Task ATimeThatIsNotAnInstantComesBackAsACheckViolation(string element, int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Shaped(connection, networkId, "interruptions", $"'[{element}]'::jsonb"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_history", refusal.ConstraintName);
    }

    [Fact]
    public async Task TwoBrokenTimesInOneHistoryStillComeBackAsACheckViolation()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Shaped(
                connection,
                44244,
                "interruptions",
                """
                '[{"fault":"DriverLost","occurredAt":"XYZ","resumedAt":null},
                  {"fault":"DriverLost","occurredAt":"ABC","resumedAt":null}]'::jsonb
                """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_history", refusal.ConstraintName);
    }

    [Theory]
    [InlineData("""{"second":1.5,"continuity":3,"scrambled":0}""", 44251)]
    [InlineData("""{"second":2147483648,"continuity":3,"scrambled":0}""", 44252)]
    [InlineData("""{"second":12,"continuity":1.5,"scrambled":0}""", 44253)]
    [InlineData("""{"second":12,"continuity":"3","scrambled":0}""", 44254)]
    public async Task ANumberTheApplicationCouldNotReadBackIsRefused(string element, int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Shaped(connection, networkId, "drop_positions", $"'[{element}]'::jsonb", anchor: "900000"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_positions", refusal.ConstraintName);
    }

    [Fact]
    public async Task ANoteThatIsNotAStringIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Shaped(
                connection,
                44261,
                "outcome_detail",
                """'[{"fault":"DriverLost","tuneFailure":null,"note":7,"noticedAt":"2026-08-24T20:10:00Z"}]'::jsonb"""));

        Assert.Equal("ck_recording_reasons", refusal.ConstraintName);
    }

    [Fact]
    public async Task AnEmptyArrayIsStillTheOrdinaryCase()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, 44271);

        Assert.Equal(
            0,
            await Scalar(connection, "SELECT jsonb_array_length(interruptions) FROM recording WHERE network_id = 44271"));
    }

    [Theory]
    [InlineData("interruptions", "ck_recording_history", """{"fault":null,"occurredAt":"2026-08-24T20:10:00Z","resumedAt":null}""", 44281)]
    [InlineData("outcome_detail", "ck_recording_reasons", """{"fault":null,"tuneFailure":null,"note":"","noticedAt":"2026-08-24T20:10:00Z"}""", 44282)]
    [InlineData("drop_positions", "ck_recording_positions", """{"second":null,"continuity":3,"scrambled":0}""", 44283)]
    [InlineData("pcr_reanchors", "ck_recording_reanchors", """{"second":2,"before":null,"after":0}""", 44284)]
    public async Task AKeyThatIsPresentButSaysNothingIsRefused(
        string column,
        string constraint,
        string element,
        int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Shaped(connection, networkId, column, $"'[{element}]'::jsonb"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal(constraint, refusal.ConstraintName);
    }

    [Theory]
    [InlineData("interruptions", "ck_recording_history", 44291)]
    [InlineData("outcome_detail", "ck_recording_reasons", 44292)]
    public async Task AnArrayThatCarriesTheKeyNamesInsteadOfCarryingKeysIsRefused(
        string column,
        string constraint,
        int networkId)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Shaped(
                connection,
                networkId,
                column,
                """'[["fault","occurredAt","resumedAt","tuneFailure","note","noticedAt"]]'::jsonb"""));

        Assert.Equal(constraint, refusal.ConstraintName);
    }

    private static Task Shaped(
        NpgsqlConnection connection,
        int networkId,
        string column,
        string value,
        string? anchor = null)
        => column switch
        {
            "interruptions" => Record(connection, networkId, interruptions: value),
            "outcome_detail" => Record(connection, networkId, detail: value),
            "drop_positions" => Record(
                connection,
                networkId,
                anchor: anchor ?? "900000",
                positions: value,
                ccMeasured: "true",
                ccDropped: "1000",
                ccTotal: "100000",
                measuredAt: Counted),
            "pcr_reanchors" => Record(
                connection,
                networkId,
                anchor: "900000",
                reanchors: value,
                ccMeasured: "true",
                ccDropped: "0",
                ccTotal: "1000",
                measuredAt: Counted),
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, "No such payload."),
        };

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
