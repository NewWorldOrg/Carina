using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Integrity;
using Carina.Infrastructure.Tests.Fixtures.Clocks;
using Carina.Infrastructure.Tests.Fixtures.Integrity;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Unit.Infra.Integrity;

[Trait("Category", "Unit")]
public sealed class IntegrityCheckJobTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 4, 30, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly OutputRoot Bulk = new("bulk");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime NamesSweptAt = new(2026, 8, 26, 7, 0, 0, DateTimeKind.Utc);

    private static readonly int[] Sizes =
    [
        0, 1, 999, 1_000, 1_001, 4_095, 4_096, 4_097, 65_535, 65_536, 65_537, 1_048_576,
    ];

    private static readonly int[] SizesAcrossTheLedger = [999, 1_000, 1_001, 65_536];

    private static readonly string[] States = ["agrees", "disagrees", "orphan", "stillWriting", "failed"];

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

    [Fact]
    public async Task ARunComparesTheLedgerWithTheFilesAndKeepsWhatItFound()
    {
        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(
            new HeldLedger(Complete(Primary, "one.m2ts", 100, 7)),
            new HeldSurvey().Declaring(Primary, ("one.m2ts", 99)),
            checks);

        IntegrityRun run = await job.RunAsync(Cancel);

        Assert.False(run.AlreadyRunning);
        IntegrityReport swept = Assert.IsType<IntegrityReport>(run.Swept);
        IntegrityFinding found = Assert.Single(swept.Findings);
        Assert.Equal(IntegrityFault.SizeDisagrees, found.Fault);
        Assert.Equal(Id(7), found.RecordingId);
        Assert.Same(swept, Assert.Single(checks.Saved));
    }

    [Fact]
    public async Task ARunStampsTheCheckWithTheClockItWasGiven()
    {
        using IntegrityCheckJob job = Job(new HeldLedger(), new HeldSurvey(), new HeldChecks());

        IntegrityRun run = await job.RunAsync(Cancel);

        Assert.Equal(Now, run.Swept!.Check.StartedAt);
        Assert.Equal(Now, run.Swept!.Check.FinishedAt);
    }

    [Fact]
    public async Task EveryRunIsItsOwnCheck()
    {
        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(new HeldLedger(), new HeldSurvey(), checks);

        await job.RunAsync(Cancel);
        await job.RunAsync(Cancel);

        Assert.NotEqual(checks.Saved[0].Check.Id, checks.Saved[1].Check.Id);
    }

    [Fact]
    public async Task ARunWalksTheRootsItIsOfferedAndTheOnesOnlyTheLedgerKnows()
    {
        HeldSurvey survey = new HeldSurvey().Declaring(Primary, ("one.m2ts", 100));
        using IntegrityCheckJob job = Job(
            new HeldLedger(Complete(Bulk, "two.m2ts", 100)),
            survey,
            new HeldChecks());

        await job.RunAsync(Cancel);

        Assert.Equal(["primary", "bulk"], survey.Asked);
    }

    [Fact]
    public async Task ARunWalksARootOnlyOnceHoweverManyRowsNameIt()
    {
        HeldSurvey survey = new HeldSurvey().Declaring(Primary, ("one.m2ts", 100));
        using IntegrityCheckJob job = Job(
            new HeldLedger(Complete(Primary, "one.m2ts", 100, 1), Complete(Primary, "two.m2ts", 100, 2)),
            survey,
            new HeldChecks());

        await job.RunAsync(Cancel);

        Assert.Equal(["primary"], survey.Asked);
    }

    [Fact]
    public async Task ASecondRunIsRefusedWhileTheFirstIsStillGoing()
    {
        var ledger = new HeldLedger(Complete(Primary, "one.m2ts", 100)) { Gate = new TaskCompletionSource() };
        using IntegrityCheckJob job = Job(ledger, new HeldSurvey(), new HeldChecks());

        Task<IntegrityRun> first = job.RunAsync(Cancel);
        await Eventually.Happens(() => ledger.Reads is 1, "the first run never reached the ledger");

        IntegrityRun second = await job.RunAsync(Cancel).WaitAsync(Eventually.Patience);

        Assert.True(second.AlreadyRunning);
        Assert.Null(second.Swept);
        Assert.Equal(1, ledger.Reads);

        ledger.Gate!.SetResult();

        Assert.False((await first).AlreadyRunning);
    }

    [Fact]
    public async Task TheRefusalNamesTheCheckTheRunItWaitedOnIsWriting()
    {
        var ledger = new HeldLedger(Complete(Primary, "one.m2ts", 100)) { Gate = new TaskCompletionSource() };
        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(ledger, new HeldSurvey(), checks);

        Task<IntegrityRun> first = job.RunAsync(Cancel);
        await Eventually.Happens(() => ledger.Reads is 1, "the first run never reached the ledger");

        IntegrityCheckId? walking = job.RunningCheck;
        IntegrityRun second = await job.RunAsync(Cancel).WaitAsync(Eventually.Patience);

        Assert.NotNull(walking);
        Assert.Equal(walking, second.Running);

        ledger.Gate!.SetResult();
        await first;

        Assert.Equal(walking, Assert.Single(checks.Saved).Check.Id);
    }

    [Fact]
    public async Task NothingIsWalkingBeforeARunAndNothingIsWalkingAfterOne()
    {
        using IntegrityCheckJob job = Job(new HeldLedger(), new HeldSurvey(), new HeldChecks());

        Assert.Null(job.RunningCheck);
        await job.RunAsync(Cancel);
        Assert.Null(job.RunningCheck);
    }

    [Fact]
    public async Task NothingIsLeftWalkingWhenTheSweepThrows()
    {
        var ledger = new HeldLedger(Complete(Primary, "one.m2ts", 100)) { Gate = new TaskCompletionSource() };
        using IntegrityCheckJob job = Job(ledger, new HeldSurvey(), new HeldChecks());

        ledger.Gate!.SetException(new InvalidOperationException("the ledger could not be read"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.RunAsync(Cancel));

        Assert.Null(job.RunningCheck);
    }

    [Fact]
    public async Task NothingIsLeftWalkingWhenTheWalkOverTheFilesThrows()
    {
        using IntegrityCheckJob job = Job(new HeldLedger(), new ThrowingSurvey(), new HeldChecks());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => job.RunAsync(Cancel));

        Assert.Null(job.RunningCheck);
    }

    [Fact]
    public async Task ARunWhoseWalkOverTheFilesThrewStillLetsTheNextOneStart()
    {
        var checks = new HeldChecks();
        var survey = new ThrowingSurvey();
        using IntegrityCheckJob job = Job(new HeldLedger(), survey, checks);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => job.RunAsync(Cancel));

        survey.Throws = false;

        Assert.False((await job.RunAsync(Cancel)).AlreadyRunning);
        Assert.Single(checks.Saved);
    }

    [Fact]
    public async Task NothingIsLeftWalkingWhenTheCallerGivesUpPartWayThrough()
    {
        var ledger = new HeldLedger(Complete(Primary, "one.m2ts", 100)) { Gate = new TaskCompletionSource() };
        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(ledger, new HeldSurvey(), checks);
        using var giving = new CancellationTokenSource();

        Task<IntegrityRun> walking = job.RunAsync(giving.Token);
        await Eventually.Happens(() => ledger.Reads is 1, "the run never reached the ledger");
        await giving.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => walking);

        Assert.Null(job.RunningCheck);
        Assert.Empty(checks.Saved);

        ledger.Gate = null;

        Assert.False((await job.RunAsync(Cancel)).AlreadyRunning);
    }

    [Fact]
    public async Task ARunIsAllowedAgainOnceTheOneBeforeItHasFinished()
    {
        var ledger = new HeldLedger(Complete(Primary, "one.m2ts", 100));
        using IntegrityCheckJob job = Job(ledger, new HeldSurvey(), new HeldChecks());

        Assert.False((await job.RunAsync(Cancel)).AlreadyRunning);
        Assert.False((await job.RunAsync(Cancel)).AlreadyRunning);
        Assert.Equal(2, ledger.Reads);
    }

    [Fact]
    public async Task ARunThatThrowsStillLetsTheNextOneStart()
    {
        var ledger = new HeldLedger(Complete(Primary, "one.m2ts", 100)) { Gate = new TaskCompletionSource() };
        using IntegrityCheckJob job = Job(ledger, new HeldSurvey(), new HeldChecks());

        ledger.Gate!.SetException(new InvalidOperationException("the ledger could not be read"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.RunAsync(Cancel));

        ledger.Gate = null;

        Assert.False((await job.RunAsync(Cancel)).AlreadyRunning);
    }

    [Fact]
    public async Task TheLoopSweepsOnceItsFirstWaitIsOverAndKeepsSweepingAfterThat()
    {
        var clock = new HurriedClock();
        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(
            new HeldLedger(Complete(Primary, "one.m2ts", 100)),
            new HeldSurvey().Declaring(Primary, ("one.m2ts", 99)),
            checks,
            new IntegritySettings
            {
                BeforeFirstSweep = TimeSpan.FromMinutes(3),
                BetweenSweeps = TimeSpan.FromHours(2),
                OutputRoots = [new StorageRootPath(Primary, "/srv/recordings")],
            },
            clock);
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => checks.Saved.Count >= 2, "the loop never swept twice");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal([TimeSpan.FromMinutes(3), TimeSpan.FromHours(2)], clock.Waits.Take(2).ToArray());
    }

    [Fact]
    public async Task TheLoopDoesNotStartWhenNoRootIsMountedIntoThisProcess()
    {
        var checks = new HeldChecks();
        var ledger = new HeldLedger(Complete(Primary, "one.m2ts", 100));
        using IntegrityCheckJob job = Job(
            ledger,
            new HeldSurvey().Declaring(Primary, ("one.m2ts", 99)),
            checks,
            new IntegritySettings { OutputRoots = [] },
            new HurriedClock());
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await job.ExecuteTask!.WaitAsync(Eventually.Patience);
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Empty(checks.Saved);
        Assert.Equal(0, ledger.Reads);
    }

    [Fact]
    public async Task TheJobStillSweepsByHandWhenNoRootIsMounted()
    {
        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(
            new HeldLedger(),
            new HeldSurvey(),
            checks,
            new IntegritySettings { OutputRoots = [] },
            new HurriedClock());

        Assert.False((await job.RunAsync(Cancel)).AlreadyRunning);
        Assert.Single(checks.Saved);
    }

    [Fact]
    public async Task ARunAsksWhatAnEncodeJobIsWritingAndLeavesThoseFilesAlone()
    {
        var working = new HeldEncodeWork(new DeclaredFile(Primary, "one.encoding"));
        using IntegrityCheckJob job = Job(
            new HeldLedger(),
            new HeldSurvey().Declaring(Primary, ("one.encoding", 512), ("stray.m2ts", 5)),
            new HeldChecks(),
            working: working);

        IntegrityRun run = await job.RunAsync(Cancel);

        Assert.Equal(1, working.Reads);
        Assert.Equal("stray.m2ts", Assert.Single(run.Swept!.Findings).Path);
    }

    [Fact]
    public async Task ARunWalksThePlacesThisProcessWritesAndLeavesWhatTheLedgerClaimsThereAlone()
    {
        var encodes = new OutputRoot("encodes");
        var pictures = new OutputRoot("thumbnails");
        var places = new HeldPlaces(
            RootListing.Of(encodes, [new StoredFile("kept.mp4", 9), new StoredFile("left.mp4", 8)], StoragePlace.Encodes),
            RootListing.Of(pictures, [new StoredFile("kept.jpg", 3), new StoredFile("left.jpg", 2)], StoragePlace.Thumbnails));
        places.Pictures.Add(new DeclaredFile(pictures, "kept.jpg"));
        using IntegrityCheckJob job = Job(
            new HeldLedger(),
            new HeldSurvey().Declaring(Primary, ("one.encoding", 512)),
            new HeldChecks(),
            working: new HeldEncodeWork(new DeclaredFile(Primary, "one.encoding"), new DeclaredFile(encodes, "kept.mp4")),
            places: places);

        IntegrityRun run = await job.RunAsync(Cancel);

        Assert.Equal(
            ["encodes/left.mp4", "thumbnails/left.jpg"],
            run.Swept!.Findings.Select(finding => $"{finding.Root.Value}/{finding.Path}").ToArray());
        Assert.Equal(3, run.Swept.Check.RootsWalked);
    }

    [Fact]
    public async Task ASweepOverEveryAwkwardNameAtOnceStillFinishes()
    {
        using var tree = new TempTree();

        foreach (string name in AwkwardNames.All)
        {
            tree.Holding(name, 12);
        }

        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(new HeldLedger(), checks, EveryMinute(tree), new StoppedClock(NamesSweptAt));

        IntegrityRun run = await job.RunAsync(Cancel);

        IntegrityReport swept = Assert.IsType<IntegrityReport>(run.Swept);

        Assert.Equal(AwkwardNames.All.Length, swept.Check.FilesRead);
        Assert.Equal(AwkwardNames.All.Length, swept.Findings.Count);
        Assert.All(swept.Findings, finding => Assert.Equal(IntegrityFault.NoLedgerRow, finding.Fault));
        Assert.Single(checks.Saved);
    }

    [Fact]
    public async Task TheLoopKeepsWritingCheckRowsWhileAnAwkwardNameSitsInTheRoot()
    {
        using var tree = new TempTree();
        tree.Holding("2026..08.m2ts", 12).Holding("trailing.m2ts ", 12).Holding("a\\b.m2ts", 12);

        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(new HeldLedger(), checks, EveryMinute(tree), new HurriedClock());
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(
            () => checks.Saved.Count >= 3,
            "the loop stopped writing check rows while an awkward name sat in the root");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.All(checks.Saved, saved => Assert.Equal(3, saved.Check.FilesRead));
    }

    [Theory]
    [MemberData(nameof(EverySize))]
    public async Task AFileOfThisSizeIsTheSameFileAfterASweep(int size)
    {
        using var recordings = new TempTree();
        recordings.Holding("one.m2ts", size);

        IReadOnlyList<string> before = recordings.Snapshot();

        var checks = new HeldChecks();
        using IntegrityCheckJob job = Job(new HeldLedger(), checks, Over(recordings));

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
            Over(recordings));

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
        using IntegrityCheckJob job = Job(new HeldLedger(), checks, Over(recordings));

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
        using IntegrityCheckJob job = Job(new HeldLedger(), checks, Over(recordings));

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
        using IntegrityCheckJob job = Job(ledger, checks, Over(recordings));

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
            Over(recordings));

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
        using IntegrityCheckJob job = Job(new HeldLedger(), checks, Over(recordings));
        await job.RunAsync(Cancel);

        Assert.Equal(before, recordings.Snapshot());
        Assert.Single(checks.Saved);
        Assert.Single(checks.Saved[0].Findings);
    }

    private static RecordingId Id(int seed) => new(new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]));

    private static LedgerFile Complete(OutputRoot root, string fileName, long size, int seed = 1)
        => LedgerFile.Ended(Id(seed), root, new RecordingFileName(fileName), LedgerClaim.EverythingLanded, size);

    private static LedgerFile Ended(string fileName, LedgerClaim claim, long size, int seed)
        => LedgerFile.Ended(Id(seed), Primary, new RecordingFileName(fileName), claim, size);

    private static LedgerFile? Row(string state, int size)
        => state switch
        {
            "agrees" => Ended("one.m2ts", LedgerClaim.EverythingLanded, size, 1),
            "disagrees" => Ended("one.m2ts", LedgerClaim.EverythingLanded, size + 1, 1),
            "stillWriting" => LedgerFile.StillWriting(Id(1), Primary, new RecordingFileName("one.m2ts")),
            "failed" => Ended("one.m2ts", LedgerClaim.NothingLanded, size, 1),
            _ => null,
        };

    private static IntegrityCheckJob Job(
        HeldLedger ledger,
        IRecordingFileSurvey survey,
        HeldChecks checks,
        IntegritySettings? settings = null,
        TimeProvider? clock = null,
        HeldEncodeWork? working = null,
        HeldPlaces? places = null)
    {
        IWrittenFileSurvey written = places ?? new HeldPlaces();
        var services = new ServiceCollection();
        services.AddScoped<IRecordingLedger>(_ => ledger);
        services.AddScoped<IEncodeWorkLedger>(_ => working ?? new HeldEncodeWork());
        services.AddSingleton(written);
        services.AddScoped<IntegrityClaimDomainService>();
        services.AddScoped<IIntegrityCheckRepository>(_ => checks);

        return new IntegrityCheckJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            survey,
            written,
            settings ?? new IntegritySettings
            {
                OutputRoots = [new StorageRootPath(Primary, "/srv/recordings")],
            },
            clock ?? new StoppedClock(Now),
            NullLogger<IntegrityCheckJob>.Instance);
    }

    private static IntegrityCheckJob Job(HeldLedger ledger, HeldChecks checks, IntegritySettings settings, TimeProvider? clock = null)
        => Job(ledger, new LocalRecordingFileSurvey(settings, NullLogger<LocalRecordingFileSurvey>.Instance), checks, settings, clock);

    private static IntegritySettings Over(TempTree recordings)
        => new() { OutputRoots = [new StorageRootPath(Primary, recordings.Root)] };

    private static IntegritySettings EveryMinute(TempTree tree)
        => Over(tree) with
        {
            BeforeFirstSweep = TimeSpan.FromMinutes(1),
            BetweenSweeps = TimeSpan.FromMinutes(1),
        };
}
