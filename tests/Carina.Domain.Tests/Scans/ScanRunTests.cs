using Carina.Domain.Scans;

namespace Carina.Domain.Tests.Scans;

public sealed class ScanRunTests
{
    private static readonly DateTime At = new(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);

    private static ScanRun Started() => ScanRun.Start(ScanRunId.New(), "instance-a", At);

    [Fact]
    public void AStartedScanIsRunningAndHasNotFinished()
    {
        ScanRun run = Started();

        Assert.Equal(ScanRunState.Running, run.State);
        Assert.True(run.IsRunning);
        Assert.Null(run.FinishedAt);
        Assert.Null(run.Reason);
    }

    [Fact]
    public void AScanRemembersWhichDriverItStartedAgainst()
    {
        Assert.Equal("instance-a", Started().DriverInstanceId);
    }

    [Fact]
    public void CompletingRecordsWhenItFinished()
    {
        ScanRun run = Started();

        run.Complete(At.AddMinutes(4));

        Assert.Equal(ScanRunState.Completed, run.State);
        Assert.Equal(At.AddMinutes(4), run.FinishedAt);
        Assert.Null(run.Reason);
    }

    [Fact]
    public void FailingWithoutSayingWhyIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Started().Fail("  ", At));
        Assert.Throws<ArgumentNullException>(() => Started().Fail(null!, At));
    }

    [Fact]
    public void CancellingWithoutSayingWhyIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Started().Cancel(string.Empty, At));
    }

    [Fact]
    public void AFailureKeepsTheStatedReason()
    {
        ScanRun run = Started();

        run.Fail("every tuner was busy for longer than the bounded wait", At.AddMinutes(1));

        Assert.Equal(ScanRunState.Failed, run.State);
        Assert.Equal("every tuner was busy for longer than the bounded wait", run.Reason);
    }

    [Fact]
    public void ADriverThatCameBackAsAnotherInstanceInterruptsTheScan()
    {
        ScanRun run = Started();

        run.Interrupt(At.AddMinutes(2));

        Assert.Equal(ScanRunState.Interrupted, run.State);
        Assert.Equal(At.AddMinutes(2), run.FinishedAt);
    }

    [Fact]
    public void AScanLeavesRunningOnlyOnce()
    {
        ScanRun run = Started();
        run.Cancel("the operator asked for it", At.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => run.Complete(At.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => run.Interrupt(At.AddMinutes(2)));
    }

    [Fact]
    public void ARehydratedRunThatEndedNamesWhenItDid()
    {
        Assert.Throws<ArgumentException>(() => ScanRun.Rehydrate(
            ScanRunId.New(), ScanRunState.Completed, "instance-a", At, null, null));
        Assert.Throws<ArgumentException>(() => ScanRun.Rehydrate(
            ScanRunId.New(), ScanRunState.Running, "instance-a", At, At, null));
    }

    [Fact]
    public void ARehydratedFailureOrCancellationCarriesItsReason()
    {
        Assert.Throws<ArgumentException>(() => ScanRun.Rehydrate(
            ScanRunId.New(), ScanRunState.Failed, "instance-a", At, At, null));
        Assert.Throws<ArgumentException>(() => ScanRun.Rehydrate(
            ScanRunId.New(), ScanRunState.Cancelled, "instance-a", At, At, "   "));
    }

    [Fact]
    public void AReasonLongerThanTheColumnIsRefusedBeforeItReachesTheDatabase()
    {
        Assert.Throws<ArgumentException>(
            () => Started().Fail(new string('x', ScanRun.ReasonMaxLength + 1), At));
    }

    [Fact]
    public void TimesArriveInUtcOrNotAtAll()
    {
        Assert.Throws<ArgumentException>(
            () => ScanRun.Start(ScanRunId.New(), null, new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Local)));
    }

    [Fact(DisplayName = "BR-TS-001: only a completed scan proposes a difference and may be applied")]
    public void OnlyACompletedScanMayBeApplied()
    {
        ScanRun running = Started();
        ScanRun completed = Started();
        completed.Complete(At);
        ScanRun failed = Started();
        failed.Fail("every tuner was busy", At);
        ScanRun cancelled = Started();
        cancelled.Cancel("the scan was cancelled", At);
        ScanRun interrupted = Started();
        interrupted.Interrupt(At);

        Assert.Equal(ScanApplyRefusal.StillRunning, running.RefusalToApply());
        Assert.Null(completed.RefusalToApply());
        Assert.Equal(ScanApplyRefusal.NeverCompleted, failed.RefusalToApply());
        Assert.Equal(ScanApplyRefusal.NeverCompleted, cancelled.RefusalToApply());
        Assert.Equal(ScanApplyRefusal.NeverCompleted, interrupted.RefusalToApply());
        Assert.Equal([false, true, false, false, false], new[] { running, completed, failed, cancelled, interrupted }.Select(run => run.ProposesADifference));
    }
}
