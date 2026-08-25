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

    private static LedgerFile Ended(OutputRoot root, string fileName, long size, int seed = 1)
        => LedgerFile.Ended(Id(seed), root, new RecordingFileName(fileName), size);

    [Fact]
    public async Task ARunCompareTheLedgerWithTheFilesAndKeepsWhatItFound()
    {
        var reports = new HeldReports();
        using IntegrityCheckJob job = Job(
            new HeldLedger(Ended(Primary, "one.m2ts", 100, 7)),
            new HeldSurvey().Declaring(Primary, ("one.m2ts", 99)),
            reports);

        IntegrityRun run = await job.RunAsync(Cancel);

        Assert.False(run.AlreadyRunning);
        IntegritySweep swept = Assert.IsType<IntegritySweep>(run.Swept);
        IntegrityFinding found = Assert.Single(swept.Findings);
        Assert.Equal(IntegrityFault.SizeDisagrees, found.Fault);
        Assert.Equal(Id(7), found.RecordingId);
        Assert.Same(swept, Assert.Single(reports.Saved));
    }

    [Fact]
    public async Task ARunStampsTheSweepWithTheClockItWasGiven()
    {
        using IntegrityCheckJob job = Job(new HeldLedger(), new HeldSurvey(), new HeldReports());

        IntegrityRun run = await job.RunAsync(Cancel);

        Assert.Equal(Now, run.Swept!.RanAt);
    }

    [Fact]
    public async Task ARunWalksTheRootsItIsOfferedAndTheOnesOnlyTheLedgerKnows()
    {
        HeldSurvey survey = new HeldSurvey().Declaring(Primary, ("one.m2ts", 100));
        using IntegrityCheckJob job = Job(
            new HeldLedger(Ended(Bulk, "two.m2ts", 100)),
            survey,
            new HeldReports());

        await job.RunAsync(Cancel);

        Assert.Equal(["primary", "bulk"], survey.Asked);
    }

    [Fact]
    public async Task ARunWalksARootOnlyOnceHoweverManyRowsNameIt()
    {
        HeldSurvey survey = new HeldSurvey().Declaring(Primary, ("one.m2ts", 100));
        using IntegrityCheckJob job = Job(
            new HeldLedger(Ended(Primary, "one.m2ts", 100, 1), Ended(Primary, "two.m2ts", 100, 2)),
            survey,
            new HeldReports());

        await job.RunAsync(Cancel);

        Assert.Equal(["primary"], survey.Asked);
    }

    [Fact]
    public async Task ASecondRunIsRefusedWhileTheFirstIsStillGoing()
    {
        var ledger = new HeldLedger(Ended(Primary, "one.m2ts", 100)) { Gate = new TaskCompletionSource() };
        using IntegrityCheckJob job = Job(ledger, new HeldSurvey(), new HeldReports());

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
    public async Task ARunIsAllowedAgainOnceTheOneBeforeItHasFinished()
    {
        var ledger = new HeldLedger(Ended(Primary, "one.m2ts", 100));
        using IntegrityCheckJob job = Job(ledger, new HeldSurvey(), new HeldReports());

        Assert.False((await job.RunAsync(Cancel)).AlreadyRunning);
        Assert.False((await job.RunAsync(Cancel)).AlreadyRunning);
        Assert.Equal(2, ledger.Reads);
    }

    [Fact]
    public async Task ARunThatThrowsStillLetsTheNextOneStart()
    {
        var ledger = new HeldLedger(Ended(Primary, "one.m2ts", 100)) { Gate = new TaskCompletionSource() };
        using IntegrityCheckJob job = Job(ledger, new HeldSurvey(), new HeldReports());

        ledger.Gate!.SetException(new InvalidOperationException("the ledger could not be read"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.RunAsync(Cancel));

        ledger.Gate = null;

        Assert.False((await job.RunAsync(Cancel)).AlreadyRunning);
    }

    [Fact]
    public async Task TheLoopSweepsOnceItsFirstWaitIsOverAndKeepsSweepingAfterThat()
    {
        var clock = new HurriedClock();
        var reports = new HeldReports();
        using IntegrityCheckJob job = Job(
            new HeldLedger(Ended(Primary, "one.m2ts", 100)),
            new HeldSurvey().Declaring(Primary, ("one.m2ts", 99)),
            reports,
            new IntegritySettings
            {
                BeforeFirstSweep = TimeSpan.FromMinutes(3),
                BetweenSweeps = TimeSpan.FromHours(2),
                OutputRoots = [new StorageRootPath(Primary, "/srv/recordings")],
            },
            clock);
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => reports.Saved.Count >= 2, "the loop never swept twice");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal(
            [TimeSpan.FromMinutes(3), TimeSpan.FromHours(2)],
            clock.Waits.Take(2).ToArray());
    }

    [Fact]
    public async Task TheLoopDoesNotStartWhenNoRootIsMountedIntoThisProcess()
    {
        var reports = new HeldReports();
        var ledger = new HeldLedger(Ended(Primary, "one.m2ts", 100));
        using IntegrityCheckJob job = Job(
            ledger,
            new HeldSurvey().Declaring(Primary, ("one.m2ts", 99)),
            reports,
            new IntegritySettings { OutputRoots = [] },
            new HurriedClock());
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await job.ExecuteTask!.WaitAsync(Eventually.Patience);
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Empty(reports.Saved);
        Assert.Equal(0, ledger.Reads);
    }

    [Fact]
    public async Task TheLoopStillSweepsByHandWhenNoRootIsMounted()
    {
        var reports = new HeldReports();
        using IntegrityCheckJob job = Job(
            new HeldLedger(),
            new HeldSurvey(),
            reports,
            new IntegritySettings { OutputRoots = [] },
            new HurriedClock());

        Assert.False((await job.RunAsync(Cancel)).AlreadyRunning);
        Assert.Single(reports.Saved);
    }

    private static IntegrityCheckJob Job(
        HeldLedger ledger,
        HeldSurvey survey,
        HeldReports reports,
        IntegritySettings? settings = null,
        TimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IRecordingLedger>(_ => ledger);

        return new IntegrityCheckJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            survey,
            reports,
            settings ?? new IntegritySettings
            {
                OutputRoots = [new StorageRootPath(Primary, "/srv/recordings")],
            },
            clock ?? new StoppedClock(Now),
            NullLogger<IntegrityCheckJob>.Instance);
    }
}
