using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Integrity;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Integrity;

public sealed class NoRecordingIsEverDeletedTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 4, 30, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static RecordingId Id(int seed) => new(new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]));

    private static LedgerFile Ended(string fileName, LedgerClaim claim, long size, int seed)
        => LedgerFile.Ended(Id(seed), Primary, new RecordingFileName(fileName), claim, size);

    [Fact]
    public async Task ASweepOverEveryKindOfTroubleLeavesEveryByteWhereItWas()
    {
        using var recordings = new TempTree();

        recordings
            .Holding("agrees.m2ts", 400)
            .Holding("disagrees.m2ts", 300)
            .Holding("empty.m2ts", 0)
            .Holding("hollow.m2ts", 0)
            .Holding("nothing-landed.m2ts", 0)
            .Holding("stray.m2ts", 200)
            .Holding("still-writing.m2ts", 100)
            .Holding("thumbnails/buried.jpg", 40)
            .HoldingDirectory("empty-room");

        IReadOnlyList<string> before = recordings.Snapshot();

        var ledger = new HeldLedger(
            Ended("agrees.m2ts", LedgerClaim.EverythingLanded, 400, 1),
            Ended("disagrees.m2ts", LedgerClaim.EverythingLanded, 999, 2),
            Ended("empty.m2ts", LedgerClaim.SomethingLanded, 500, 3),
            Ended("hollow.m2ts", LedgerClaim.EverythingLanded, 500, 4),
            Ended("nothing-landed.m2ts", LedgerClaim.NothingLanded, 0, 5),
            Ended("gone.m2ts", LedgerClaim.EverythingLanded, 600, 6),
            LedgerFile.StillWriting(Id(7), Primary, new RecordingFileName("still-writing.m2ts")));

        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(ledger, checks, recordings);
        IntegrityRun run = await job.RunAsync(Cancel);

        IntegrityReport swept = Assert.IsType<IntegrityReport>(run.Swept);

        Assert.Equal(
            ["EmptyThoughComplete", "FileEmpty", "FileMissing", "NoLedgerRow", "NoLedgerRow", "SizeDisagrees"],
            swept.Findings.Select(finding => finding.Fault.ToString()).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["disagrees.m2ts", "empty.m2ts", "gone.m2ts", "hollow.m2ts", "stray.m2ts", "thumbnails/buried.jpg"],
            swept.Findings.Select(finding => finding.Path).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(6, swept.Check.LedgerRowsJudged);
        Assert.Equal(8, swept.Check.FilesRead);
        Assert.Equal(before, recordings.Snapshot());
        Assert.Equal(10, before.Count);
    }

    [Fact]
    public async Task ASweepThatFindsNothingWrongStillLeavesEveryByteWhereItWas()
    {
        using var recordings = new TempTree();

        recordings.Holding("agrees.m2ts", 400);

        IReadOnlyList<string> before = recordings.Snapshot();

        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(
            new HeldLedger(Ended("agrees.m2ts", LedgerClaim.EverythingLanded, 400, 1)),
            checks,
            recordings);
        IntegrityReport swept = (await job.RunAsync(Cancel)).Swept!;

        Assert.Empty(swept.Findings);
        Assert.Equal(1, swept.Check.FilesRead);
        Assert.Equal(before, recordings.Snapshot());
        Assert.Single(before);
    }

    [Fact]
    public async Task SweepingTheSameTroubleAgainAndAgainStillLeavesEveryByteWhereItWas()
    {
        using var recordings = new TempTree();

        recordings.Holding("empty.m2ts", 0).Holding("nested/stray.m2ts", 200);

        IReadOnlyList<string> before = recordings.Snapshot();

        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(
            new HeldLedger(Ended("empty.m2ts", LedgerClaim.SomethingLanded, 500, 3)),
            checks,
            recordings);

        for (int round = 0; round < 5; round++)
        {
            Assert.Equal(2, (await job.RunAsync(Cancel)).Swept!.Findings.Count);
        }

        Assert.Equal(before, recordings.Snapshot());
        Assert.Equal(3, before.Count);
    }

    [Fact]
    public async Task ASweepPutsNothingOfItsOwnUnderTheRecordingStore()
    {
        using var recordings = new TempTree();

        recordings.Holding("agrees.m2ts", 400);

        IReadOnlyList<string> before = recordings.Snapshot();

        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(new HeldLedger(), checks, recordings);
        await job.RunAsync(Cancel);

        Assert.Equal(before, recordings.Snapshot());
        Assert.Single(checks.Saved);
        Assert.Single(checks.Saved[0].Findings);
    }

    private static IntegrityCheckJob Job(HeldLedger ledger, HeldChecks checks, TempTree recordings)
    {
        var settings = new IntegritySettings
        {
            OutputRoots = [new StorageRootPath(Primary, recordings.Root)],
        };

        var services = new ServiceCollection();
        services.AddScoped<IRecordingLedger>(_ => ledger);
        services.AddScoped<IIntegrityCheckRepository>(_ => checks);

        return new IntegrityCheckJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new LocalRecordingFileSurvey(settings, NullLogger<LocalRecordingFileSurvey>.Instance),
            settings,
            new StoppedClock(Now),
            NullLogger<IntegrityCheckJob>.Instance);
    }
}
