using Carina.Domain.Scans;

namespace Carina.Domain.Tests.Scans;

public sealed class ScanConclusionTests
{
    private static readonly DateTime At = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

    private static ScanRun Running() => ScanRun.Start(ScanRunId.New(), "instance-a", At);

    [Fact]
    public void AScanTheOperatorStoppedIsRecordedAsCancelled()
    {
        var run = Running();

        ScanConclusion.Stop(run, ScanStop.AsRequested, At);

        Assert.Equal(ScanRunState.Cancelled, run.State);
        Assert.Equal(ScanConclusion.CancelledReason, run.Reason);
    }

    [Fact]
    public void AScanTheAppStoppedIsNotRecordedAsSomethingTheOperatorDid()
    {
        var run = Running();

        ScanConclusion.Stop(run, ScanStop.BecauseTheAppIsStopping, At);

        Assert.NotEqual(ScanRunState.Cancelled, run.State);
        Assert.Equal(ScanRunState.Failed, run.State);
        Assert.Equal(ScanConclusion.AppStoppingReason, run.Reason);
    }

    [Fact]
    public void EveryWayAScanIsStoppedSaysWhy()
    {
        foreach (var stop in Enum.GetValues<ScanStop>())
        {
            var run = Running();

            ScanConclusion.Stop(run, stop, At);

            Assert.False(string.IsNullOrWhiteSpace(run.Reason));
        }
    }

    [Fact]
    public void AScanLeftBehindByAnEarlierProcessSaysThatIsWhatHappened()
    {
        var run = Running();

        ScanConclusion.Abandon(run, At);

        Assert.Equal(ScanRunState.Failed, run.State);
        Assert.Equal(ScanConclusion.AbandonedReason, run.Reason);
    }

    [Fact]
    public void AScanThatAlreadyEndedIsNotStoppedTwice()
    {
        var run = Running();

        ScanConclusion.Stop(run, ScanStop.AsRequested, At);

        Assert.Throws<InvalidOperationException>(
            () => ScanConclusion.Stop(run, ScanStop.BecauseTheAppIsStopping, At));
    }
}
