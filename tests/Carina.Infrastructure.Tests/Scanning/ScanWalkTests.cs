using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.Infrastructure.Scanning;

namespace Carina.Infrastructure.Tests.Scanning;

public sealed class ScanWalkTests
{
    private const int SomeStreamId = 50002;
    private const int SomeServiceId = 50101;

    private static readonly TuningParameters Channel53 = TuningParameters.Terrestrial(53);
    private static readonly TuningParameters Channel55 = TuningParameters.Terrestrial(55);
    private static readonly TuningParameters Channel57 = TuningParameters.Terrestrial(57);
    private static readonly TuningParameters Slot9 =
        TuningParameters.Bs(9, new TransportStreamId(SomeStreamId));

    private static readonly TuningParameters Slot11 =
        TuningParameters.Bs(11, new TransportStreamId(SomeStreamId));
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task TwoSatelliteSlotsCarryingTheOneStreamProposeThatStreamOnce()
    {
        var carrying = ChannelScript.Carrying(SyntheticStream.Carrying(
            SomeStreamId,
            new SyntheticService(SomeServiceId, "Carina One")));
        ScriptedDriverClient driver = new ScriptedDriverClient()
            .Script(Slot9, carrying)
            .Script(Slot11, carrying);

        ScanOutcome outcome = await new ScanHarness(driver).Orchestrator.RunAsync(
            ScanScope.Over([Slot9, Slot11]),
            Cancel);

        Assert.Equal(2, outcome.Attempts.Count);
        Assert.Empty(outcome.Failures);

        ScanServiceChange added = Assert.Single(outcome.Difference.Added);

        Assert.Equal(Slot9, Assert.Single(added.Channels).Tuning);
        Assert.Contains(
            ScanTargetNames.Of(Slot9),
            outcome.Attempts[1].Detail!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSameServiceOnTwoStreamsKeepsBothAsCandidates()
    {
        ScriptedDriverClient driver = new ScriptedDriverClient()
            .Script(Channel53, ChannelScript.Carrying(SyntheticStream.Carrying(
                SomeStreamId,
                new SyntheticService(SomeServiceId, "Carina One"))))
            .Script(Channel55, ChannelScript.Carrying(SyntheticStream.Carrying(
                50003,
                new SyntheticService(SomeServiceId, "Carina One"))));

        ScanOutcome outcome = await new ScanHarness(driver).Orchestrator.RunAsync(
            ScanScope.Over([Channel53, Channel55]),
            Cancel);

        ScanServiceChange added = Assert.Single(outcome.Difference.Added);

        Assert.Equal([Channel53, Channel55], added.Channels.Select(channel => channel.Tuning));
    }

    [Fact]
    public async Task ABusyTunerIsWaitedForOnAWideningIntervalRatherThanAtAFixedOne()
    {
        var clock = new HurriedClock();
        ScriptedDriverClient driver = new ScriptedDriverClient
        {
            BusyRefusalsRemaining = 3,
        }.Script(Channel53, ChannelScript.Carrying(SyntheticStream.Carrying(
            SomeStreamId,
            new SyntheticService(SomeServiceId, "Carina One"))));

        ScanOutcome outcome = await Harness(driver, clock).Orchestrator.RunAsync(
            ScanScope.Over([Channel53]),
            Cancel);

        Assert.Equal([2d, 4, 8], clock.Waits.Select(wait => wait.TotalSeconds));
        Assert.Equal(ScanRunState.Completed, outcome.State);
    }

    [Fact]
    public async Task ATunerThatStaysBusyEndsTheScanWithAReasonInsteadOfRetryingForever()
    {
        var clock = new HurriedClock();
        var driver = new ScriptedDriverClient
        {
            BusyRefusalsRemaining = int.MaxValue,
        };

        ScanOutcome outcome = await Harness(driver, clock).Orchestrator.RunAsync(
            ScanScope.Over([Channel53, Channel55]),
            Cancel);

        Assert.Equal([2d, 4, 8, 16], clock.Waits.Select(wait => wait.TotalSeconds));
        Assert.Equal(ScanRunState.Failed, outcome.State);
        Assert.StartsWith(ChannelScanOrchestrator.BusyReason, outcome.Run!.Reason, StringComparison.Ordinal);
        Assert.Contains("Every usable tuner is busy.", outcome.Run.Reason, StringComparison.Ordinal);
        Assert.Empty(outcome.Attempts);
    }

    [Fact]
    public async Task AChannelThatDidNotLockIsRecordedAndTheSweepWalksOn()
    {
        ScriptedDriverClient driver = new ScriptedDriverClient()
            .Script(Channel53, ChannelScript.Carrying(SyntheticStream.Carrying(
                SomeStreamId,
                new SyntheticService(SomeServiceId, "Carina One"))))
            .Script(Channel55, new ChannelScript
            {
                Refusal = new DriverProblem(
                    "noLock",
                    ["/dev/dvb/adapter0/frontend0: the frontend did not lock within 5 seconds."]),
            })
            .Script(Channel57, ChannelScript.Carrying(SyntheticStream.Carrying(
                50003,
                new SyntheticService(50102, "Carina Two"))));

        ScanOutcome outcome = await new ScanHarness(driver).Orchestrator.RunAsync(
            ScanScope.Over([Channel53, Channel55, Channel57]),
            Cancel);

        Assert.Equal(ScanRunState.Completed, outcome.State);
        Assert.Equal(
            [ScanAttemptOutcome.Succeeded, ScanAttemptOutcome.NoLock, ScanAttemptOutcome.Succeeded],
            outcome.Attempts.Select(attempt => attempt.Outcome));
        Assert.Contains("did not lock", outcome.Attempts[1].Detail!, StringComparison.Ordinal);
        Assert.Equal(2, outcome.Difference.Added.Count);
    }

    [Fact]
    public async Task AStreamTheDriverToreDownIsRecordedOnItsBytesAndTheSweepWalksOn()
    {
        ScriptedDriverClient driver = new ScriptedDriverClient()
            .Script(Channel53, ChannelScript.Carrying(SyntheticStream.Carrying(
                SomeStreamId,
                new SyntheticService(SomeServiceId, "Carina One"))))
            .Script(Channel55, new ChannelScript { Paced = () => PacedStream.Torn() })
            .Script(Channel57, ChannelScript.Carrying(SyntheticStream.Carrying(
                50003,
                new SyntheticService(50102, "Carina Two"))));

        ScanOutcome outcome = await new ScanHarness(driver).Orchestrator.RunAsync(
            ScanScope.Over([Channel53, Channel55, Channel57]),
            Cancel);

        Assert.Equal(ScanRunState.Completed, outcome.State);
        Assert.Equal(
            [
                ScanAttemptOutcome.Succeeded,
                ScanAttemptOutcome.LockedWithoutData,
                ScanAttemptOutcome.Succeeded,
            ],
            outcome.Attempts.Select(attempt => attempt.Outcome));
    }

    [Fact]
    public async Task AnAttemptThatRunsOutOfPatienceIsRecordedOnWhatItManagedToRead()
    {
        var clock = new HeldClock();
        var stream = PacedStream.InChunksOf(
            SyntheticStream.Carrying(SomeStreamId, new SyntheticService(SomeServiceId, "Carina One")).ToBytes(),
            188);
        ScriptedDriverClient driver = new ScriptedDriverClient().Script(Channel53, new ChannelScript { Paced = () => stream });
        var harness = new ScanHarness(driver, clock);

        Task<ScanOutcome> scan = Task.Run(() => harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel), Cancel);

        stream.Allow(1);
        stream.AwaitParkedBefore(2);
        clock.FireOnePending("the patience of the attempt");

        ScanOutcome outcome = await scan;

        Assert.Equal(ScanAttemptOutcome.IncompleteTables, Assert.Single(outcome.Attempts).Outcome);
        Assert.Equal(ScanRunState.Completed, outcome.State);
    }

    [Fact]
    public async Task ADriverThatComesBackAsAnotherInstanceEndsTheScanRatherThanFinishingOnHalfOfIt()
    {
        var stream = PacedStream.InChunksOf(
            SyntheticStream.Carrying(SomeStreamId, new SyntheticService(SomeServiceId, "Carina One")).ToBytes(),
            188);
        ScriptedDriverClient driver = new ScriptedDriverClient().Script(Channel53, new ChannelScript { Paced = () => stream });
        var harness = new ScanHarness(driver);

        Task<ScanOutcome> scan = Task.Run(
            () => harness.Orchestrator.RunAsync(ScanScope.Over([Channel53, Channel55]), Cancel),
            Cancel);

        stream.AwaitParkedBefore(1);
        harness.RestartTheDriver();

        ScanOutcome outcome = await scan;

        Assert.Equal(ScanRunState.Interrupted, outcome.State);
        Assert.Equal([Channel53], driver.Started);
        Assert.True(outcome.Difference.ChangesNothing);
    }

    [Fact]
    public async Task ADriverThatGoesAwayMidWalkFailsTheScanInsteadOfProposingWhatItAlreadyHas()
    {
        var carrying = ChannelScript.Carrying(SyntheticStream.Carrying(
            SomeStreamId,
            new SyntheticService(SomeServiceId, "Carina One")));
        ScriptedDriverClient driver = new ScriptedDriverClient
        {
            UnreachableFrom = "the socket went away",
        }
            .Script(Channel53, carrying)
            .Script(Channel55, carrying);

        ScanOutcome outcome = await new ScanHarness(driver).Orchestrator.RunAsync(
            ScanScope.Over([Channel53, Channel55, Channel57]),
            Cancel);

        Assert.Equal(ScanRunState.Failed, outcome.State);
        Assert.Equal("the socket went away", outcome.Run!.Reason);
        Assert.True(outcome.Difference.ChangesNothing);
    }

    [Fact]
    public async Task ASecondScanIsRefusedAndNamesTheOneAlreadyRunning()
    {
        var harness = new ScanHarness(new ScriptedDriverClient());
        var running = ScanRun.Start(ScanRunId.New(), "instance-a", DateTime.UtcNow);
        harness.Runs.Runs.Add(running);

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        Assert.False(outcome.WasStarted);
        Assert.Equal(running.Id, outcome.AlreadyRunning);
    }

    [Fact]
    public async Task AScanIsNotStartedAtAllWhileTheDriverIsOutOfReach()
    {
        var harness = new ScanHarness(new ScriptedDriverClient { GreetingFailure = "no socket" });

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        Assert.False(outcome.WasStarted);
        Assert.Equal("no socket", outcome.CouldNotStartBecause);
        Assert.Empty(harness.Runs.Runs);
    }

    [Fact]
    public async Task TheWholeRangeOfASystemIsWalkedWhenNoTargetsAreNamed()
    {
        var harness = new ScanHarness(new ScriptedDriverClient());

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Of(TuneSystem.IsdbT), Cancel);

        Assert.Equal(50, outcome.Attempts.Count);
        Assert.Equal(
            TuningParameters.Terrestrial(BroadcastStandards.TerrestrialFirstChannel),
            outcome.Attempts[0].Tuning);
        Assert.Equal(
            TuningParameters.Terrestrial(BroadcastStandards.TerrestrialLastChannel),
            outcome.Attempts[^1].Tuning);
    }

    [Fact]
    public async Task TheSatelliteSlotsWalkedAreTheStreamsAlreadyRecordedForThem()
    {
        var harness = new ScanHarness(new ScriptedDriverClient());
        harness.SatelliteStreams.Streams.Add(
            SatelliteTransportStream.Rehydrate(9, 0, new TransportStreamId(SomeStreamId)));
        harness.SatelliteStreams.Streams.Add(
            SatelliteTransportStream.Rehydrate(3, 0, new TransportStreamId(50003)));

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Of(TuneSystem.IsdbSBs), Cancel);

        Assert.Equal(
            [
                TuningParameters.Bs(3, new TransportStreamId(50003)),
                TuningParameters.Bs(9, new TransportStreamId(SomeStreamId)),
            ],
            outcome.Attempts.Select(attempt => attempt.Tuning));
    }

    private static ScanHarness Harness(ScriptedDriverClient driver, TimeProvider clock)
        => new(
            driver,
            clock,
            ScanSettings.Default with { AttemptPatience = Timeout.InfiniteTimeSpan });
}
