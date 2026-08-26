using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string Now = "timestamptz '2026-08-24 12:00:00+00'";

    [Fact]
    public async Task TheDatabaseRefusesASecondReservationForTheSameProgramme()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Reserve(connection, 70001, 4001, Airs);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Reserve(connection, 70001, 4001, Airs));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refusal.SqlState);
        Assert.Equal("ux_reservation_programme", refusal.ConstraintName);
    }

    [Fact]
    public async Task TheSameEventIdAtAnotherStartIsAnotherProgramme()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Reserve(connection, 70002, 4001, Airs);
        await Reserve(connection, 70002, 4001, $"{Airs} + interval '7 days'");

        Assert.Equal(2, await Count(connection, "reservation WHERE network_id = 70002"));
    }

    [Fact]
    public async Task ACancelledReservationGoesOnHoldingItsProgrammeDown()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Reserve(connection, 70003, 4001, Airs, state: "Cancelled");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Reserve(connection, 70003, 4001, Airs));

        Assert.Equal("ux_reservation_programme", refusal.ConstraintName);
    }

    [Fact]
    public async Task TheDatabaseKnowsOnlyTheFourStatesThisDomainOwns()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        foreach ((int order, string owned) in new[] { "Scheduled", "Conflict", "Cancelled", "Missed" }.Index())
        {
            await Reserve(connection, 70004, 4001 + order, Airs, state: owned);
        }

        foreach ((int order, string borrowed) in new[] { "Recording", "Complete", "Failed" }.Index())
        {
            PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
                () => Reserve(connection, 70004, 4900 + order, Airs, state: borrowed));

            Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
            Assert.Equal("ck_reservation_state", refusal.ConstraintName);
        }
    }

    [Fact]
    public async Task AnOutcomeWithoutAClaimIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Reserve(connection, 70005, 4001, Airs, recordingOutcome: "'Failed'"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_reservation_recording_outcome", refusal.ConstraintName);
    }

    [Fact]
    public async Task TheCompositeStateIsReadFromWhatRecordingWrote()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Reserve(connection, 70006, 4001, Airs);
        await Reserve(connection, 70006, 4002, Airs, startedAt: Now);
        await Reserve(connection, 70006, 4003, Airs, startedAt: Now, recordingOutcome: "'Truncated'");
        await Reserve(connection, 70006, 4004, Airs, state: "Conflict");

        Assert.Equal("Scheduled", await Composite(connection, 70006, 4001));
        Assert.Equal("Recording", await Composite(connection, 70006, 4002));
        Assert.Equal("Truncated", await Composite(connection, 70006, 4003));
        Assert.Equal("Conflict", await Composite(connection, 70006, 4004));
    }

    [Fact]
    public async Task AReservationSurvivesTheProgrammeGuideBeingThrownAway()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Programme(connection, 70007, 4001);
        await Reserve(connection, 70007, 4001, Airs);

        await Execute(connection, "TRUNCATE broadcast_service, programme CASCADE");

        Assert.Equal(0, await Count(connection, "programme"));
        Assert.Equal(
            "A programme",
            await Scalar(
                connection,
                "SELECT snapshot_name FROM reservation WHERE network_id = 70007 AND event_id = 4001"));
    }

    [Fact]
    public async Task DeletingARuleLeavesTheReservationsItMadeBehind()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid rule = await Rule(connection);
        await Reserve(connection, 70008, 4001, Airs, ruleId: rule);

        await Execute(connection, $"DELETE FROM rule WHERE id = '{rule}'");

        Assert.Equal(1, await Count(connection, "reservation WHERE network_id = 70008"));
        Assert.Null(await Scalar(
            connection,
            "SELECT rule_id FROM reservation WHERE network_id = 70008 AND event_id = 4001"));
    }

    [Fact]
    public async Task TheLedgerIsNotDraggedAwayWithTheReservation()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = await Reserve(connection, 70009, 4001, Airs);
        await Record(connection, reservation, "Competing");

        await Execute(connection, $"DELETE FROM reservation WHERE id = '{reservation}'");

        Assert.Equal(1, await Count(connection, $"reservation_outcome WHERE reservation_id = '{reservation}'"));
    }

    [Fact]
    public async Task ALedgerEntryIsNotFlattenedIntoOneString()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = await Reserve(connection, 70010, 4001, Airs);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, reservation, "TuneFailure"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_reservation_outcome_tune_failure", refusal.ConstraintName);

        await Record(connection, reservation, "TuneFailure", tuneFailure: "'IncompletePsi'");
    }

    [Fact]
    public async Task AnAcknowledgementWithoutADivergenceIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Reserve(connection, 70011, 4001, Airs, acknowledgedAt: Now));

        Assert.Equal("ck_reservation_divergence", refusal.ConstraintName);
    }

    [Fact]
    public async Task AReservationWithNowhereToTuneKeepsItsStateAndCarriesAMark()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid marked = await Reserve(connection, 70012, 4001, Airs, receptionUnavailableSince: Now);

        Assert.Equal(
            "Scheduled",
            await Scalar(connection, $"SELECT state FROM reservation WHERE id = '{marked}'"));
        Assert.Equal(
            true,
            await Scalar(connection, $"SELECT reception_unavailable FROM reservation WHERE id = '{marked}'"));
        Assert.Equal("Scheduled", await Composite(connection, 70012, 4001));
    }

    [Fact]
    public async Task AMarkWithoutTheMomentItWasNoticedIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Reserve(connection, 70013, 4001, Airs, receptionUnavailable: true));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_reservation_reception", refusal.ConstraintName);
    }

    [Fact]
    public async Task TheClaimableIndexNarrowsToWhatRecordingMayStart()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        string definition = (string)(await Scalar(
            connection,
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_reservation_claimable'"))!;

        Assert.Contains("started_at IS NULL", definition, StringComparison.Ordinal);
        Assert.Contains("'Scheduled'", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoTableOutsideThisDomainIsTiedToAReservation()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        long reaching = (long)(await Scalar(
            connection,
            """
            SELECT count(*)
            FROM pg_constraint AS c
            JOIN pg_class AS child ON child.oid = c.conrelid
            JOIN pg_class AS parent ON parent.oid = c.confrelid
            WHERE c.contype = 'f'
              AND (
                    (child.relname IN ('reservation', 'rule', 'reservation_outcome')
                     AND parent.relname NOT IN ('reservation', 'rule', 'reservation_outcome'))
                 OR (parent.relname IN ('reservation', 'rule', 'reservation_outcome')
                     AND child.relname NOT IN ('reservation', 'rule', 'reservation_outcome'))
                  )
            """))!;

        Assert.Equal(0, reaching);
    }

    private static async Task<Guid> Reserve(
        NpgsqlConnection connection,
        int networkId,
        int eventId,
        string programmeStartAt,
        string state = "Scheduled",
        string? startedAt = null,
        string? recordingOutcome = null,
        string? acknowledgedAt = null,
        Guid? ruleId = null,
        bool? receptionUnavailable = null,
        string? receptionUnavailableSince = null)
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
                reception_unavailable, reception_unavailable_since,
                broadcast_group_key, broadcast_group_role, state, started_at, recording_outcome, created_at)
            VALUES (
                '{id}', {networkId}, 1024, {eventId}, {programmeStartAt},
                {(ruleId is { } rule ? $"'{rule}'" : "NULL")}, 10,
                {Airs}, {Ends}, true, 10, 30,
                'A programme', 'What it is about', '', '[]'::jsonb, {Now},
                false, '[]'::jsonb, false, {acknowledgedAt ?? "NULL"},
                {(receptionUnavailable ?? receptionUnavailableSince is not null).ToString().ToLowerInvariant()},
                {receptionUnavailableSince ?? "NULL"},
                NULL, 'Standalone', '{state}', {startedAt ?? "NULL"}, {recordingOutcome ?? "NULL"}, {Now})
            """);

        return id;
    }

    private static async Task<Guid> Rule(NpgsqlConnection connection)
    {
        var id = Guid.NewGuid();

        await Execute(
            connection,
            $"""
            INSERT INTO rule (id, name, query, priority, enabled, margin_before, margin_after, created_at)
            VALUES ('{id}', 'Drama', 'keyword=drama', 10, true, 10, 30, {Now})
            """);

        return id;
    }

    private static Task Record(
        NpgsqlConnection connection,
        Guid reservationId,
        string kind,
        string? tuneFailure = null,
        string? recordingOutcome = null)
        => Execute(
            connection,
            $"""
            INSERT INTO reservation_outcome (
                id, reservation_id, network_id, service_id, event_id, programme_start_at, snapshot_name,
                effective_start_at, effective_end_at, priority, rule_id, kind, tune_failure,
                recording_outcome, recorded_instead, occurred_at)
            VALUES (
                '{Guid.NewGuid()}', '{reservationId}', 70001, 1024, 4001, {Airs}, 'A programme',
                {Airs}, {Ends}, 10, NULL, '{kind}', {tuneFailure ?? "NULL"},
                {recordingOutcome ?? "NULL"}, '[]'::jsonb, {Now})
            """);

    private static Task Programme(NpgsqlConnection connection, int networkId, int eventId)
        => Execute(
            connection,
            $"""
            INSERT INTO programme (
                network_id, service_id, event_id, transport_stream_id, start_at, end_at,
                name, summary, is_shadow, genres, items, related, has_subtitles, source, updated_at)
            VALUES (
                {networkId}, 1024, {eventId}, 32736, {Airs}, {Ends},
                'A programme', 'What it is about', false, '[]'::jsonb, '[]'::jsonb, '[]'::jsonb,
                false, 'ScheduleBasic', {Now})
            """);

    private static async Task<string?> Composite(NpgsqlConnection connection, int networkId, int eventId)
        => await Scalar(
            connection,
            $"SELECT composite_state FROM reservation WHERE network_id = {networkId} AND event_id = {eventId}") as string;

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> Count(NpgsqlConnection connection, string from)
        => (long)(await Scalar(connection, $"SELECT count(*) FROM {from}"))!;

    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        object? read = await command.ExecuteScalarAsync();

        return read is DBNull ? null : read;
    }
}
