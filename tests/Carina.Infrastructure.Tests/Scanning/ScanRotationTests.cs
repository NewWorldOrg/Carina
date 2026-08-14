using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.Infrastructure.Scanning;

namespace Carina.Infrastructure.Tests.Scanning;

public sealed class ScanRotationTests
{
    private const int SomeNetworkId = SyntheticStream.SomeNetworkId;
    private const int SomeStreamId = 50002;
    private const int SomeServiceId = 50101;

    private static readonly TuningParameters Channel53 = TuningParameters.Terrestrial(53);
    private static readonly DateTime At = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly ScanSettings BacksOffTwice = ScanSettings.Default with
    {
        Rotation = new RotationBackoff(TimeSpan.FromMinutes(1), 2, TimeSpan.FromHours(1), 3),
    };

    [Fact]
    public async Task AChannelThatKeepsFailingWaitsLongerEachTimeBeforeItIsTriedAgain()
    {
        var harness = Failing();
        var candidate = harness.Candidates.Candidates[0];

        await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        var first = candidate.NextAttemptAt!.Value - At;

        harness.Runs.Runs.Clear();
        await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        var second = candidate.NextAttemptAt!.Value - At;

        Assert.Equal(RotationState.BackingOff, candidate.RotationState);
        Assert.Equal(TimeSpan.FromMinutes(1), first);
        Assert.Equal(TimeSpan.FromMinutes(2), second);
    }

    [Fact]
    public async Task AChannelThatFailsUpToTheCeilingLeavesTheRotationAndSaysSoOutLoud()
    {
        var harness = Failing();
        var candidate = harness.Candidates.Candidates[0];

        for (var round = 0; round < 2; round++)
        {
            harness.Runs.Runs.Clear();
            await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        }

        var outcome = await LastRun(harness);
        var departure = Assert.Single(outcome.Difference.Departures);

        Assert.Equal(RotationState.NeedsAttention, candidate.RotationState);
        Assert.False(candidate.IsInRotation);
        Assert.Equal(Channel53, departure.Tuning);
        Assert.Equal(SomeServiceId, departure.ServiceId.Value);
        Assert.Equal(3, departure.ConsecutiveFailures);
    }

    [Fact]
    public async Task AChannelThatLeftTheRotationIsFoundAgainAmongThoseNeedingAttention()
    {
        var harness = Failing();

        for (var round = 0; round < 3; round++)
        {
            harness.Runs.Runs.Clear();
            await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        }

        Assert.Single(await harness.Candidates.ListNeedingAttentionAsync(Cancel));
        Assert.Empty(await harness.Candidates.ListInRotationAsync(At, Cancel));
    }

    [Fact]
    public async Task ADepartureIsAnnouncedOnlyOnTheRunThatCausedIt()
    {
        var harness = Failing();

        harness.Runs.Runs.Clear();
        var first = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        harness.Runs.Runs.Clear();
        var second = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        harness.Runs.Runs.Clear();
        var third = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        harness.Runs.Runs.Clear();
        var fourth = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        Assert.Empty(first.Difference.Departures);
        Assert.Empty(second.Difference.Departures);
        Assert.Single(third.Difference.Departures);
        Assert.Empty(fourth.Difference.Departures);
    }

    [Fact]
    public async Task AChannelThatCarriesItsServiceAgainIsPutBackIntoTheRotation()
    {
        var harness = Failing();
        var candidate = harness.Candidates.Candidates[0];

        await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        Assert.Equal(RotationState.BackingOff, candidate.RotationState);

        harness.Runs.Runs.Clear();
        harness.Driver.Script(Channel53, ChannelScript.Carrying(SyntheticStream.Carrying(
            SomeStreamId,
            new SyntheticService(SomeServiceId, "Carina One"))));

        await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        Assert.Equal(RotationState.Active, candidate.RotationState);
        Assert.Equal(0, candidate.ConsecutiveFailures);
        Assert.NotNull(candidate.LastMeasurement);
        Assert.Equal(21_500, candidate.LastMeasurement!.CnrMilliDecibels);
    }

    [Fact]
    public async Task AChannelOnAStreamThatNoLongerNamesTheServiceIsCountedAsAFailure()
    {
        var harness = new ScanHarness(
            new ScriptedDriverClient().Script(Channel53, ChannelScript.Carrying(SyntheticStream.Carrying(
                SomeStreamId,
                new SyntheticService(50109, "Carina Nine")))),
            settings: BacksOffTwice);
        Store(harness);

        await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        Assert.Equal(1, harness.Candidates.Candidates[0].ConsecutiveFailures);
    }

    [Fact]
    public async Task AChannelNoScanWalkedIsLeftWhereItWas()
    {
        var harness = Failing();

        await harness.Orchestrator.RunAsync(
            ScanScope.Over([TuningParameters.Terrestrial(55)]),
            Cancel);

        Assert.Equal(RotationState.Active, harness.Candidates.Candidates[0].RotationState);
        Assert.Equal(0, harness.Candidates.Saves);
    }

    private static ScanHarness Failing()
    {
        var harness = new ScanHarness(
            new ScriptedDriverClient().Script(Channel53, ChannelScript.NoLock()),
            settings: BacksOffTwice);
        Store(harness);

        return harness;
    }

    private static async Task<ScanOutcome> LastRun(ScanHarness harness)
    {
        harness.Runs.Runs.Clear();

        return await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
    }

    private static void Store(ScanHarness harness)
    {
        var networkId = new NetworkId(SomeNetworkId);
        var serviceId = new ServiceId(SomeServiceId);

        harness.Services.Services.Add(
            BroadcastService.Discover(networkId, serviceId, "Carina One", ServiceCategory.Television, At));
        harness.Candidates.Candidates.Add(
            CandidateChannel.Discover(CandidateChannelId.New(), networkId, serviceId, Channel53, At));
    }
}
