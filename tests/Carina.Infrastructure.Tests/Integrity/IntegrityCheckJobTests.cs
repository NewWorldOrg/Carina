using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Integrity;
using Carina.Infrastructure.Tests.Scanning;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Integrity;

public sealed class IntegrityCheckJobTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 4, 30, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly OutputRoot Bulk = new("bulk");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static RecordingId Id(int seed) => new(new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]));

    private static LedgerFile Complete(OutputRoot root, string fileName, long size, int seed = 1)
        => LedgerFile.Ended(Id(seed), root, new RecordingFileName(fileName), LedgerClaim.EverythingLanded, size);

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

    private static IntegrityCheckJob Job(
        HeldLedger ledger,
        IRecordingFileSurvey survey,
        HeldChecks checks,
        IntegritySettings? settings = null,
        TimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IRecordingLedger>(_ => ledger);
        services.AddScoped<IIntegrityCheckRepository>(_ => checks);

        return new IntegrityCheckJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            survey,
            settings ?? new IntegritySettings
            {
                OutputRoots = [new StorageRootPath(Primary, "/srv/recordings")],
            },
            clock ?? new StoppedClock(Now),
            NullLogger<IntegrityCheckJob>.Instance);
    }
}
