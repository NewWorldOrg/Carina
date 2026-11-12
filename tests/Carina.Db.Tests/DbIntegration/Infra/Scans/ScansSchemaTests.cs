using Npgsql;

namespace Carina.Db.Tests.DbIntegration.Infra.Scans;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ScansSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Now = "timestamptz '2026-08-14 00:00:00+00'";

    [Fact]
    public async Task TheDatabaseRefusesASecondRunningScan()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Execute(connection, "DELETE FROM scan_run");
        await ScanRun(connection, "Running");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => ScanRun(connection, "Running"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refusal.SqlState);
        Assert.Equal("ux_scan_run_running", refusal.ConstraintName);
    }

    [Fact]
    public async Task AScanThatEndedDoesNotStandInTheWayOfTheNextOne()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
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
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Execute(connection, "DELETE FROM scan_run");

        foreach (string? state in new[] { "Failed", "Cancelled" })
        {
            PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => ScanRun(connection, state));

            Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
            Assert.Equal("ck_scan_run_reason", refusal.ConstraintName);
        }
    }

    [Fact]
    public async Task AStateNoScanCanBeInIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => ScanRun(connection, "Draining", "a state this domain does not have"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_scan_run_state", refusal.ConstraintName);
    }

    [Fact]
    public async Task EachWayOfFailingAnAttemptIsAValueOfItsOwn()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await ScanRun(connection, "Completed");

        foreach (string? outcome in new[]
                 {
                     "Succeeded", "NoLock", "LockedWithoutData", "IncompleteTables", "UnexpectedStream",
                 })
        {
            await Attempt(connection, run, outcome);
        }

        Assert.Equal(
            5,
            await Count(connection, $"scan_run_attempt WHERE scan_run_id = '{run}' AND outcome IS NOT NULL"));

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Attempt(connection, run, "did not lock"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_scan_run_attempt_outcome", refusal.ConstraintName);
    }

    [Fact]
    public async Task AnAttemptOutlivesTheCandidateItWasMadeFor()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Service(connection, 60008, 1);
        await Candidate(connection, 60008, 1, 27, selected: false);
        Guid run = await ScanRun(connection, "Completed");
        await Attempt(connection, run, "NoLock");

        await Execute(connection, "DELETE FROM broadcast_service WHERE network_id = 60008");

        Assert.Equal(0, await Count(connection, "candidate_channel WHERE network_id = 60008"));
        Assert.Equal(1, await Count(connection, $"scan_run_attempt WHERE scan_run_id = '{run}'"));
    }

    private static Task Service(NpgsqlConnection connection, int networkId, int serviceId)
        => Execute(
            connection,
            $"""
             INSERT INTO broadcast_service
                 (network_id, service_id, name, category, logo_declaration, discovered_at, last_seen_at)
             VALUES ({networkId}, {serviceId}, 'Fixture Service', 'Television', 'NotYetRead', {Now}, {Now})
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
        string finished = state == "Running" ? "NULL" : Now;

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
