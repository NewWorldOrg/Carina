using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class QualitySchemaTests(MigratedScratchDatabase database) : IClassFixture<MigratedScratchDatabase>
{
    private const string Taken = "timestamptz '2026-08-08 03:00:00+00'";

    private const string Later = "timestamptz '2026-08-08 03:05:00+00'";

    [Fact(DisplayName = "no quality table holds a foreign key into another domain's table")]
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

    [Fact(DisplayName = "nothing anywhere holds a foreign key into a quality table either")]
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

    [Fact(DisplayName = "a frontend that never locked cannot leave a carrier to noise figure behind")]
    public async Task AFrontendThatNeverLockedCannotLeaveACarrierToNoiseFigureBehind()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => SampleAsync(connection, locked: "false", cnr: "17", cnrReadAt: Taken));

        Assert.Equal("ck_quality_signal_sample_lock_gate", refusal.ConstraintName);
    }

    [Fact(DisplayName = "a locked frontend's figures are stored with the time each was read")]
    public async Task ALockedFrontendsFiguresAreStoredWithTheTimeEachWasRead()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await SampleAsync(connection, locked: "true", cnr: "29000", cnrReadAt: Taken);
    }

    [Fact(DisplayName = "a figure without the time it was read is refused")]
    public async Task AFigureWithoutTheTimeItWasReadIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => SampleAsync(connection, locked: "true", cnr: "29000", cnrReadAt: "NULL"));

        Assert.Equal("ck_quality_signal_sample_read_at", refusal.ConstraintName);
    }

    [Fact(DisplayName = "a sample keeps a count for each broadcast layer")]
    public async Task ASampleKeepsACountForEachBroadcastLayer()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await SampleAsync(
            connection,
            locked: "true",
            cnr: "29000",
            cnrReadAt: Taken,
            bitErrors: """'[{"Layer":0,"ErrorBits":3,"TotalBits":1600000},{"Layer":1,"ErrorBits":12,"TotalBits":64000000}]'::jsonb""",
            bitErrorsReadAt: Taken);

        await using var reading = new NpgsqlCommand(
            "SELECT jsonb_array_length(bit_errors) FROM quality_signal_sample WHERE bit_errors <> '[]'::jsonb",
            connection);

        Assert.Equal(2, (int)(await reading.ExecuteScalarAsync())!);
    }

    [Fact(DisplayName = "what a recording session measured has no home in this table")]
    public async Task WhatARecordingSessionMeasuredHasNoHomeInThisTable()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => MeasurementAsync(connection, purpose: "Recording"));

        Assert.Equal("ck_quality_session_measurement_purpose", refusal.ConstraintName);
    }

    [Fact(DisplayName = "an unmeasured session carries no counts that could be read as none lost")]
    public async Task AnUnmeasuredSessionCarriesNoCountsThatCouldBeReadAsNoneLost()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => MeasurementAsync(connection, measured: "false", dropped: "0", total: "0", measuredUpdatedAt: Later));

        Assert.Equal("ck_quality_session_measurement_counts", refusal.ConstraintName);
    }

    [Fact(DisplayName = "a session that is not a recording keeps what it measured after the session is gone")]
    public async Task ASessionThatIsNotARecordingKeepsWhatItMeasured()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        string session = Guid.NewGuid().ToString("N");

        await MeasurementAsync(
            connection,
            measured: "true",
            dropped: "2",
            total: "741375",
            measuredUpdatedAt: Later,
            endedAt: Later,
            session: session);

        await using var asking = new NpgsqlCommand(
            "SELECT purpose, ended_at, cc_dropped_packets, cc_total_packets FROM quality_session_measurement WHERE session_id = @session",
            connection);
        asking.Parameters.AddWithValue("session", session);

        await using NpgsqlDataReader reading = await asking.ExecuteReaderAsync();

        Assert.True(await reading.ReadAsync(), "the measurement of a session that has ended was not kept");
        Assert.Equal("Survey", reading.GetString(0));
        Assert.False(await reading.IsDBNullAsync(1));
        Assert.Equal(2, reading.GetInt64(2));
        Assert.Equal(741_375, reading.GetInt64(3));
        Assert.False(await reading.ReadAsync());
    }

    [Fact(DisplayName = "a threshold that no longer calls itself provisional stands on measurement")]
    public async Task AThresholdThatNoLongerCallsItselfProvisionalStandsOnMeasurement()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => ThresholdAsync(connection, "LockRate", provisional: "false", observations: "0"));

        Assert.Equal("ck_quality_threshold_standing", refusal.ConstraintName);
    }

    [Fact(DisplayName = "a threshold that moves leaves a record of what it moved from")]
    public async Task AThresholdThatMovesLeavesARecordOfWhatItMovedFrom()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        var id = Guid.NewGuid();
        await ThresholdChangeAsync(connection, id, "PacketsLostWarning");

        await using var reading = new NpgsqlCommand(
            $"SELECT previous_value, next_value, changed_by FROM quality_threshold_change WHERE id = '{id}'",
            connection);

        await using NpgsqlDataReader row = await reading.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal(0.0002, row.GetDouble(0));
        Assert.Equal(0.0005, row.GetDouble(1));
        Assert.True(await row.IsDBNullAsync(2));
    }

    [Fact(DisplayName = "a change under a key this domain does not name is refused")]
    public async Task AChangeUnderAKeyThisDomainDoesNotNameIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => ThresholdChangeAsync(connection, Guid.NewGuid(), "SomethingElse"));

        Assert.Equal("ck_quality_threshold_change_key", refusal.ConstraintName);
    }

    [Fact(DisplayName = "a threshold that moves does not rewrite what an incident was judged against")]
    public async Task AThresholdThatMovesDoesNotRewriteWhatAnIncidentWasJudgedAgainst()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        string subject = Guid.NewGuid().ToString("N");
        await IncidentAsync(connection, state: "Detected", subject: subject);
        await ThresholdAsync(connection, "PacketsLostWarning", provisional: "true", observations: "0");
        await new NpgsqlCommand(
            "UPDATE quality_threshold SET current_value = 0.5 WHERE threshold_key = 'PacketsLostWarning'",
            connection).ExecuteNonQueryAsync();

        await using var reading = new NpgsqlCommand(
            $"SELECT applied_current FROM quality_incident WHERE subject_key = '{subject}'",
            connection);

        await using NpgsqlDataReader row = await reading.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal(0.0002, row.GetDouble(0));
    }

    [Fact(DisplayName = "the threshold an incident was judged against is kept on the incident")]
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
    [InlineData("Notified", "NULL", "NULL")]
    [InlineData("Resolved", "NULL", "NULL")]
    [InlineData("Detected", Later, "NULL")]
    [InlineData("Notified", Later, Later)]
    public async Task AnIncidentStandsWhereItsOwnTimesPutIt(string state, string notifiedAt, string resolvedAt)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => IncidentAsync(connection, state, notifiedAt, resolvedAt));

        Assert.Equal("ck_quality_incident_lifecycle", refusal.ConstraintName);
    }

    [Fact(DisplayName = "an incident has no acknowledged state and nowhere to keep who acknowledged it")]
    public async Task AnIncidentHasNoAcknowledgedStateAndNowhereToKeepWhoAcknowledgedIt()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await using var columns = new NpgsqlCommand(
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = 'quality_incident' AND column_name LIKE 'acknowledged%'
            """,
            connection);

        Assert.Equal(0L, (long)(await columns.ExecuteScalarAsync())!);

        await using var constraints = new NpgsqlCommand(
            """
            SELECT string_agg(pg_get_constraintdef(held.oid), ' ')
            FROM pg_constraint AS held
            JOIN pg_class AS declaring ON declaring.oid = held.conrelid
            WHERE declaring.relname = 'quality_incident' AND held.contype = 'c'
            """,
            connection);

        string declared = (string)(await constraints.ExecuteScalarAsync())!;

        Assert.DoesNotContain("Acknowledged", declared, StringComparison.Ordinal);
        Assert.DoesNotContain("acknowledged", declared, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "an anomaly another domain owns is kept under that domain's own classification")]
    public async Task AnAnomalyAnotherDomainOwnsIsKeptUnderThatDomainsOwnClassification()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await IncidentAsync(connection, state: "Detected", owner: "Tuner", classification: "'NoLock'");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => IncidentAsync(connection, state: "Detected", owner: "Quality", classification: "'NoLock'"));

        Assert.Equal("ck_quality_incident_classification", refusal.ConstraintName);
    }

    [Fact(DisplayName = "a supply that went quiet names which of the four supplies it was")]
    public async Task ASupplyThatWentQuietNamesWhichOfTheFourSuppliesItWas()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await IncidentAsync(
            connection,
            state: "Detected",
            breached: "SupplySilence",
            silence: "'SignalSamples'");

        PostgresException nameless = await Assert.ThrowsAsync<PostgresException>(
            () => IncidentAsync(connection, state: "Detected", breached: "SupplySilence"));

        Assert.Equal("ck_quality_incident_silence", nameless.ConstraintName);

        PostgresException unasked = await Assert.ThrowsAsync<PostgresException>(
            () => IncidentAsync(connection, state: "Detected", silence: "'SignalSamples'"));

        Assert.Equal("ck_quality_incident_silence", unasked.ConstraintName);
    }

    [Fact(DisplayName = "the visit ledger is a subject of its own rather than one stream standing in for it")]
    public async Task TheVisitLedgerIsASubjectOfItsOwn()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await IncidentAsync(
            connection,
            state: "Detected",
            breached: "SupplySilence",
            silence: "'GuideVisits'",
            subjectKind: "Guide");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => IncidentAsync(connection, state: "Detected", subjectKind: "SomethingElse"));

        Assert.Equal("ck_quality_incident_vocabulary", refusal.ConstraintName);
    }

    [Fact(DisplayName = "a supply named outside this domain's vocabulary is refused")]
    public async Task ASupplyNamedOutsideThisDomainsVocabularyIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => IncidentAsync(
                connection,
                state: "Detected",
                breached: "SupplySilence",
                silence: "'SomethingElse'"));

        Assert.Equal("ck_quality_incident_silence", refusal.ConstraintName);
    }

    [Fact(DisplayName = "the lookup for what still stands reads the unsettled index")]
    public async Task TheLookupForWhatStillStandsReadsTheUnsettledIndex()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await new NpgsqlCommand("SET enable_seqscan = off", connection).ExecuteNonQueryAsync();

        await using var explaining = new NpgsqlCommand(
            """
            EXPLAIN SELECT id FROM quality_incident
            WHERE resolved_at IS NULL
            ORDER BY detected_at DESC
            """,
            connection);

        List<string> plan = [];
        await using (NpgsqlDataReader reading = await explaining.ExecuteReaderAsync())
        {
            while (await reading.ReadAsync())
            {
                plan.Add(reading.GetString(0));
            }
        }

        await new NpgsqlCommand("SET enable_seqscan = on", connection).ExecuteNonQueryAsync();

        Assert.Contains(plan, line => line.Contains("ix_quality_incident_unsettled", StringComparison.Ordinal));

        await using var filtering = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_quality_incident_unsettled'",
            connection);

        string declared = (string)(await filtering.ExecuteScalarAsync())!;

        Assert.Contains("resolved_at IS NULL", declared, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "the raw samples carry the index a retention sweep reads them by")]
    public async Task TheRawSamplesCarryTheIndexARetentionSweepReadsThemBy()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await using var reading = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_quality_signal_sample_taken_at'",
            connection);

        Assert.Contains("taken_at", (string)(await reading.ExecuteScalarAsync())!, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a reading that could not be taken is kept with the way it could not be")]
    public async Task AReadingThatCouldNotBeTakenIsKeptWithTheWayItCouldNotBe()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await NotTakenAsync(connection, "'NothingReported'");
    }

    [Fact(DisplayName = "a reading that could not be taken carries no figure that could be read as one")]
    public async Task AReadingThatCouldNotBeTakenCarriesNoFigureThatCouldBeReadAsOne()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => NotTakenAsync(connection, "'NothingReported'", locked: "true", cnr: "29000"));

        Assert.Equal("ck_quality_signal_sample_not_taken", refusal.ConstraintName);
    }

    [Fact(DisplayName = "a reading cannot fail for a reason this domain does not name")]
    public async Task AReadingCannotFailForAReasonThisDomainDoesNotName()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => NotTakenAsync(connection, "'SomethingElse'"));

        Assert.Equal("ck_quality_signal_sample_not_taken", refusal.ConstraintName);
    }

    [Fact(DisplayName = "a statistic the tuner does not keep is not the same as a reading that failed")]
    public async Task AStatisticTheTunerDoesNotKeepIsNotTheSameAsAReadingThatFailed()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => NotTakenAsync(connection, "'NothingReported'", metricsNotRead: """'["cnr"]'::jsonb"""));

        Assert.Equal("ck_quality_signal_sample_not_taken", refusal.ConstraintName);
    }

    private static Task SampleAsync(
        NpgsqlConnection connection,
        string locked,
        string cnr,
        string cnrReadAt,
        string bitErrors = "'[]'::jsonb",
        string bitErrorsReadAt = "NULL",
        string notTakenBecause = "NULL",
        string metricsNotRead = "'[]'::jsonb")
        => new NpgsqlCommand(
            $"""
            INSERT INTO quality_signal_sample (
                driver_instance_id, session_id, taken_at, purpose, tuner_device_id, network_id, service_id,
                locked, lock_read_at, cnr_milli_decibels, cnr_read_at, bit_errors, bit_errors_read_at,
                metrics_not_read, not_taken_because)
            VALUES (
                'driver-7', '{Guid.NewGuid():N}', {Taken}, 'Survey', 'adapter0', 32736, 1024,
                {locked}, {Taken}, {cnr}, {cnrReadAt}, {bitErrors}, {bitErrorsReadAt},
                {metricsNotRead}, {notTakenBecause})
            """,
            connection).ExecuteNonQueryAsync();

    private static Task NotTakenAsync(
        NpgsqlConnection connection,
        string because,
        string locked = "false",
        string cnr = "NULL",
        string metricsNotRead = "'[]'::jsonb")
        => SampleAsync(
            connection,
            locked,
            cnr,
            cnr is "NULL" ? "NULL" : Taken,
            notTakenBecause: because,
            metricsNotRead: metricsNotRead);

    private static Task MeasurementAsync(
        NpgsqlConnection connection,
        string purpose = "Survey",
        string measured = "false",
        string dropped = "NULL",
        string total = "NULL",
        string measuredUpdatedAt = "NULL",
        string endedAt = "NULL",
        string? session = null)
        => new NpgsqlCommand(
            $"""
            INSERT INTO quality_session_measurement (
                driver_instance_id, session_id, purpose, tuner_device_id, network_id, service_id,
                started_at, ended_at, cc_measured, cc_dropped_packets, cc_total_packets, eovf_count, measured_updated_at)
            VALUES (
                'driver-7', '{session ?? Guid.NewGuid().ToString("N")}', '{purpose}', 'adapter0', 32736, 1024,
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

    private static Task ThresholdChangeAsync(NpgsqlConnection connection, Guid id, string key)
        => new NpgsqlCommand(
            $"""
            INSERT INTO quality_threshold_change (
                id, threshold_key, previous_value, next_value, changed_at, changed_by)
            VALUES ('{id}', '{key}', 0.0002, 0.0005, {Taken}, NULL)
            """,
            connection).ExecuteNonQueryAsync();

    private static Task IncidentAsync(
        NpgsqlConnection connection,
        string state,
        string notifiedAt = "NULL",
        string resolvedAt = "NULL",
        string owner = "Quality",
        string classification = "NULL",
        string? subject = null,
        string breached = "PacketsLostWarning",
        string silence = "NULL",
        string subjectKind = "Recording")
        => new NpgsqlCommand(
            $"""
            INSERT INTO quality_incident (
                id, detected_at, breached, subject_kind, subject_key, observed, owner, classification, silence,
                applied_default, applied_current, applied_provisional, applied_observations, applied_updated_at,
                state, notified_at, resolved_at)
            VALUES (
                '{Guid.NewGuid()}', {Taken}, '{breached}', '{subjectKind}', '{subject ?? Guid.NewGuid().ToString("N")}', 0.004,
                '{owner}', {classification}, {silence}, 0.0002, 0.0002, true, 0, {Taken},
                '{state}', {notifiedAt}, {resolvedAt})
            """,
            connection).ExecuteNonQueryAsync();
}
