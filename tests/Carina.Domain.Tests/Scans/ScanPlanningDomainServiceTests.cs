using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.TestSupport;

using Puppis.Contracts;

namespace Carina.Domain.Tests.Scans;

public sealed class ScanPlanningDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly HeldSatelliteStreams satelliteStreams = new();

    [Fact(DisplayName = "BR-TD-010: a scan walks only the systems a tuner in service receives")]
    public async Task AScanWalksOnlyWhatATunerInServiceReceives()
    {
        ScanPlan? plan = await Planning(Seats(TunerKind.Terrestrial)).PlanAsync(ScanScope.Everything, Cancel);

        Assert.NotNull(plan);
        Assert.Equal([TuneSystem.IsdbT], plan.Scope.Systems);
        Assert.All(plan.Targets, target => Assert.Equal(TuneSystem.IsdbT, target.System));
    }

    [Fact(DisplayName = "BR-TD-010: a scan nothing in service receives is not planned")]
    public async Task AScanNothingReceivesIsNotPlanned()
        => Assert.Null(await Planning(Seats(TunerKind.Terrestrial)).PlanAsync(ScanScope.Of(TuneSystem.IsdbSCs110), Cancel));

    [Fact(DisplayName = "BR-TD-010: while what a tuner receives is undetermined, or the tuners cannot be read, every system asked for is walked")]
    public async Task EverySystemIsWalkedWhileTheTunersAreNotKnown()
    {
        TunerCapacity undetermined = new([new TunerSeat("adapter0", [TuneSystem.IsdbT], false)], ["adapter1"]);

        ScanPlan? unsure = await Planning(undetermined).PlanAsync(ScanScope.Of(TuneSystem.IsdbSCs110), Cancel);
        ScanPlan? unread = await Planning(null).PlanAsync(ScanScope.Of(TuneSystem.IsdbSCs110), Cancel);

        Assert.Equal([TuneSystem.IsdbSCs110], unsure?.Scope.Systems);
        Assert.Equal([TuneSystem.IsdbSCs110], unread?.Scope.Systems);
    }

    [Fact(DisplayName = "a terrestrial scan walks every physical channel in order")]
    public async Task ATerrestrialScanWalksEveryPhysicalChannel()
    {
        ScanPlan? plan = await Planning(Seats(TunerKind.Terrestrial)).PlanAsync(ScanScope.Of(TuneSystem.IsdbT), Cancel);

        Assert.Equal(
            Enumerable.Range(BroadcastStandards.TerrestrialFirstChannel, BroadcastStandards.TerrestrialLastChannel - BroadcastStandards.TerrestrialFirstChannel + 1),
            plan!.Targets.Select(target => target.PhysicalChannel));
    }

    [Fact(DisplayName = "a BS scan walks the streams known, by slot and then by stream number")]
    public async Task ABsScanWalksTheStreamsKnown()
    {
        SatelliteTransportStream later = SatelliteTransportStream.Rehydrate(3, 1, new TransportStreamId(0x4031));
        SatelliteTransportStream second = SatelliteTransportStream.Rehydrate(1, 1, new TransportStreamId(0x4011));
        SatelliteTransportStream first = SatelliteTransportStream.Rehydrate(1, 0, new TransportStreamId(0x4010));
        satelliteStreams.Streams.AddRange([later, second, first]);

        ScanPlan? plan = await Planning(Seats(TunerKind.Satellite)).PlanAsync(ScanScope.Of(TuneSystem.IsdbSBs), Cancel);

        Assert.Equal([first.ToTuningParameters(), second.ToTuningParameters(), later.ToTuningParameters()], plan!.Targets);
    }

    [Fact(DisplayName = "a CS scan walks every other slot")]
    public async Task ACsScanWalksEveryOtherSlot()
    {
        ScanPlan? plan = await Planning(Seats(TunerKind.Satellite)).PlanAsync(ScanScope.Of(TuneSystem.IsdbSCs110), Cancel);

        Assert.Equal(
            Enumerable.Range(0, ((BroadcastStandards.Cs110LastChannel - BroadcastStandards.Cs110FirstChannel) / 2) + 1)
                .Select(step => BroadcastStandards.Cs110FirstChannel + (step * 2)),
            plan!.Targets.Select(target => target.PhysicalChannel));
    }

    [Fact(DisplayName = "BR-TD-010: a scan over named targets keeps only those a tuner in service receives")]
    public async Task AScanOverNamedTargetsKeepsWhatIsReceived()
    {
        ScanScope named = ScanScope.Over([TuningParameters.Terrestrial(27), TuningParameters.Cs110(2)]);

        ScanPlan? plan = await Planning(Seats(TunerKind.Terrestrial)).PlanAsync(named, Cancel);

        Assert.Equal([TuningParameters.Terrestrial(27)], plan!.Targets);
    }

    private ScanPlanningDomainService Planning(TunerCapacity? capacity) => new(new HeldCapacity(capacity), satelliteStreams);

    private static TunerCapacity Seats(TunerKind kind) => new([new TunerSeat("adapter0", BroadcastReception.Of(kind), false)], []);

    private sealed class HeldCapacity(TunerCapacity? capacity) : ITunerCapacityDirectory
    {
        public Task<TunerCapacity?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(capacity);
    }
}
