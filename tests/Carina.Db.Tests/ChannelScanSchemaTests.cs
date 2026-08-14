using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ChannelScanSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Now = "timestamptz '2026-08-14 00:00:00+00'";

    [Fact]
    public async Task TheDatabaseRefusesASecondSelectedCandidateForTheSameService()
    {
        await using var connection = await database.OpenAsync();
        await Service(connection, 60001, 1);
        await Candidate(connection, 60001, 1, 27, selected: true);

        var refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Candidate(connection, 60001, 1, 28, selected: true));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refusal.SqlState);
        Assert.Equal("ux_candidate_channel_selected", refusal.ConstraintName);
    }

    [Fact]
    public async Task EachServiceSelectsOneOfItsOwnCandidates()
    {
        await using var connection = await database.OpenAsync();
        await Service(connection, 60002, 1);
        await Service(connection, 60002, 2);

        await Candidate(connection, 60002, 1, 27, selected: true);
        await Candidate(connection, 60002, 2, 27, selected: true);

        Assert.Equal(2, await Count(connection, "candidate_channel WHERE network_id = 60002 AND is_selected"));
    }

    [Fact]
    public async Task NoSelectedCandidateAtAllIsAStateTheDatabaseKeeps()
    {
        await using var connection = await database.OpenAsync();
        await Service(connection, 60003, 1);

        await Candidate(connection, 60003, 1, 27, selected: false);
        await Candidate(connection, 60003, 1, 28, selected: false);

        Assert.Equal(0, await Count(connection, "candidate_channel WHERE network_id = 60003 AND is_selected"));
    }

    [Fact]
    public async Task TheSameWayOfReachingAServiceCannotBeRecordedTwice()
    {
        await using var connection = await database.OpenAsync();
        await Service(connection, 60004, 1);
        await Candidate(connection, 60004, 1, 27, selected: false);

        var refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Candidate(connection, 60004, 1, 27, selected: false));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refusal.SqlState);
        Assert.Equal("ux_candidate_channel_identity", refusal.ConstraintName);
    }

    [Fact]
    public async Task ADefinitionOutsideWhatCanBeReceivedIsRefused()
    {
        await using var connection = await database.OpenAsync();
        await Service(connection, 60005, 1);

        foreach (var (system, channel, transportStreamId) in new[]
                 {
                     ("IsdbT", 12, "NULL"),
                     ("IsdbT", 63, "NULL"),
                     ("IsdbSBs", 7, "40000"),
                     ("IsdbSBs", 17, "40000"),
                     ("IsdbSBs", 2, "40000"),
                     ("IsdbSBs", 3, "NULL"),
                     ("IsdbSCs110", 3, "NULL"),
                     ("IsdbSCs110", 26, "NULL"),
                     ("IsdbSky", 1, "NULL"),
                 })
        {
            var refusal = await Assert.ThrowsAsync<PostgresException>(
                () => Candidate(connection, 60005, 1, channel, false, system, transportStreamId));

            Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
            Assert.Equal("ck_candidate_channel_tuning", refusal.ConstraintName);
        }
    }

    [Fact]
    public async Task AReadingWithoutLockCannotCarryAQualityFigure()
    {
        await using var connection = await database.OpenAsync();
        await Service(connection, 60006, 1);

        var refusal = await Assert.ThrowsAsync<PostgresException>(() => Execute(
            connection,
            $"""
             INSERT INTO candidate_channel
                 (id, network_id, service_id, tune_system, physical_channel, is_selected,
                  needs_revalidation, rotation_state, consecutive_failures, discovered_at, last_seen_at,
                  measured_at, locked, cnr_milli_decibels)
             VALUES
                 (gen_random_uuid(), 60006, 1, 'IsdbT', 27, false,
                  false, 'Active', 0, {Now}, {Now},
                  {Now}, false, 21000)
             """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_candidate_channel_measurement_lock", refusal.ConstraintName);
    }

    [Fact]
    public async Task EditingAServiceLeavesItsCandidatesToTheServiceAlone()
    {
        await using var connection = await database.OpenAsync();
        await Service(connection, 60007, 1);
        await Candidate(connection, 60007, 1, 27, selected: true);

        await Execute(
            connection,
            $"UPDATE broadcast_service SET name = 'Renamed', last_seen_at = {Now} WHERE network_id = 60007");

        Assert.Equal(1, await Count(connection, "candidate_channel WHERE network_id = 60007"));
    }

    [Fact]
    public async Task TheDatabaseRefusesASecondRunningScan()
    {
        await using var connection = await database.OpenAsync();
        await Execute(connection, "DELETE FROM scan_run");
        await ScanRun(connection, "Running");

        var refusal = await Assert.ThrowsAsync<PostgresException>(() => ScanRun(connection, "Running"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refusal.SqlState);
        Assert.Equal("ux_scan_run_running", refusal.ConstraintName);
    }

    [Fact]
    public async Task AScanThatEndedDoesNotStandInTheWayOfTheNextOne()
    {
        await using var connection = await database.OpenAsync();
        await Execute(connection, "DELETE FROM scan_run");

        await ScanRun(connection, "Completed");
        await ScanRun(connection, "Cancelled", "the operator asked for it");
        await ScanRun(connection, "Failed", "every tuner was busy for longer than the bounded wait");
        await ScanRun(connection, "Interrupted");
        await ScanRun(connection, "Running");

        Assert.Equal(5, await Count(connection, "scan_run"));
    }

    [Fact]
    public async Task AScanThatFailedOrWasCancelledWithoutAReasonIsRefused()
    {
        await using var connection = await database.OpenAsync();
        await Execute(connection, "DELETE FROM scan_run");

        foreach (var state in new[] { "Failed", "Cancelled" })
        {
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => ScanRun(connection, state));

            Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
            Assert.Equal("ck_scan_run_reason", refusal.ConstraintName);
        }
    }

    [Fact]
    public async Task AStateNoScanCanBeInIsRefused()
    {
        await using var connection = await database.OpenAsync();

        var refusal = await Assert.ThrowsAsync<PostgresException>(
            () => ScanRun(connection, "Draining", "a state this domain does not have"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_scan_run_state", refusal.ConstraintName);
    }

    [Fact]
    public async Task EachWayOfFailingAnAttemptIsAValueOfItsOwn()
    {
        await using var connection = await database.OpenAsync();
        var run = await ScanRun(connection, "Completed");

        foreach (var outcome in new[]
                 {
                     "Succeeded", "NoLock", "LockedWithoutData", "IncompleteTables", "UnexpectedStream",
                 })
        {
            await Attempt(connection, run, outcome);
        }

        Assert.Equal(
            5,
            await Count(connection, $"scan_run_attempt WHERE scan_run_id = '{run}' AND outcome IS NOT NULL"));

        var refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Attempt(connection, run, "did not lock"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_scan_run_attempt_outcome", refusal.ConstraintName);
    }

    [Fact]
    public async Task AnAttemptOutlivesTheCandidateItWasMadeFor()
    {
        await using var connection = await database.OpenAsync();
        await Service(connection, 60008, 1);
        await Candidate(connection, 60008, 1, 27, selected: false);
        var run = await ScanRun(connection, "Completed");
        await Attempt(connection, run, "NoLock");

        await Execute(connection, "DELETE FROM broadcast_service WHERE network_id = 60008");

        Assert.Equal(0, await Count(connection, "candidate_channel WHERE network_id = 60008"));
        Assert.Equal(1, await Count(connection, $"scan_run_attempt WHERE scan_run_id = '{run}'"));
    }

    [Fact]
    public async Task TheSeedNamesTheFirstTransportStreamOfEveryBsSlotThatDemodulates()
    {
        await using var connection = await database.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT bs_channel, relative_stream_number, transport_stream_id FROM satellite_transport_stream ORDER BY bs_channel",
            connection);
        await using var reader = await command.ExecuteReaderAsync();

        var slots = new List<int>();
        while (await reader.ReadAsync())
        {
            var bsChannel = reader.GetInt32(0);
            slots.Add(bsChannel);

            Assert.Equal(0, reader.GetInt32(1));
            Assert.Equal(0x4000 | (bsChannel << 4), reader.GetInt32(2));
        }

        Assert.Equal([1, 3, 5, 9, 11, 13, 15, 19, 21, 23], slots);
    }

    private static Task Service(NpgsqlConnection connection, int networkId, int serviceId)
        => Execute(
            connection,
            $"""
             INSERT INTO broadcast_service (network_id, service_id, name, category, discovered_at, last_seen_at)
             VALUES ({networkId}, {serviceId}, 'Fixture Service', 'Television', {Now}, {Now})
             """);

    private static Task Candidate(
        NpgsqlConnection connection,
        int networkId,
        int serviceId,
        int physicalChannel,
        bool selected,
        string tuneSystem = "IsdbT",
        string transportStreamId = "NULL")
        => Execute(
            connection,
            $"""
             INSERT INTO candidate_channel
                 (id, network_id, service_id, tune_system, physical_channel, transport_stream_id, is_selected,
                  selection_source, selected_at, needs_revalidation, rotation_state, consecutive_failures,
                  discovered_at, last_seen_at)
             VALUES
                 (gen_random_uuid(), {networkId}, {serviceId}, '{tuneSystem}', {physicalChannel},
                  {transportStreamId}, {(selected ? "true" : "false")},
                  {(selected ? "'Manual'" : "NULL")}, {(selected ? Now : "NULL")}, false, 'Active', 0,
                  {Now}, {Now})
             """);

    private static async Task<Guid> ScanRun(NpgsqlConnection connection, string state, string? reason = null)
    {
        var id = Guid.NewGuid();
        var finished = state == "Running" ? "NULL" : Now;

        await Execute(
            connection,
            $"""
             INSERT INTO scan_run (id, state, driver_instance_id, started_at, finished_at, reason)
             VALUES ('{id}', '{state}', 'instance-a', {Now}, {finished},
                     {(reason is null ? "NULL" : $"'{reason}'")})
             """);

        return id;
    }

    private static Task Attempt(NpgsqlConnection connection, Guid scanRunId, string outcome)
        => Execute(
            connection,
            $"""
             INSERT INTO scan_run_attempt
                 (id, scan_run_id, tune_system, physical_channel, outcome, started_at, finished_at)
             VALUES (gen_random_uuid(), '{scanRunId}', 'IsdbT', 27, '{outcome}', {Now}, {Now})
             """);

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> Count(NpgsqlConnection connection, string from)
    {
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {from}", connection);

        return (long)(await command.ExecuteScalarAsync())!;
    }
}
