using Carina.Domain.Migration;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class MigrationRecordSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Began = "timestamptz '2026-09-08 03:00:00+00'";

    private const string Ended = "timestamptz '2026-09-08 03:04:00+00'";

    public static TheoryData<string> Refusals => Named(MigrationRefusals.All.Select(refusal => refusal.ToString()));

    public static TheoryData<string> Counted =>
        Named(MigrationPopulations.Counted.Select(population => population.ToString()));

    public static TheoryData<string> Standings =>
        Named(MigrationChannelStandings.All.Select(standing => standing.ToString()));

    public static TheoryData<string> LeftAlone =>
        Named(MigrationOmissionSubjects.All.Select(subject => subject.ToString()));

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task EveryReasonTheApplicationCanNameIsOneTheTableTakes(string refusal)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await RunAsync(connection);

        await DetailAsync(connection, run, "Recordings", refusal, $"'{refusal}'");

        Assert.Equal(
            1L,
            await CountAsync(connection, $"SELECT count(*) FROM migration_detail WHERE run_id = '{run}'"));
    }

    [Fact]
    public async Task AReasonTheApplicationCannotNameIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await RunAsync(connection);

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => DetailAsync(connection, run, "Recordings", "Whatever", "'7'"));

        Assert.Equal("ck_migration_detail_refusal", refused.ConstraintName);
    }

    [Theory]
    [MemberData(nameof(Counted))]
    public async Task EveryPopulationTheSummaryCountsIsOneTheTableTakes(string population)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await RunAsync(connection);

        await TallyAsync(connection, run, population, "1, 1, 0, 0");

        Assert.Equal(
            1L,
            await CountAsync(
                connection,
                $"SELECT count(*) FROM migration_tally WHERE run_id = '{run}' AND population = '{population}'"));
    }

    [Fact]
    public async Task TheProgrammeGuideIsNeverASummaryRow()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await RunAsync(connection);

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => TallyAsync(connection, run, "ProgrammeGuide", "0, 0, 0, 0"));

        Assert.Equal("ck_migration_tally_population", refused.ConstraintName);
    }

    [Theory]
    [InlineData("2, 1, 0, 0")]
    [InlineData("1, 1, 1, 0")]
    [InlineData("-1, 0, 0, 0")]
    [InlineData("0, -1, 1, 0")]
    public async Task ASummaryThatDoesNotAccountForEveryElementIsRefused(string counts)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await RunAsync(connection);

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => TallyAsync(connection, run, "Recordings", counts));

        Assert.Equal("ck_migration_tally_counts", refused.ConstraintName);
    }

    [Fact]
    public async Task ASummaryMayLeaveSomethingUnclassifiedOnlyByCountingIt()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await RunAsync(connection);

        await TallyAsync(connection, run, "Recordings", "3, 1, 1, 1");

        Assert.Equal(
            1L,
            await CountAsync(
                connection,
                $"SELECT count(*) FROM migration_tally WHERE run_id = '{run}' AND unclassified = 1"));
    }

    [Theory]
    [MemberData(nameof(LeftAlone))]
    public async Task EveryThingLeftAloneIsWrittenDownWithTheGroundTheRequirementsGive(string subject)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await RunAsync(connection);

        MigrationOmissionSubject named = Enum.Parse<MigrationOmissionSubject>(subject);

        await OmissionAsync(
            connection,
            run,
            subject,
            MigrationOmission.GroundOf(named).ToString(),
            MigrationOmissionSubjects.CountsRows(named) ? "17" : "NULL");

        Assert.Equal(
            1L,
            await CountAsync(
                connection,
                $"SELECT count(*) FROM migration_omission WHERE run_id = '{run}' AND subject = '{subject}'"));
    }

    [Fact]
    public async Task WhatALineOfTheRecordCountsIsSettledByTheRequirementsAndNotByTheRun()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await RunAsync(connection);

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => OmissionAsync(connection, run, "ProgrammeGuide", "NotMigratedByDesign", "17"));

        Assert.Equal("ck_migration_omission_affected", refused.ConstraintName);
    }

    [Fact]
    public async Task ARowThatShouldSayHowManyItTouchedCannotStaySilentAboutIt()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await RunAsync(connection);

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => OmissionAsync(connection, run, "EnclosedCharacters", "NotMigratedByDesign", "NULL"));

        Assert.Equal("ck_migration_omission_affected", refused.ConstraintName);
    }

    [Fact]
    public async Task TheGroundForLeavingSomethingAloneIsNotTheRunsToChoose()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await RunAsync(connection);

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => OmissionAsync(connection, run, "ProgrammeGuide", "NothingToCarry"));

        Assert.Equal("ck_migration_omission_ground", refused.ConstraintName);
    }

    [Fact]
    public async Task ARunIsEitherARehearsalOrTheRealThing()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => RunAsync(connection, "Whatever"));

        Assert.Equal("ck_migration_run_pass", refused.ConstraintName);
    }

    [Fact]
    public async Task TheSameSubjectIsNeverExplainedTwiceInOneRun()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await RunAsync(connection);

        await DetailAsync(connection, run, "Recordings", "Orphan", "'7'");

        await Assert.ThrowsAsync<PostgresException>(
            () => DetailAsync(connection, run, "Recordings", "FileMissing", "'7'"));
    }

    [Fact]
    public async Task TheSameSubjectInAnotherPopulationIsItsOwnLine()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await RunAsync(connection);

        await DetailAsync(connection, run, "Recordings", "Orphan", "'7'");
        await DetailAsync(connection, run, "Rules", "Orphan", "'7'");

        Assert.Equal(
            2L,
            await CountAsync(connection, $"SELECT count(*) FROM migration_detail WHERE run_id = '{run}'"));
    }

    private static async Task ProposalAsync(
        NpgsqlConnection connection,
        Guid run,
        string standing,
        string rescannedName,
        int service = 1024)
    {
        await using NpgsqlCommand writing = new(
            "INSERT INTO migration_channel_proposal "
            + "(run_id, network_id, service_id, standing, source_name, source_physical_channel, rescanned_name) "
            + $"VALUES ('{run}', 32736, {service}, '{standing}', 'an old name', '21', {rescannedName})",
            connection);

        await writing.ExecuteNonQueryAsync();
    }

    [Theory]
    [MemberData(nameof(Standings))]
    public async Task EveryStandingAChannelDefinitionCanEndUpInIsOneTheTableTakes(string standing)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await RunAsync(connection);

        await ProposalAsync(
            connection,
            run,
            standing,
            standing is "NameProposed" ? "'the rescanned name'" : "NULL");

        Assert.Equal(
            1L,
            await CountAsync(
                connection,
                $"SELECT count(*) FROM migration_channel_proposal WHERE run_id = '{run}'"));
    }

    [Fact]
    public async Task ANameIsProposedOnlyForAServiceTheRescanAnsweredFor()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await RunAsync(connection);

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => ProposalAsync(connection, run, "NothingAnswers", "'the rescanned name'"));

        Assert.Equal("ck_migration_channel_proposal_name", refused.ConstraintName);
    }

    [Fact]
    public async Task WhatTheSourceMeantByARuleIsWrittenDownEvenWhenNoRuleWasMade()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid run = await RunAsync(connection);

        await using NpgsqlCommand writing = new(
            "INSERT INTO migration_rule_proposal (run_id, source_row, rule_id, enabled_at_the_source) "
            + $"VALUES ('{run}', 3, NULL, true)",
            connection);

        await writing.ExecuteNonQueryAsync();

        Assert.Equal(
            1L,
            await CountAsync(
                connection,
                $"SELECT count(*) FROM migration_rule_proposal WHERE run_id = '{run}' AND rule_id IS NULL"));
    }

    private static TheoryData<string> Named(IEnumerable<string> names)
    {
        TheoryData<string> data = [];

        foreach (string name in names)
        {
            data.Add(name);
        }

        return data;
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand asking = new(sql, connection);

        return (long)(await asking.ExecuteScalarAsync())!;
    }

    private static async Task<Guid> RunAsync(NpgsqlConnection connection, string pass = "Rehearsal")
    {
        Guid id = Guid.NewGuid();

        await using NpgsqlCommand writing = new(
            "INSERT INTO migration_run (id, source, pass, started_at, finished_at) "
            + $"VALUES ('{id}', 'a system', '{pass}', {Began}, {Ended})",
            connection);

        await writing.ExecuteNonQueryAsync();

        return id;
    }

    private static async Task TallyAsync(NpgsqlConnection connection, Guid run, string population, string counts)
    {
        await using NpgsqlCommand writing = new(
            "INSERT INTO migration_tally (run_id, population, offered, carried, not_carried, unclassified) "
            + $"VALUES ('{run}', '{population}', {counts})",
            connection);

        await writing.ExecuteNonQueryAsync();
    }

    private static async Task DetailAsync(
        NpgsqlConnection connection,
        Guid run,
        string population,
        string refusal,
        string subject)
    {
        await using NpgsqlCommand writing = new(
            "INSERT INTO migration_detail (id, run_id, population, refusal, subject, note, claimed, observed) "
            + $"VALUES ('{Guid.NewGuid()}', '{run}', '{population}', '{refusal}', {subject}, 'a programme', "
            + "NULL, NULL)",
            connection);

        await writing.ExecuteNonQueryAsync();
    }

    private static async Task OmissionAsync(
        NpgsqlConnection connection,
        Guid run,
        string subject,
        string ground,
        string affected = "DEFAULT")
    {
        await using NpgsqlCommand writing = new(
            "INSERT INTO migration_omission (run_id, subject, ground, affected) "
            + $"VALUES ('{run}', '{subject}', '{ground}', {affected})",
            connection);

        await writing.ExecuteNonQueryAsync();
    }
}
