using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Integrity;
using Carina.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Integrity;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class IntegrityCheckReachesTheTableTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = new(2026, 8, 26, 6, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static RecordingId Id(int seed) => new(new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 3]));

    private static LedgerFile Complete(string fileName, long size, int seed)
        => LedgerFile.Ended(Id(seed), Primary, new RecordingFileName(fileName), LedgerClaim.EverythingLanded, size);

    [Fact]
    public async Task ARootThatCouldNotBeReadIsWrittenDownRatherThanPassedOverInSilence()
    {
        using var tree = new TempTree();
        var ledger = new HeldLedger(Complete("one.m2ts", 100, 1), Complete("two.m2ts", 200, 2));

        IntegrityCheckId ran = await SweepAsync(ledger, tree.Under("nothing-is-mounted-here"));

        IntegrityCheck written = await ReadAsync(ran);

        Assert.Equal(0, written.RootsWalked);
        Assert.Equal(1, written.RootsOutOfReach);
        Assert.Equal(0, written.FilesRead);
        Assert.Equal(2, written.LedgerRowsRead);
        Assert.Equal(0, written.LedgerRowsJudged);
        Assert.Equal(0, written.LedgerRowsStillWriting);
        Assert.Equal(2, written.LedgerRowsInRootsOutOfReach);
        Assert.Empty(await FindingsAsync(ran));
    }

    [Fact]
    public async Task ARootThatCouldBeReadSaysSoInTheSameColumns()
    {
        using var tree = new TempTree();
        tree.Holding("one.m2ts", 100).Holding("two.m2ts", 200);
        var ledger = new HeldLedger(Complete("one.m2ts", 100, 1), Complete("two.m2ts", 200, 2));

        IntegrityCheckId ran = await SweepAsync(ledger, tree.Root);

        IntegrityCheck written = await ReadAsync(ran);

        Assert.Equal(1, written.RootsWalked);
        Assert.Equal(0, written.RootsOutOfReach);
        Assert.Equal(2, written.FilesRead);
        Assert.Equal(2, written.LedgerRowsJudged);
        Assert.Equal(0, written.LedgerRowsInRootsOutOfReach);
        Assert.Empty(await FindingsAsync(ran));
    }

    [Fact]
    public async Task EveryClassASweepCanFindLandsInTheTableWithItsReason()
    {
        using var tree = new TempTree();
        tree
            .Holding("disagrees.m2ts", 300)
            .Holding("empty.m2ts", 0)
            .Holding("hollow.m2ts", 0)
            .Holding("thumbnails/buried.jpg", 40);

        var ledger = new HeldLedger(
            Complete("disagrees.m2ts", 999, 1),
            LedgerFile.Ended(Id(2), Primary, new RecordingFileName("empty.m2ts"), LedgerClaim.SomethingLanded, 500),
            Complete("hollow.m2ts", 500, 3),
            Complete("gone.m2ts", 600, 4));

        IntegrityCheckId ran = await SweepAsync(ledger, tree.Root);

        IReadOnlyList<IntegrityFinding> written = await FindingsAsync(ran);

        Assert.Equal(
            ["EmptyThoughComplete", "FileEmpty", "FileMissing", "NoLedgerRow", "SizeDisagrees"],
            written.Select(finding => finding.Fault.ToString()).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["disagrees.m2ts", "empty.m2ts", "gone.m2ts", "hollow.m2ts", "thumbnails/buried.jpg"],
            written.Select(finding => finding.Path).Order(StringComparer.Ordinal).ToArray());

        IntegrityFinding disagreed = written.Single(finding => finding.Path is "disagrees.m2ts");

        Assert.Equal(Id(1), disagreed.RecordingId);
        Assert.Equal(999, disagreed.LedgerSize);
        Assert.Equal(300, disagreed.ObservedSize);
        Assert.Equal(Now, disagreed.NoticedAt);
        Assert.Equal(4, (await ReadAsync(ran)).FilesRead);
    }

    private async Task<IntegrityCheckId> SweepAsync(HeldLedger ledger, string mountedAt)
    {
        var settings = new IntegritySettings
        {
            OutputRoots = [new StorageRootPath(Primary, mountedAt)],
        };

        var services = new ServiceCollection();
        services.AddScoped<IRecordingLedger>(_ => ledger);
        services.AddScoped(_ => database.Open());
        services.AddScoped<IIntegrityCheckRepository, IntegrityCheckRepository>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        using var job = new IntegrityCheckJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new LocalRecordingFileSurvey(settings, NullLogger<LocalRecordingFileSurvey>.Instance),
            settings,
            new StoppedClock(Now),
            NullLogger<IntegrityCheckJob>.Instance);

        IntegrityRun run = await job.RunAsync(Cancel);

        return Assert.IsType<IntegrityReport>(run.Swept).Check.Id;
    }

    private async Task<IntegrityCheck> ReadAsync(IntegrityCheckId id)
    {
        await using CarinaDbContext context = database.Open();

        return await context.FindAsync<IntegrityCheck>([id], Cancel)
            ?? throw new InvalidOperationException("The check that was just run is not in the table.");
    }

    private async Task<IReadOnlyList<IntegrityFinding>> FindingsAsync(IntegrityCheckId id)
    {
        await using CarinaDbContext context = database.Open();

        return await new IntegrityCheckRepository(context).ListFindingsAsync(id, Cancel);
    }
}
