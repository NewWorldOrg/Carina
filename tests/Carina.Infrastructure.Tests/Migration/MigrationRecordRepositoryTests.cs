using Carina.Domain.Migration;
using Carina.Infrastructure.Migration;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Tests.Migration;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class MigrationRecordRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime At = new(2026, 9, 8, 3, 0, 0, DateTimeKind.Utc);

    private static readonly MigrationSourceName Source = new("the recording system being replaced");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ARunComesBackWithEverythingItSaidAndEverythingItLeftBehind()
    {
        await ClearAsync();

        MigrationRunId id = MigrationRunId.New();
        MigrationReport written = MigrationCensus.Taken(
            id,
            Source,
            MigrationPass.ForReal,
            Rolled(
                Offered(recordings: 2, files: 3),
                MigrationVerdict.Carry(MigrationPopulation.Recordings, "1", "a programme", 100, 100),
                MigrationVerdict.Refuse(
                    MigrationPopulation.Recordings,
                    "2",
                    MigrationRefusal.ReallyEmpty,
                    "another programme",
                    17_171_113_480,
                    0),
                MigrationVerdict.Carry(MigrationPopulation.RecordingFiles, "one.m2ts", "one.m2ts", 100, 100),
                MigrationVerdict.Refuse(
                    MigrationPopulation.RecordingFiles,
                    "two.m2ts",
                    MigrationRefusal.ReallyEmpty,
                    "two.m2ts",
                    17_171_113_480,
                    0),
                MigrationVerdict.Refuse(
                    MigrationPopulation.RecordingFiles,
                    "bash.sh",
                    MigrationRefusal.Orphan,
                    "bash.sh",
                    null,
                    539)),
            At,
            At.AddMinutes(4));

        await SaveAsync(written);

        MigrationReport read = Assert.IsType<MigrationReport>(await ReadAsync(id));

        Assert.Equal(id, read.Run.Id);
        Assert.Equal(Source, read.Run.Source);
        Assert.Equal(MigrationPass.ForReal, read.Run.Pass);
        Assert.Equal(At, read.Run.StartedAt);
        Assert.Equal(At.AddMinutes(4), read.Run.FinishedAt);

        Assert.Equal(MigrationPopulations.Counted.Count, read.Tallies.Count);
        Assert.All(read.Tallies, tally => Assert.Equal(0, tally.Unclassified));

        MigrationTally files = read.Tallies.Single(
            tally => tally.Population is MigrationPopulation.RecordingFiles);

        Assert.Equal(3, files.Offered);
        Assert.Equal(1, files.Carried);
        Assert.Equal(2, files.NotCarried);

        Assert.Equal(3, read.Details.Count);

        MigrationDetail orphan = read.Details.Single(
            detail => detail.Refusal is MigrationRefusal.Orphan);

        Assert.Equal("bash.sh", orphan.Subject);
        Assert.Null(orphan.Claimed);
        Assert.Equal(539, orphan.Observed);

        MigrationDetail empty = read.Details.Single(
            detail => detail.Population is MigrationPopulation.Recordings);

        Assert.Equal(MigrationRefusal.ReallyEmpty, empty.Refusal);
        Assert.Equal("another programme", empty.Note);
        Assert.Equal(17_171_113_480, empty.Claimed);
        Assert.Equal(0, empty.Observed);

        Assert.Equal(
            MigrationOmissionSubjects.All,
            read.Omissions.Select(omission => omission.Subject).Order());
    }

    [Fact]
    public async Task ARehearsalThatFoundNothingIsStillWrittenDownWithWhatItDidNotDo()
    {
        await ClearAsync();

        MigrationRunId id = MigrationRunId.New();

        await SaveAsync(MigrationCensus.Taken(
            id,
            Source,
            MigrationPass.Rehearsal,
            Rolled(Offered()),
            At,
            At));

        MigrationReport read = Assert.IsType<MigrationReport>(await ReadAsync(id));

        Assert.Empty(read.Details);
        Assert.Equal(4, read.Omissions.Count);
        Assert.Equal(MigrationPass.Rehearsal, read.Run.Pass);
    }

    [Fact]
    public async Task NothingHasBeenMigratedBeforeARunHasHappened()
    {
        await ClearAsync();

        Assert.Null(await LatestAsync());
    }

    [Fact]
    public async Task TheLatestRunIsTheOneThatFinishedLast()
    {
        await ClearAsync();

        MigrationRunId older = MigrationRunId.New();
        MigrationRunId newer = MigrationRunId.New();

        await SaveAsync(MigrationCensus.Taken(older, Source, MigrationPass.Rehearsal, Rolled(Offered()), At, At));
        await SaveAsync(MigrationCensus.Taken(
            newer,
            Source,
            MigrationPass.ForReal,
            Rolled(Offered()),
            At.AddHours(6),
            At.AddHours(6)));

        MigrationRun latest = Assert.IsType<MigrationRun>(await LatestAsync());

        Assert.Equal(newer, latest.Id);
        Assert.Equal(MigrationPass.ForReal, latest.Pass);
    }

    [Fact]
    public async Task OneRunsLinesNeverComeBackUnderAnother()
    {
        await ClearAsync();

        MigrationRunId mine = MigrationRunId.New();
        MigrationRunId theirs = MigrationRunId.New();

        await SaveAsync(MigrationCensus.Taken(
            mine,
            Source,
            MigrationPass.Rehearsal,
            Rolled(
                Offered(files: 1),
                MigrationVerdict.Refuse(
                    MigrationPopulation.RecordingFiles,
                    "mine.m2ts",
                    MigrationRefusal.Orphan,
                    "mine.m2ts",
                    null,
                    1)),
            At,
            At));

        await SaveAsync(MigrationCensus.Taken(
            theirs,
            Source,
            MigrationPass.Rehearsal,
            Rolled(
                Offered(files: 1),
                MigrationVerdict.Refuse(
                    MigrationPopulation.RecordingFiles,
                    "theirs.m2ts",
                    MigrationRefusal.Orphan,
                    "theirs.m2ts",
                    null,
                    1)),
            At,
            At));

        MigrationReport read = Assert.IsType<MigrationReport>(await ReadAsync(mine));

        Assert.Equal(["mine.m2ts"], read.Details.Select(detail => detail.Subject));
    }

    [Fact]
    public async Task ARunThatWasNeverMadeReadsBackAsNothing()
    {
        Assert.Null(await ReadAsync(MigrationRunId.New()));
    }

    [Fact]
    public async Task AReasonOutsideTheOnesTheRecordKnowsIsRefusedByTheDatabase()
    {
        MigrationRunId id = await AnEmptyRunAsync();

        await Assert.ThrowsAsync<PostgresException>(
            () => InsertDetailAsync(id, "'Recordings', 'Whatever'", "'7'"));
    }

    [Fact]
    public async Task ThatTheProgrammeGuideWasNotMigratedIsNotSomethingTheRunGetsToRestate()
    {
        MigrationRunId id = await AnEmptyRunAsync();

        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand writing = new(
            "UPDATE migration_omission SET ground = 'NothingToCarry' "
            + $"WHERE run_id = '{id.Value}' AND subject = 'ProgrammeGuide'",
            connection);

        await Assert.ThrowsAsync<PostgresException>(() => writing.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ASummaryOfTheProgrammeGuideIsRefusedByTheDatabase()
    {
        MigrationRunId id = await AnEmptyRunAsync();

        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand writing = new(
            "INSERT INTO migration_tally (run_id, population, offered, carried, not_carried, unclassified) "
            + $"VALUES ('{id.Value}', 'ProgrammeGuide', 0, 0, 0, 0)",
            connection);

        await Assert.ThrowsAsync<PostgresException>(() => writing.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ASummaryThatDoesNotAddUpIsRefusedByTheDatabase()
    {
        MigrationRunId id = await AnEmptyRunAsync();

        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand writing = new(
            "UPDATE migration_tally SET offered = 5 "
            + $"WHERE run_id = '{id.Value}' AND population = 'Recordings'",
            connection);

        await Assert.ThrowsAsync<PostgresException>(() => writing.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ALineBelongingToNoRunIsRefusedByTheDatabase()
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand writing = new(
            "INSERT INTO migration_detail (id, run_id, population, refusal, subject, note, claimed, observed) "
            + $"VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', 'Recordings', 'Orphan', '7', 'a programme', "
            + "NULL, NULL)",
            connection);

        await Assert.ThrowsAsync<PostgresException>(() => writing.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task TheSameSubjectIsNeverExplainedTwiceInOneRun()
    {
        MigrationRunId id = await AnEmptyRunAsync();

        await InsertDetailAsync(id, "'Recordings', 'Orphan'", "'7'");

        await Assert.ThrowsAsync<PostgresException>(
            () => InsertDetailAsync(id, "'Recordings', 'FileMissing'", "'7'"));
    }

    [Fact]
    public async Task WhatARunSaidGoesWithItWhenTheRunIsDropped()
    {
        MigrationRunId id = await AnEmptyRunAsync();
        await InsertDetailAsync(id, "'Recordings', 'Orphan'", "'7'");

        await using (NpgsqlConnection connection = await OpenAsync())
        {
            await using NpgsqlCommand dropping = new(
                $"DELETE FROM migration_run WHERE id = '{id.Value}'",
                connection);
            await dropping.ExecuteNonQueryAsync();
        }

        Assert.Equal(0L, await CountAsync($"SELECT count(*) FROM migration_detail WHERE run_id = '{id.Value}'"));
        Assert.Equal(0L, await CountAsync($"SELECT count(*) FROM migration_tally WHERE run_id = '{id.Value}'"));
        Assert.Equal(0L, await CountAsync($"SELECT count(*) FROM migration_omission WHERE run_id = '{id.Value}'"));
    }

    [Fact]
    public async Task ARunThatFinishesBeforeItStartsIsRefusedByTheDatabase()
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand writing = new(
            "INSERT INTO migration_run (id, source, pass, started_at, finished_at) VALUES "
            + $"('{Guid.NewGuid()}', 'a system', 'Rehearsal', timestamptz '2026-09-08 05:00:00+00', "
            + "timestamptz '2026-09-08 04:00:00+00')",
            connection);

        await Assert.ThrowsAsync<PostgresException>(() => writing.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task SavingNoReportAtAllIsRefused()
    {
        await using CarinaDbContext context = database.Open();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new MigrationRecordRepository(context).SaveAsync(null!, Cancel));
    }

    [Fact]
    public async Task ReadingNoRunAtAllIsRefused()
    {
        await using CarinaDbContext context = database.Open();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new MigrationRecordRepository(context).ReadAsync(null!, Cancel));
    }

    private static Dictionary<MigrationPopulation, int> Offered(int recordings = 0, int files = 0)
    {
        Dictionary<MigrationPopulation, int> offered = MigrationPopulations.Counted
            .ToDictionary(population => population, _ => 0);

        offered[MigrationPopulation.Recordings] = recordings;
        offered[MigrationPopulation.RecordingFiles] = files;

        return offered;
    }

    private static MigrationRoll Rolled(
        IReadOnlyDictionary<MigrationPopulation, int> offered,
        params MigrationVerdict[] verdicts)
        => MigrationRoll.Of(offered, verdicts);

    private async Task<MigrationRunId> AnEmptyRunAsync()
    {
        MigrationRunId id = MigrationRunId.New();

        await SaveAsync(MigrationCensus.Taken(id, Source, MigrationPass.Rehearsal, Rolled(Offered()), At, At));

        return id;
    }

    private async Task InsertDetailAsync(MigrationRunId id, string values, string subject)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand writing = new(
            "INSERT INTO migration_detail (id, run_id, population, refusal, subject, note, claimed, observed) "
            + $"VALUES ('{Guid.NewGuid()}', '{id.Value}', {values}, {subject}, 'a programme', NULL, NULL)",
            connection);

        await writing.ExecuteNonQueryAsync();
    }

    private async Task ClearAsync()
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand clearing = new("DELETE FROM migration_run", connection);
        await clearing.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(string sql)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand asking = new(sql, connection);

        return (long)(await asking.ExecuteScalarAsync())!;
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        await using CarinaDbContext context = database.Open();
        NpgsqlConnection connection = new(context.Database.GetConnectionString());
        await connection.OpenAsync();

        return connection;
    }

    private async Task SaveAsync(MigrationReport report)
    {
        await using CarinaDbContext context = database.Open();
        await new MigrationRecordRepository(context).SaveAsync(report, Cancel);
    }

    private async Task<MigrationRun?> LatestAsync()
    {
        await using CarinaDbContext context = database.Open();

        return await new MigrationRecordRepository(context).LatestAsync(Cancel);
    }

    private async Task<MigrationReport?> ReadAsync(MigrationRunId id)
    {
        await using CarinaDbContext context = database.Open();

        return await new MigrationRecordRepository(context).ReadAsync(id, Cancel);
    }
}
