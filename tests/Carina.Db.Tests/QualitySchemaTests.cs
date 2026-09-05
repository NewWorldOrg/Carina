using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class QualitySchemaTests(MigratedScratchDatabase database) : IClassFixture<MigratedScratchDatabase>
{
    private const string Taken = "timestamptz '2026-08-08 03:00:00+00'";

    private const string Later = "timestamptz '2026-08-08 03:05:00+00'";

    [Fact(DisplayName = "BR-QD-013: no quality table holds a foreign key into another domain's table")]
    public async Task NoQualityTableHoldsAForeignKeyIntoAnotherDomainsTable()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await using var asking = new NpgsqlCommand(
            """
            SELECT declaring.relname || ' -> ' || principal.relname
            FROM pg_constraint AS held
            JOIN pg_class AS declaring ON declaring.oid = held.conrelid
            JOIN pg_class AS principal ON principal.oid = held.confrelid
            WHERE held.contype = 'f' AND declaring.relname LIKE 'quality\_%'
            ORDER BY 1
            """,
            connection);

        List<string> pointing = [];
        await using NpgsqlDataReader reading = await asking.ExecuteReaderAsync();

        while (await reading.ReadAsync())
        {
            pointing.Add(reading.GetString(0));
        }

        Assert.Empty(pointing);
    }

    [Fact(DisplayName = "BR-QD-013: nothing anywhere holds a foreign key into a quality table either")]
    public async Task NothingAnywhereHoldsAForeignKeyIntoAQualityTable()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await using var asking = new NpgsqlCommand(
            """
            SELECT declaring.relname || ' -> ' || principal.relname
            FROM pg_constraint AS held
            JOIN pg_class AS declaring ON declaring.oid = held.conrelid
            JOIN pg_class AS principal ON principal.oid = held.confrelid
            WHERE held.contype = 'f' AND principal.relname LIKE 'quality\_%'
            ORDER BY 1
            """,
            connection);

        List<string> pointing = [];
        await using NpgsqlDataReader reading = await asking.ExecuteReaderAsync();

        while (await reading.ReadAsync())
        {
            pointing.Add(reading.GetString(0));
        }

        Assert.Empty(pointing);
    }

    [Fact(DisplayName = "BR-QD-004: a frontend that never locked cannot leave a carrier to noise figure behind")]
    public async Task AFrontendThatNeverLockedCannotLeaveACarrierToNoiseFigureBehind()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => SampleAsync(connection, locked: "false", cnr: "17", cnrReadAt: Taken));

        Assert.Equal("ck_quality_signal_sample_lock_gate", refusal.ConstraintName);
    }

    [Fact(DisplayName = "BR-QD-004: a locked frontend's figures are stored with the time each was read")]
    public async Task ALockedFrontendsFiguresAreStoredWithTheTimeEachWasRead()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await SampleAsync(connection, locked: "true", cnr: "33304", cnrReadAt: Taken);
    }

    [Fact(DisplayName = "BR-QV-003: a figure without the time it was read is refused")]
    public async Task AFigureWithoutTheTimeItWasReadIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => SampleAsync(connection, locked: "true", cnr: "33304", cnrReadAt: "NULL"));

        Assert.Equal("ck_quality_signal_sample_read_at", refusal.ConstraintName);
    }

    [Fact(DisplayName = "BR-QD-009: a sample keeps a count for each broadcast layer")]
    public async Task ASampleKeepsACountForEachBroadcastLayer()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await SampleAsync(
            connection,
            locked: "true",
            cnr: "33304",
            cnrReadAt: Taken,
            bitErrors: """'[{"Layer":0,"ErrorBits":3,"TotalBits":1671168},{"Layer":1,"ErrorBits":12,"TotalBits":67682304}]'::jsonb""",
            bitErrorsReadAt: Taken);

        await using var reading = new NpgsqlCommand(
            "SELECT jsonb_array_length(bit_errors) FROM quality_signal_sample WHERE bit_errors <> '[]'::jsonb",
            connection);

        Assert.Equal(2, (int)(await reading.ExecuteScalarAsync())!);
    }

    [Fact(DisplayName = "決定4: what a recording session measured has no home in this table")]
    public async Task WhatARecordingSessionMeasuredHasNoHomeInThisTable()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => MeasurementAsync(connection, purpose: "Recording"));

        Assert.Equal("ck_quality_session_measurement_purpose", refusal.ConstraintName);
    }

    [Fact(DisplayName = "BR-QD-001: an unmeasured session carries no counts that could be read as none lost")]
    public async Task AnUnmeasuredSessionCarriesNoCountsThatCouldBeReadAsNoneLost()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => MeasurementAsync(connection, measured: "false", dropped: "0", total: "0", measuredUpdatedAt: Later));

        Assert.Equal("ck_quality_session_measurement_counts", refusal.ConstraintName);
    }

    [Fact(DisplayName = "決定4: a session that is not a recording keeps what it measured after the session is gone")]
    public async Task ASessionThatIsNotARecordingKeepsWhatItMeasured()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await MeasurementAsync(connection, measured: "true", dropped: "2", total: "741375", measuredUpdatedAt: Later, endedAt: Later);
    }

    [Fact(DisplayName = "BR-QD-003: a threshold that no longer calls itself provisional stands on measurement")]
    public async Task AThresholdThatNoLongerCallsItselfProvisionalStandsOnMeasurement()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => ThresholdAsync(connection, "LockRate", provisional: "false", observations: "0"));

        Assert.Equal("ck_quality_threshold_standing", refusal.ConstraintName);
    }

    [Fact(DisplayName = "BR-QV-002: the threshold an incident was judged against is kept on the incident")]
    public async Task TheThresholdAnIncidentWasJudgedAgainstIsKeptOnTheIncident()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        string subject = Guid.NewGuid().ToString("N");
        await IncidentAsync(connection, state: "Detected", subject: subject);

        await using var reading = new NpgsqlCommand(
            $"SELECT applied_current, applied_provisional FROM quality_incident WHERE subject_key = '{subject}'",
            connection);

        await using NpgsqlDataReader row = await reading.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal(0.0002, row.GetDouble(0));
        Assert.True(row.GetBoolean(1));
    }

    [Theory]
    [InlineData("Notified", "NULL", "NULL", "NULL", "NULL")]
    [InlineData("Acknowledged", "NULL", Later, "'operator'", "NULL")]
    [InlineData("Resolved", "NULL", "NULL", "NULL", "NULL")]
    [InlineData("Detected", Later, "NULL", "NULL", "NULL")]
    [InlineData("Acknowledged", Later, Later, "NULL", "NULL")]
    public async Task AnIncidentStandsWhereItsOwnTimesPutIt(
        string state,
        string notifiedAt,
        string acknowledgedAt,
        string acknowledgedBy,
        string resolvedAt)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => IncidentAsync(connection, state, notifiedAt, acknowledgedAt, acknowledgedBy, resolvedAt));

        Assert.Equal("ck_quality_incident_lifecycle", refusal.ConstraintName);
    }

    [Fact(DisplayName = "BR-QD-002: an anomaly another domain owns is kept under that domain's own classification")]
    public async Task AnAnomalyAnotherDomainOwnsIsKeptUnderThatDomainsOwnClassification()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await IncidentAsync(connection, state: "Detected", owner: "Tuner", classification: "'NoLock'");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => IncidentAsync(connection, state: "Detected", owner: "Quality", classification: "'NoLock'"));

        Assert.Equal("ck_quality_incident_classification", refusal.ConstraintName);
    }

    [Fact(DisplayName = "BR-QS-003: the raw samples carry the index a retention sweep reads them by")]
    public async Task TheRawSamplesCarryTheIndexARetentionSweepReadsThemBy()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await using var reading = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_quality_signal_sample_taken_at'",
            connection);

        Assert.Contains("taken_at", (string)(await reading.ExecuteScalarAsync())!, StringComparison.Ordinal);
    }

    private static Task SampleAsync(
        NpgsqlConnection connection,
        string locked,
        string cnr,
        string cnrReadAt,
        string bitErrors = "'[]'::jsonb",
        string bitErrorsReadAt = "NULL")
        => new NpgsqlCommand(
            $"""
            INSERT INTO quality_signal_sample (
                driver_instance_id, session_id, taken_at, purpose, tuner_device_id, network_id, service_id,
                locked, lock_read_at, cnr_milli_decibels, cnr_read_at, bit_errors, bit_errors_read_at, metrics_not_read)
            VALUES (
                'driver-7', '{Guid.NewGuid():N}', {Taken}, 'Survey', 'adapter0', 32736, 1024,
                {locked}, {Taken}, {cnr}, {cnrReadAt}, {bitErrors}, {bitErrorsReadAt}, '[]'::jsonb)
            """,
            connection).ExecuteNonQueryAsync();

    private static Task MeasurementAsync(
        NpgsqlConnection connection,
        string purpose = "Survey",
        string measured = "false",
        string dropped = "NULL",
        string total = "NULL",
        string measuredUpdatedAt = "NULL",
        string endedAt = "NULL")
        => new NpgsqlCommand(
            $"""
            INSERT INTO quality_session_measurement (
                driver_instance_id, session_id, purpose, tuner_device_id, network_id, service_id,
                started_at, ended_at, cc_measured, cc_dropped_packets, cc_total_packets, eovf_count, measured_updated_at)
            VALUES (
                'driver-7', '{Guid.NewGuid():N}', '{purpose}', 'adapter0', 32736, 1024,
                {Taken}, {endedAt}, {measured}, {dropped}, {total}, 0, {measuredUpdatedAt})
            """,
            connection).ExecuteNonQueryAsync();

    private static Task ThresholdAsync(
        NpgsqlConnection connection,
        string key,
        string provisional,
        string observations)
        => new NpgsqlCommand(
            $"""
            INSERT INTO quality_threshold (
                threshold_key, default_value, current_value, provisional, observations, updated_at, updated_by)
            VALUES ('{key}', 0.0002, 0.0002, {provisional}, {observations}, {Taken}, NULL)
            """,
            connection).ExecuteNonQueryAsync();

    private static Task IncidentAsync(
        NpgsqlConnection connection,
        string state,
        string notifiedAt = "NULL",
        string acknowledgedAt = "NULL",
        string acknowledgedBy = "NULL",
        string resolvedAt = "NULL",
        string owner = "Quality",
        string classification = "NULL",
        string? subject = null)
        => new NpgsqlCommand(
            $"""
            INSERT INTO quality_incident (
                id, detected_at, breached, subject_kind, subject_key, observed, owner, classification,
                applied_default, applied_current, applied_provisional, applied_observations, applied_updated_at,
                state, notified_at, acknowledged_at, acknowledged_by, resolved_at)
            VALUES (
                '{Guid.NewGuid()}', {Taken}, 'PacketsLostWarning', 'Recording', '{subject ?? Guid.NewGuid().ToString("N")}', 0.004,
                '{owner}', {classification}, 0.0002, 0.0002, true, 0, {Taken},
                '{state}', {notifiedAt}, {acknowledgedAt}, {acknowledgedBy}, {resolvedAt})
            """,
            connection).ExecuteNonQueryAsync();
}
