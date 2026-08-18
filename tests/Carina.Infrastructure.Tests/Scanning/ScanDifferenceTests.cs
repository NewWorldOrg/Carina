using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.Infrastructure.Scanning;

namespace Carina.Infrastructure.Tests.Scanning;

public sealed class ScanDifferenceTests
{
    private const int SomeNetworkId = SyntheticStream.SomeNetworkId;
    private const int SomeStreamId = 50002;
    private const int SomeServiceId = 50101;
    private const int AnotherServiceId = 50102;
    private const int AThirdServiceId = 50103;

    private static readonly TuningParameters Channel53 = TuningParameters.Terrestrial(53);
    private static readonly TuningParameters Channel55 = TuningParameters.Terrestrial(55);
    private static readonly DateTime At = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task AServiceNobodyHasStoredYetIsProposedAsAnAddition()
    {
        ScanHarness harness = Harness(Carrying(new SyntheticService(SomeServiceId, "Carina One")));

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        ScanServiceChange added = Assert.Single(outcome.Difference.Added);

        Assert.Equal(SomeServiceId, added.ServiceId.Value);
        Assert.Equal("Carina One", added.Name);
        Assert.Equal(Channel53, Assert.Single(added.Channels).Tuning);
        Assert.Equal(SomeStreamId, added.Channels[0].TransportStreamId!.Value);
    }

    [Fact]
    public async Task AServiceReachedOnTwoTerrestrialChannelsIsProposedWithBoth()
    {
        ChannelScript script = Carrying(new SyntheticService(SomeServiceId, "Carina One"));
        var harness = new ScanHarness(new ScriptedDriverClient()
            .Script(Channel53, script)
            .Script(Channel55, script));

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(
            ScanScope.Over([Channel53, Channel55]),
            Cancel);
        ScanServiceChange added = Assert.Single(outcome.Difference.Added);

        Assert.Equal(
            [Channel53, Channel55],
            added.Channels.Select(channel => channel.Tuning));
    }

    [Fact]
    public async Task ASatelliteSlotCarryingAStreamAlreadyReachedIsProposedOnceOnly()
    {
        var twin = TuningParameters.Bs(9, new TransportStreamId(50004));
        var other = TuningParameters.Bs(11, new TransportStreamId(50004));
        var script = ChannelScript.Carrying(
            SyntheticStream.Carrying(50004, new SyntheticService(SomeServiceId, "Carina One")));
        var harness = new ScanHarness(new ScriptedDriverClient()
            .Script(twin, script)
            .Script(other, script));

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Over([twin, other]), Cancel);
        ScanServiceChange added = Assert.Single(outcome.Difference.Added);

