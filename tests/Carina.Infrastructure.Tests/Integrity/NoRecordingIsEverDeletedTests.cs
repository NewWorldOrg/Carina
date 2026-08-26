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

    private static readonly int[] Sizes =
    [
        0, 1, 999, 1_000, 1_001, 4_095, 4_096, 4_097, 65_535, 65_536, 65_537, 1_048_576,
    ];

    private static readonly int[] SizesAcrossTheLedger = [999, 1_000, 1_001, 65_536];

    public static TheoryData<int> EverySize
    {
        get
        {
            var sizes = new TheoryData<int>();

            foreach (int size in Sizes)
            {
                sizes.Add(size);
            }

            return sizes;
        }
    }

    private static readonly string[] States = ["agrees", "disagrees", "orphan", "stillWriting", "failed"];

    public static TheoryData<int, string> EverySizeAgainstEveryLedgerState
    {
        get
        {
            var rows = new TheoryData<int, string>();

            foreach (int size in SizesAcrossTheLedger)
            {
                foreach (string state in States)
                {
                    rows.Add(size, state);
                }
            }

            return rows;
        }
    }

    private static RecordingId Id(int seed) => new(new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]));

    private static LedgerFile Ended(string fileName, LedgerClaim claim, long size, int seed)
        => LedgerFile.Ended(Id(seed), Primary, new RecordingFileName(fileName), claim, size);

    [Theory]
    [MemberData(nameof(EverySize))]
    public async Task AFileOfThisSizeIsTheSameFileAfterASweep(int size)
    {
        using var recordings = new TempTree();
        recordings.Holding("one.m2ts", size);

        IReadOnlyList<string> before = recordings.Snapshot();

        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(new HeldLedger(), checks, recordings);

        Assert.Single((await job.RunAsync(Cancel)).Swept!.Findings);
        Assert.Equal(before, recordings.Snapshot());
        Assert.Equal(1, checks.Saved[0].Check.FilesRead);
    }

    [Theory]
    [MemberData(nameof(EverySizeAgainstEveryLedgerState))]
    public async Task AFileOfThisSizeInThisLedgerStateIsTheSameFileAfterASweep(int size, string state)
    {
        using var recordings = new TempTree();
        recordings.Holding("one.m2ts", size);

        IReadOnlyList<string> before = recordings.Snapshot();

        LedgerFile? row = Row(state, size);
        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(
            row is null ? new HeldLedger() : new HeldLedger(row),
            checks,
            recordings);

        await job.RunAsync(Cancel);

        Assert.Equal(before, recordings.Snapshot());
        Assert.Equal(1, checks.Saved[0].Check.FilesRead);
    }

    [Fact]
    public async Task ATreeOfEverySizeAtEveryDepthIsTheSameTreeAfterASweep()
    {
        using var recordings = new TempTree();
        List<string> written = [];

        for (int index = 0; index < Sizes.Length; index++)
        {
            string room = string.Join("/", Enumerable.Repeat("down", index % 5));
            string path = room.Length is 0 ? $"file-{index}.m2ts" : $"{room}/file-{index}.m2ts";
            recordings.Holding(path, Sizes[index]);
            written.Add(path);
        }

        IReadOnlyList<string> before = recordings.Snapshot();

        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(new HeldLedger(), checks, recordings);

        IntegrityReport swept = (await job.RunAsync(Cancel)).Swept!;

        Assert.Equal(written.Count, swept.Check.FilesRead);
        Assert.Equal(written.Count, swept.Findings.Count);
        Assert.Equal(before, recordings.Snapshot());
        Assert.Equal(written.Order(StringComparer.Ordinal), swept.Findings.Select(f => f.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task SweepingTheSameTreeTenTimesLeavesItTheSameTree()
    {
        using var recordings = new TempTree();

        for (int index = 0; index < Sizes.Length; index++)
        {
            recordings.Holding($"file-{index}.m2ts", Sizes[index]);
        }

        IReadOnlyList<string> before = recordings.Snapshot();

        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(new HeldLedger(), checks, recordings);

        for (int round = 0; round < 10; round++)
        {
            Assert.Equal(Sizes.Length, (await job.RunAsync(Cancel)).Swept!.Check.FilesRead);
        }

        Assert.Equal(before, recordings.Snapshot());
        Assert.Equal(Sizes.Length, before.Count);
    }

    [Fact]
    public async Task ASweepOverEveryKindOfTroubleLeavesEveryByteWhereItWas()
    {
        using var recordings = new TempTree();

        recordings
            .Holding("agrees.m2ts", 400_000)
            .Holding("disagrees.m2ts", 300_000)
            .Holding("empty.m2ts", 0)
            .Holding("hollow.m2ts", 0)
            .Holding("nothing-landed.m2ts", 0)
            .Holding("stray.m2ts", 200_000)
            .Holding("still-writing.m2ts", 100_000)
            .Holding("thumbnails/buried.jpg", 40_000)
            .HoldingDirectory("empty-room");

        IReadOnlyList<string> before = recordings.Snapshot();

        var ledger = new HeldLedger(
            Ended("agrees.m2ts", LedgerClaim.EverythingLanded, 400_000, 1),
            Ended("disagrees.m2ts", LedgerClaim.EverythingLanded, 999_000, 2),
            Ended("empty.m2ts", LedgerClaim.SomethingLanded, 500_000, 3),
            Ended("hollow.m2ts", LedgerClaim.EverythingLanded, 500_000, 4),
            Ended("nothing-landed.m2ts", LedgerClaim.NothingLanded, 0, 5),
            Ended("gone.m2ts", LedgerClaim.EverythingLanded, 600_000, 6),
            LedgerFile.StillWriting(Id(7), Primary, new RecordingFileName("still-writing.m2ts")));

        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(ledger, checks, recordings);

        IntegrityReport swept = Assert.IsType<IntegrityReport>((await job.RunAsync(Cancel)).Swept);

        Assert.Equal(
            ["EmptyThoughComplete", "FileEmpty", "FileMissing", "NoLedgerRow", "NoLedgerRow", "SizeDisagrees"],
            swept.Findings.Select(finding => finding.Fault.ToString()).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(6, swept.Check.LedgerRowsJudged);
        Assert.Equal(8, swept.Check.FilesRead);
        Assert.Equal(before, recordings.Snapshot());
        Assert.Equal(10, before.Count);
    }

    [Fact]
    public async Task ASweepThatFindsNothingWrongStillLeavesEveryByteWhereItWas()
    {
        using var recordings = new TempTree();
        recordings.Holding("agrees.m2ts", 400_000);

        IReadOnlyList<string> before = recordings.Snapshot();

        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(
            new HeldLedger(Ended("agrees.m2ts", LedgerClaim.EverythingLanded, 400_000, 1)),
            checks,
            recordings);

        IntegrityReport swept = (await job.RunAsync(Cancel)).Swept!;

        Assert.Empty(swept.Findings);
        Assert.Equal(1, swept.Check.FilesRead);
        Assert.Equal(before, recordings.Snapshot());
    }

    [Fact]
    public async Task ASweepPutsNothingOfItsOwnUnderTheRecordingStore()
    {
        using var recordings = new TempTree();
        recordings.Holding("agrees.m2ts", 400_000);

        IReadOnlyList<string> before = recordings.Snapshot();

        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(new HeldLedger(), checks, recordings);
        await job.RunAsync(Cancel);

        Assert.Equal(before, recordings.Snapshot());
        Assert.Single(checks.Saved);
        Assert.Single(checks.Saved[0].Findings);
    }

    private static LedgerFile? Row(string state, int size)
        => state switch
        {
            "agrees" => Ended("one.m2ts", LedgerClaim.EverythingLanded, size, 1),
            "disagrees" => Ended("one.m2ts", LedgerClaim.EverythingLanded, size + 1, 1),
            "stillWriting" => LedgerFile.StillWriting(Id(1), Primary, new RecordingFileName("one.m2ts")),
            "failed" => Ended("one.m2ts", LedgerClaim.NothingLanded, size, 1),
            _ => null,
        };

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
