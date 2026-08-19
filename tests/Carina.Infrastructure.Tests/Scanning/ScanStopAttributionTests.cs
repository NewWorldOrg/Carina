using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Scanning;

public sealed class ScanStopAttributionTests
{
    private static readonly TuningParameters Channel53 = TuningParameters.Terrestrial(53);

    private static readonly TuningParameters Channel55 = TuningParameters.Terrestrial(55);

    private sealed class Stopping(ScanStop stop) : IScanRunObserver
    {
        public ScanStop Stop => stop;

        public void Started(ScanRun run)
        {
        }
    }

    private static async Task<ScanOutcome> StoppedAsync(ScanStop stop)
    {
        var stream = PacedStream.InChunksOf(
            SyntheticStream.Carrying(50002, new SyntheticService(50101, "Carina One")).ToBytes(),
            188);
        ScriptedDriverClient driver = new ScriptedDriverClient().Script(Channel53, new ChannelScript { Paced = () => stream });
        var harness = new ScanHarness(driver);

        using var stopping = new CancellationTokenSource();

        Task<ScanOutcome> scan = Task.Run(
            () => harness.Orchestrator.RunAsync(
                ScanScope.Over([Channel53, Channel55]),
                new Stopping(stop),
                stopping.Token),
            CancellationToken.None);

        stream.AwaitParkedBefore(1);
        await stopping.CancelAsync();

        return await scan;
    }

    [Fact]
    public async Task AScanTheOperatorStoppedIsRecordedAsCancelled()
    {
        ScanOutcome outcome = await StoppedAsync(ScanStop.AsRequested);

        Assert.Equal(ScanRunState.Cancelled, outcome.State);
        Assert.Equal(ScanConclusion.CancelledReason, outcome.Run!.Reason);
    }

    [Fact]
    public async Task ADeploymentRestartIsNotRecordedAsSomethingTheOperatorDid()
    {
        ScanOutcome outcome = await StoppedAsync(ScanStop.BecauseTheAppIsStopping);

        Assert.NotEqual(ScanRunState.Cancelled, outcome.State);
        Assert.Equal(ScanRunState.Failed, outcome.State);
        Assert.Equal(ScanConclusion.AppStoppingReason, outcome.Run!.Reason);
    }
}