        Assert.Equal([twin], added.Channels.Select(channel => channel.Tuning));
    }

    [Fact]
    public async Task AServiceThatChangedItsNameIsProposedAsAnUpdateRatherThanAsANewOne()
    {
        ScanHarness harness = Harness(Carrying(new SyntheticService(SomeServiceId, "Carina One Renamed")));
        Store(harness, SomeServiceId, "Carina One", Channel53);

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        ScanServiceChange updated = Assert.Single(outcome.Difference.Updated);

        Assert.Equal("Carina One Renamed", updated.Name);
        Assert.Empty(updated.Channels);
        Assert.Empty(outcome.Difference.Added);
    }

    [Fact]
    public async Task AStoredServiceTheScanReachedAndDidNotFindIsProposedAsMissing()
    {
        ScanHarness harness = Harness(Carrying(new SyntheticService(SomeServiceId, "Carina One")));
        Store(harness, AnotherServiceId, "Carina Two", Channel53);

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        ScanServiceChange missing = Assert.Single(outcome.Difference.Missing);

        Assert.Equal(AnotherServiceId, missing.ServiceId.Value);
        Assert.Equal(Channel53, Assert.Single(missing.Channels).Tuning);

        Assert.False(missing.Seen);
    }

    [Fact]
    public async Task AStoredServiceOnAChannelTheScanCouldNotReachIsLeftAlone()
    {
        ScanHarness harness = Harness(Carrying(new SyntheticService(SomeServiceId, "Carina One")));
        harness.Driver.Script(Channel55, ChannelScript.NoLock());
        Store(harness, AnotherServiceId, "Carina Two", Channel55);

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(
            ScanScope.Over([Channel53, Channel55]),
            Cancel);

        Assert.Empty(outcome.Difference.Missing);
        Assert.Empty(outcome.Difference.Updated);
        Assert.Single(outcome.Difference.Added);
    }

    [Fact]
    public async Task AScanThatOnlyWalkedOneSystemProposesNothingAboutTheOthers()
    {
        var slot = TuningParameters.Bs(9, new TransportStreamId(50004));
        ScanHarness harness = Harness(Carrying(new SyntheticService(SomeServiceId, "Carina One")));
        Store(harness, AnotherServiceId, "Carina Two", slot);

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        Assert.Empty(outcome.Difference.Missing);
        Assert.Equal([SomeServiceId], outcome.Difference.Added.Select(change => change.ServiceId.Value));
    }

    [Fact]
    public async Task ANewChannelForAStoredServiceIsProposedWithoutTouchingTheOneAlreadyThere()
    {
        ScanHarness harness = Harness(Carrying(new SyntheticService(SomeServiceId, "Carina One")));
        harness.Driver.Script(Channel55, ChannelScript.Carrying(SyntheticStream.Carrying(
            50003,
            new SyntheticService(SomeServiceId, "Carina One"))));
        Store(harness, SomeServiceId, "Carina One", Channel53);

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(
            ScanScope.Over([Channel53, Channel55]),
            Cancel);

        ScanServiceChange updated = Assert.Single(outcome.Difference.Updated);
        ScanChannelChange channel = Assert.Single(updated.Channels);

        Assert.Equal(ScanChangeKind.Added, channel.Kind);
        Assert.Equal(Channel55, channel.Tuning);
    }

    [Fact]
    public async Task TheDifferenceIsProposedAndNothingIsWrittenToTheDefinitions()
    {
        ScanHarness harness = Harness(Carrying(
            new SyntheticService(SomeServiceId, "Carina One Renamed"),
            new SyntheticService(AThirdServiceId, "Carina Three")));
        Store(harness, SomeServiceId, "Carina One", Channel53);
        Store(harness, AnotherServiceId, "Carina Two", Channel55);

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        Assert.Equal(2, harness.Services.Services.Count);
        Assert.Equal(2, harness.Candidates.Candidates.Count);
        Assert.Equal("Carina One", harness.Services.Services[0].Name);
        Assert.Equal(
            [ScanChangeKind.Updated, ScanChangeKind.Added],
            outcome.Difference.Services.Select(change => change.Kind));
    }

    [Fact]
    public async Task AnInterruptedScanProposesNothingAtAll()
    {
        var stream = PacedStream.InChunksOf(
            SyntheticStream.Carrying(SomeStreamId, new SyntheticService(SomeServiceId, "Carina One")).ToBytes(),
            188);
        var harness = new ScanHarness(
            new ScriptedDriverClient().Script(Channel53, new ChannelScript { Paced = () => stream }));
        Store(harness, SomeServiceId, "Carina One", Channel53);

        Task<ScanOutcome> scan = Task.Run(() => harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel), Cancel);

        stream.AwaitParkedBefore(1);
        harness.RestartTheDriver();

        ScanOutcome outcome = await scan;

        Assert.Equal(ScanRunState.Interrupted, outcome.State);
        Assert.True(outcome.Difference.ChangesNothing);
        Assert.Equal(0, harness.Candidates.Saves);
    }

    [Fact]
    public async Task EveryScanTellsTheScreensSomethingChanged()
    {
        ScanHarness harness = Harness(Carrying(new SyntheticService(SomeServiceId, "Carina One")));

        await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        Assert.Equal(["tuners"], harness.Events.Signalled);
    }

    private static ScanHarness Harness(ChannelScript script)
        => new(new ScriptedDriverClient().Script(Channel53, script));

    private static ChannelScript Carrying(params SyntheticService[] services)
        => ChannelScript.Carrying(SyntheticStream.Carrying(SomeStreamId, services));

    private static void Store(ScanHarness harness, int serviceId, string name, TuningParameters tuning)
    {
        var networkId = new NetworkId(SomeNetworkId);
        var service = new ServiceId(serviceId);

        harness.Services.Services.Add(
            BroadcastService.Discover(networkId, service, name, ServiceCategory.Television, At));
        CandidateChannel candidate = CandidateChannel.Discover(
            CandidateChannelId.New(),
            networkId,
            service,
            tuning,
            At);

        candidate.CarriedBy(new TransportStreamId(SomeStreamId));

        harness.Candidates.Candidates.Add(candidate);
    }
}
