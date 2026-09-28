using Carina.BroadcastTestSupport;
using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.Infrastructure.Scanning;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Scanning;

public sealed class ScanReceptionTests
{
    private const int GroundStreamId = 50_001;
    private const int GroundServiceId = 50_111;
    private const int SkyNetworkId = 4;
    private const int SkyStreamId = 50_002;
    private const int SkyServiceId = 50_101;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TuningParameters Channel53 = TuningParameters.Terrestrial(53);

    private static readonly TuningParameters Slot9 = TuningParameters.Bs(9, new TransportStreamId(SkyStreamId));

    private static readonly TunerCapacity GroundOnly =
        new([new TunerSeat("adapter0", [TuneSystem.IsdbT], Faulted: false)], []);

    [Fact]
    public async Task ScanningEverythingWithTheSatelliteTunerOffWalksTheGroundAndNoSatelliteSlot()
    {
        ScanHarness harness = GroundAndSky(GroundOnly);

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Everything, Cancel);

        Assert.Equal(ScanRunState.Completed, outcome.State);
        Assert.Contains(Channel53, harness.Driver.Started);
        Assert.All(harness.Driver.Started, tuning => Assert.Equal(TuneSystem.IsdbT, tuning.System));
        Assert.All(outcome.Attempts, attempt => Assert.Equal(TuneSystem.IsdbT, attempt.Tuning.System));
    }

    [Fact]
    public async Task TheSatelliteDefinitionsAnUnwalkedSystemHoldsAreNeitherProposedAwayNorRemovedByTheApply()
    {
        ScanHarness harness = GroundAndSky(GroundOnly);

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Everything, Cancel);

        Assert.DoesNotContain(outcome.Difference.Services, change => change.ServiceId.Value == SkyServiceId);

        await new ScanApplier(
                harness.Services,
                harness.Candidates,
                new UnguardedWrites(),
                harness.Events,
                harness.Clock)
            .ApplyAsync(
                outcome.Difference,
                [.. outcome.Attempts.Select(attempt => attempt.Tuning.System).Distinct()],
                Cancel);

        Assert.NotNull(await harness.Services.FindAsync(
            new NetworkId(SkyNetworkId),
            new ServiceId(SkyServiceId),
            Cancel));
        Assert.Equal(
            Slot9,
            Assert.Single(await harness.Candidates.ListForServiceAsync(
                new NetworkId(SkyNetworkId),
                new ServiceId(SkyServiceId),
                Cancel)).Tuning);
        Assert.NotNull(await harness.Services.FindAsync(
            new NetworkId(SyntheticStream.SomeNetworkId),
            new ServiceId(GroundServiceId),
            Cancel));
    }

    [Fact]
    public async Task AScanOfOnlyTheSatelliteWithItsTunerOffDoesNotStartAndSaysWhichSystemsNoTunerReceives()
    {
        ScanHarness harness = GroundAndSky(GroundOnly);

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(
            ScanScope.Of(TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110),
            Cancel);

        Assert.False(outcome.WasStarted);
        Assert.True(outcome.NoTunerReceivesIt);
        Assert.Contains("isdbSBs", outcome.CouldNotStartBecause, StringComparison.Ordinal);
        Assert.Contains("isdbSCs110", outcome.CouldNotStartBecause, StringComparison.Ordinal);
        Assert.Empty(harness.Runs.Runs);
        Assert.Empty(harness.Driver.Started);
    }

    [Fact]
    public async Task NamedChannelsOfASystemNoTunerReceivesAreLeftOutOfTheWalk()
    {
        ScanHarness harness = GroundAndSky(GroundOnly);

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Over([Slot9, Channel53]), Cancel);

        Assert.Equal([Channel53], outcome.Attempts.Select(attempt => attempt.Tuning));
    }

    [Fact]
    public async Task TurningTheSatelliteTunerBackOnPutsItsSlotsBackIntoTheWalk()
    {
        TunerCapacity both = new(
            [
                new TunerSeat("adapter0", [TuneSystem.IsdbT], Faulted: false),
                new TunerSeat("adapter1", [TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110], Faulted: false),
            ],
            []);
        ScanHarness harness = GroundAndSky(both);

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Everything, Cancel);

        Assert.Contains(Slot9, outcome.Attempts.Select(attempt => attempt.Tuning));
    }

    [Fact]
    public async Task TunersWhoseKindNobodyKnowsLeaveTheScopeAsAskedRatherThanNarrowingItOnAGuess()
    {
        TunerCapacity unsure = new(
            [new TunerSeat("adapter0", [TuneSystem.IsdbT], Faulted: false)],
            ["adapter1"]);
        ScanHarness harness = GroundAndSky(unsure);

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Over([Slot9, Channel53]), Cancel);

        Assert.Equal([Slot9, Channel53], outcome.Attempts.Select(attempt => attempt.Tuning));
    }

    [Fact]
    public async Task ALedgerThatCannotBeReadLeavesTheScopeAsAsked()
    {
        ScanHarness harness = GroundAndSky(capacity: null);

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Over([Slot9, Channel53]), Cancel);

        Assert.Equal([Slot9, Channel53], outcome.Attempts.Select(attempt => attempt.Tuning));
    }

    private static ScanHarness GroundAndSky(TunerCapacity? capacity)
    {
        ScriptedDriverClient driver = new ScriptedDriverClient().Script(
            Channel53,
            ChannelScript.Carrying(SyntheticStream.Carrying(
                GroundStreamId,
                new SyntheticService(GroundServiceId, "Carina Ground")).ToBytes()));
        ScanHarness harness = new(driver, capacity: capacity);

        harness.SatelliteStreams.Streams.Add(SatelliteTransportStream.Rehydrate(9, 0, new TransportStreamId(SkyStreamId)));
        harness.Services.Services.Add(BroadcastService.Discover(
            new NetworkId(SkyNetworkId),
            new ServiceId(SkyServiceId),
            "Carina Sky",
            ServiceCategory.Television,
            StillClock.Now.UtcDateTime));
        harness.Candidates.Candidates.Add(CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(SkyNetworkId),
            new ServiceId(SkyServiceId),
            Slot9,
            StillClock.Now.UtcDateTime));

        return harness;
    }
}
