using Carina.Domain.Base;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Integrity;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Tests.Integrity;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class IntegrityCheckRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime At = new(2026, 8, 26, 5, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly RecordingFileName Name = new("one.m2ts");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static RecordingId Id(int seed) => new(new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 2]));

    [Fact]
    public async Task ACheckAndEveryClassOfFindingComeBackTheWayTheyWereWritten()
    {
        await ClearAsync();

        IntegrityCheckId id = IntegrityCheckId.New();
        IntegrityCheck check = IntegrityCheck.Rehydrate(id, At, At.AddSeconds(3), 2, 1, 6, 5, 4, 1, 1);
        IReadOnlyList<IntegrityFinding> written =
        [
            IntegrityFinding.SizeDisagrees(id, Primary, Id(1), Name, 100, 99, At),
            IntegrityFinding.NoLedgerRow(id, Primary, "nested/stray.m2ts", 512, At),
            IntegrityFinding.FileMissing(id, Primary, Id(3), Name, 100, At),
            IntegrityFinding.FileEmpty(id, Primary, Id(4), Name, 100, 0, At),
            IntegrityFinding.EmptyThoughComplete(id, Primary, Id(5), Name, 100, 0, At),
        ];

        await SaveAsync(IntegrityReport.Of(check, written));

        IntegrityCheck read = Assert.IsType<IntegrityCheck>(await LatestAsync());

        Assert.Equal(id, read.Id);
        Assert.Equal(At, read.StartedAt);
        Assert.Equal(At.AddSeconds(3), read.FinishedAt);
        Assert.Equal(2, read.RootsWalked);
        Assert.Equal(1, read.RootsOutOfReach);
        Assert.Equal(6, read.FilesRead);
        Assert.Equal(5, read.LedgerRowsRead);
        Assert.Equal(4, read.LedgerRowsJudged);
        Assert.Equal(1, read.LedgerRowsStillWriting);
        Assert.Equal(1, read.LedgerRowsInRootsOutOfReach);

        IReadOnlyList<IntegrityFinding> back = await FindingsAsync(id);

        Assert.Equal(
            ["EmptyThoughComplete", "FileEmpty", "FileMissing", "NoLedgerRow", "SizeDisagrees"],
            back.Select(finding => finding.Fault.ToString()).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            written.Select(finding => finding.Id.Value).Order().ToArray(),
            back.Select(finding => finding.Id.Value).Order().ToArray());

        IntegrityFinding orphan = back.Single(finding => finding.Fault is IntegrityFault.NoLedgerRow);

        Assert.Equal("nested/stray.m2ts", orphan.Path);
        Assert.Null(orphan.RecordingId);
        Assert.Null(orphan.LedgerSize);
        Assert.Equal(512, orphan.ObservedSize);
        Assert.Equal(At, orphan.NoticedAt);
        Assert.Equal("primary", orphan.Root.Value);

        IntegrityFinding missing = back.Single(finding => finding.Fault is IntegrityFault.FileMissing);

        Assert.Equal(Id(3), missing.RecordingId);
        Assert.Equal(100, missing.LedgerSize);
        Assert.Null(missing.ObservedSize);
    }

    [Fact]
    public async Task ACheckThatFoundNothingIsStillWrittenDown()
    {
        IntegrityCheckId id = IntegrityCheckId.New();

        await SaveAsync(IntegrityReport.Of(
            IntegrityCheck.Rehydrate(id, At, At.AddSeconds(1), 1, 0, 3, 3, 3, 0, 0),
            []));

        Assert.Empty(await FindingsAsync(id));
        Assert.Equal(3, (await FindByIdAsync(id)).FilesRead);
    }

    [Fact]
    public async Task ThereIsNoLatestCheckBeforeOneHasRun()
    {
        await ClearAsync();

        Assert.Null(await LatestAsync());
    }

    [Fact]
    public async Task TheLatestCheckIsTheOneThatFinishedLast()
    {
        await ClearAsync();

        IntegrityCheckId older = IntegrityCheckId.New();
        IntegrityCheckId newer = IntegrityCheckId.New();

        await SaveAsync(IntegrityReport.Of(
            IntegrityCheck.Rehydrate(older, At, At.AddSeconds(1), 1, 0, 0, 0, 0, 0, 0),
            []));
        await SaveAsync(IntegrityReport.Of(
            IntegrityCheck.Rehydrate(newer, At.AddHours(6), At.AddHours(6).AddSeconds(1), 9, 0, 0, 0, 0, 0, 0),
            []));

        IntegrityCheck latest = Assert.IsType<IntegrityCheck>(await LatestAsync());

        Assert.Equal(newer, latest.Id);
        Assert.Equal(9, latest.RootsWalked);
    }

    [Fact]
    public async Task OneChecksFindingsNeverComeBackUnderAnother()
    {
        IntegrityCheckId mine = IntegrityCheckId.New();
        IntegrityCheckId theirs = IntegrityCheckId.New();

        await SaveAsync(IntegrityReport.Of(
            IntegrityCheck.Rehydrate(mine, At, At, 0, 0, 0, 0, 0, 0, 0),
            [IntegrityFinding.NoLedgerRow(mine, Primary, "mine.m2ts", 1, At)]));
        await SaveAsync(IntegrityReport.Of(
            IntegrityCheck.Rehydrate(theirs, At, At, 0, 0, 0, 0, 0, 0, 0),
            [IntegrityFinding.NoLedgerRow(theirs, Primary, "theirs.m2ts", 1, At)]));

        Assert.Equal(["mine.m2ts"], (await FindingsAsync(mine)).Select(finding => finding.Path).ToArray());
    }

    [Fact]
    public async Task AFindingBelongingToNoCheckIsRefusedByTheDatabase()
    {
        IntegrityCheckId nobody = IntegrityCheckId.New();

        await using NpgsqlConnection connection = await OpenAsync();
        await using var writing = new NpgsqlCommand(
            "INSERT INTO integrity_finding "
            + "(id, check_id, fault, output_root, path, recording_id, ledger_size, observed_size, noticed_at) "
            + $"VALUES ('{Guid.NewGuid()}', '{nobody.Value}', 'NoLedgerRow', 'primary', 'a.m2ts', "
            + "NULL, NULL, 1, timestamptz '2026-08-26 05:00:00+00')",
            connection);

        await Assert.ThrowsAsync<PostgresException>(() => writing.ExecuteNonQueryAsync());
    }

    [Theory]
    [InlineData("'NoLedgerRow', NULL, 5, 1")]
    [InlineData("'NoLedgerRow', '00000001-0000-0000-0000-000000000002', NULL, 1")]
    [InlineData("'NoLedgerRow', NULL, NULL, NULL")]
    [InlineData("'FileMissing', NULL, 5, NULL")]
    [InlineData("'FileMissing', '00000001-0000-0000-0000-000000000002', 5, 1")]
    [InlineData("'SizeDisagrees', NULL, 5, 1")]
    [InlineData("'SizeDisagrees', '00000001-0000-0000-0000-000000000002', NULL, 1")]
    [InlineData("'FileEmpty', '00000001-0000-0000-0000-000000000002', 5, NULL")]
    [InlineData("'FileMissing', '00000001-0000-0000-0000-000000000002', NULL, NULL")]
    [InlineData("'EmptyThoughComplete', '00000001-0000-0000-0000-000000000002', NULL, 0")]
    [InlineData("'Whatever', '00000001-0000-0000-0000-000000000002', 5, 1")]
    [InlineData("'SizeDisagrees', '00000001-0000-0000-0000-000000000002', -1, 1")]
    [InlineData("'SizeDisagrees', '00000001-0000-0000-0000-000000000002', 5, -1")]
    public async Task AFindingWhoseShapeDoesNotMatchItsClassIsRefusedByTheDatabase(string values)
    {
        IntegrityCheckId id = await AnEmptyCheckAsync();

        await Assert.ThrowsAsync<PostgresException>(() => InsertFindingAsync(id, values, "'a.m2ts'"));
    }

    [Theory]
    [InlineData("'/a.m2ts'")]
    [InlineData("''")]
    public async Task AFindingWhosePathLeavesTheRoomIsRefusedByTheDatabase(string path)
    {
        IntegrityCheckId id = await AnEmptyCheckAsync();

        await Assert.ThrowsAsync<PostgresException>(
            () => InsertFindingAsync(id, "'NoLedgerRow', NULL, NULL, 1", path));
    }

    [Fact]
    public async Task AFindingDeeperDownIsAcceptedByTheDatabase()
    {
        IntegrityCheckId id = await AnEmptyCheckAsync();

        await InsertFindingAsync(id, "'NoLedgerRow', NULL, NULL, 1", "'a/b/c.m2ts'");

        Assert.Equal(["a/b/c.m2ts"], (await FindingsAsync(id)).Select(finding => finding.Path).ToArray());
    }

    [Fact]
    public async Task ACheckThatFinishesBeforeItStartsIsRefusedByTheDatabase()
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using var writing = new NpgsqlCommand(
            "INSERT INTO integrity_check (id, started_at, finished_at, roots_walked, roots_out_of_reach, "
            + "files_read, ledger_rows_read, ledger_rows_judged, ledger_rows_still_writing, "
            + "ledger_rows_in_roots_out_of_reach) VALUES "
            + $"('{Guid.NewGuid()}', timestamptz '2026-08-26 05:00:00+00', "
            + "timestamptz '2026-08-26 04:00:00+00', 0, 0, 0, 0, 0, 0, 0)",
            connection);

        await Assert.ThrowsAsync<PostgresException>(() => writing.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ACheckThatCountsBelowNothingIsRefusedByTheDatabase()
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using var writing = new NpgsqlCommand(
            "INSERT INTO integrity_check (id, started_at, finished_at, roots_walked, roots_out_of_reach, "
            + "files_read, ledger_rows_read, ledger_rows_judged, ledger_rows_still_writing, "
            + "ledger_rows_in_roots_out_of_reach) VALUES "
            + $"('{Guid.NewGuid()}', timestamptz '2026-08-26 05:00:00+00', "
            + "timestamptz '2026-08-26 05:00:01+00', -1, 0, 0, 0, 0, 0, 0)",
            connection);

        await Assert.ThrowsAsync<PostgresException>(() => writing.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task FindingsOfACheckThatWasDroppedGoWithIt()
    {
        IntegrityCheckId id = await AnEmptyCheckAsync();
        await InsertFindingAsync(id, "'NoLedgerRow', NULL, NULL, 1", "'a.m2ts'");

        await using (NpgsqlConnection connection = await OpenAsync())
        {
            await using var dropping = new NpgsqlCommand(
                $"DELETE FROM integrity_check WHERE id = '{id.Value}'",
                connection);
            await dropping.ExecuteNonQueryAsync();
        }

        Assert.Empty(await FindingsAsync(id));
    }

    [Fact]
    public async Task TheFindingsComeBackAPageAtATimeInTheOrderTheSweepPutThemIn()
    {
        await ClearAsync();

        IntegrityCheckId id = IntegrityCheckId.New();
        await SaveAsync(IntegrityReport.Of(
            IntegrityCheck.Rehydrate(id, At, At.AddSeconds(1), 1, 0, 3, 0, 0, 0, 0),
            [
                IntegrityFinding.NoLedgerRow(id, Primary, "c.m2ts", 3, At),
                IntegrityFinding.NoLedgerRow(id, Primary, "a.m2ts", 1, At),
                IntegrityFinding.NoLedgerRow(id, Primary, "b.m2ts", 2, At),
            ]));

        PaginatedList<IntegrityFinding> first = await PageAsync(id, page: 1, perPage: 2);
        PaginatedList<IntegrityFinding> second = await PageAsync(id, page: 2, perPage: 2);

        Assert.Equal(["a.m2ts", "b.m2ts"], first.Items.Select(finding => finding.Path).ToArray());
        Assert.Equal(["c.m2ts"], second.Items.Select(finding => finding.Path).ToArray());
        Assert.Equal(3, first.Total);
        Assert.Equal(3, second.Total);
        Assert.Equal(2, first.LastPage);
        Assert.Equal(1, first.CurrentPage);
        Assert.Equal(2, second.CurrentPage);
        Assert.Equal(2, first.PerPage);
    }

    [Fact]
    public async Task ThePageCountsEveryFindingOfThatCheckAndNoOthers()
    {
        await ClearAsync();

        IntegrityCheckId mine = IntegrityCheckId.New();
        IntegrityCheckId theirs = IntegrityCheckId.New();

        await SaveAsync(IntegrityReport.Of(
            IntegrityCheck.Rehydrate(mine, At, At.AddSeconds(1), 1, 0, 2, 0, 0, 0, 0),
            [
                IntegrityFinding.NoLedgerRow(mine, Primary, "mine-a.m2ts", 1, At),
                IntegrityFinding.NoLedgerRow(mine, Primary, "mine-b.m2ts", 2, At),
            ]));
        await SaveAsync(IntegrityReport.Of(
            IntegrityCheck.Rehydrate(theirs, At, At.AddSeconds(2), 1, 0, 1, 0, 0, 0, 0),
            [IntegrityFinding.NoLedgerRow(theirs, Primary, "theirs.m2ts", 3, At)]));

        PaginatedList<IntegrityFinding> page = await PageAsync(mine, page: 1, perPage: 50);

        Assert.Equal(2, page.Total);
        Assert.Equal(
            ["mine-a.m2ts", "mine-b.m2ts"],
            page.Items.Select(finding => finding.Path).ToArray());
    }

    [Fact]
    public async Task APageBeyondTheLastOneIsEmptyRatherThanTheLastPageOver()
    {
        await ClearAsync();

        IntegrityCheckId id = IntegrityCheckId.New();
        await SaveAsync(IntegrityReport.Of(
            IntegrityCheck.Rehydrate(id, At, At.AddSeconds(1), 1, 0, 1, 0, 0, 0, 0),
            [IntegrityFinding.NoLedgerRow(id, Primary, "a.m2ts", 1, At)]));

        PaginatedList<IntegrityFinding> page = await PageAsync(id, page: 9, perPage: 50);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task ListingTheFindingsWithNoPageAtAllIsRefused()
    {
        await using CarinaDbContext context = database.Open();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new IntegrityCheckRepository(context).ListFindingsAsync(
                IntegrityCheckId.New(),
                null!,
                Cancel));
    }

    [Fact]
    public async Task ListingTheFindingsOfNoCheckAtAllIsRefused()
    {
        await using CarinaDbContext context = database.Open();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new IntegrityCheckRepository(context).ListFindingsAsync(
                null!,
                IntegrityFindingQuery.For(null, null)!,
                Cancel));
    }

    [Fact]
    public async Task SavingNoReportAtAllIsRefused()
    {
        await using CarinaDbContext context = database.Open();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new IntegrityCheckRepository(context).SaveAsync(null!, Cancel));
    }

    private async Task<IntegrityCheckId> AnEmptyCheckAsync()
    {
        IntegrityCheckId id = IntegrityCheckId.New();

        await SaveAsync(IntegrityReport.Of(
            IntegrityCheck.Rehydrate(id, At, At, 0, 0, 0, 0, 0, 0, 0),
            []));

        return id;
    }

    private async Task InsertFindingAsync(IntegrityCheckId id, string values, string path)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using var writing = new NpgsqlCommand(
            "INSERT INTO integrity_finding "
            + "(id, check_id, fault, recording_id, ledger_size, observed_size, output_root, path, noticed_at) "
            + $"VALUES ('{Guid.NewGuid()}', '{id.Value}', {values}, 'primary', {path}, "
            + "timestamptz '2026-08-26 05:00:00+00')",
            connection);

        await writing.ExecuteNonQueryAsync();
    }

    private async Task ClearAsync()
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using var clearing = new NpgsqlCommand("DELETE FROM integrity_check", connection);
        await clearing.ExecuteNonQueryAsync();
    }

    private async Task SaveAsync(IntegrityReport report)
    {
        await using CarinaDbContext context = database.Open();
        await new IntegrityCheckRepository(context).SaveAsync(report, Cancel);
    }

    private async Task<IntegrityCheck?> LatestAsync()
    {
        await using CarinaDbContext context = database.Open();

        return await new IntegrityCheckRepository(context).LatestAsync(Cancel);
    }

    private async Task<IntegrityCheck> FindByIdAsync(IntegrityCheckId id)
    {
        await using CarinaDbContext context = database.Open();

        return await context.FindAsync<IntegrityCheck>([id], Cancel)
            ?? throw new InvalidOperationException("The check that was just written is not there.");
    }

    private async Task<IReadOnlyList<IntegrityFinding>> FindingsAsync(IntegrityCheckId id)
    {
        await using CarinaDbContext context = database.Open();

        return (await new IntegrityCheckRepository(context).ListFindingsAsync(
            id,
            IntegrityFindingQuery.For(null, IntegrityFindingQuery.MostPerPage)!,
            Cancel)).Items;
    }

    private async Task<PaginatedList<IntegrityFinding>> PageAsync(IntegrityCheckId id, int page, int perPage)
    {
        await using CarinaDbContext context = database.Open();

        return await new IntegrityCheckRepository(context).ListFindingsAsync(
            id,
            IntegrityFindingQuery.For(page, perPage)!,
            Cancel);
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        await using CarinaDbContext context = database.Open();
        var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();

        return connection;
    }
}
