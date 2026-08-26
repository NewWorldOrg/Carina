using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Infrastructure.Channels;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Channels;

public sealed class ServiceTuningDirectoryTests
{
    private static readonly DateTime At = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly NetworkId Network = new(4);

    private static readonly ServiceId Service = new(101);

    [Fact]
    public async Task AServiceWithASelectedChannelAndATunerForItResolvesToWhereItTunes()
    {
        Fixture fixture = Ready();

        TuningResolution resolved = await fixture.Directory.ResolveTuningAsync(Network, Service, Cancel);

        Assert.True(resolved.CanTune);
        Assert.Equal(TuningParameters.Terrestrial(27), resolved.Tuning);
        Assert.Equal(fixture.Selected.Id, resolved.CandidateChannelId);
    }

    [Fact]
    public async Task AServiceNobodyHasHeardOfIsRefusedAsUnknown()
    {
        Fixture fixture = Ready();

        TuningResolution resolved = await fixture.Directory.ResolveTuningAsync(Network, new ServiceId(999), Cancel);

        Assert.Equal(TuningRefusal.NoSuchService, resolved.Refusal);
    }

    [Fact]
    public async Task AServiceWithNowhereToTuneIsRefusedForThatAndNotForWantOfATuner()
    {
        Fixture fixture = Ready(selected: false);

        TuningResolution resolved = await fixture.Directory.ResolveTuningAsync(Network, Service, Cancel);

        Assert.Equal(TuningRefusal.NoSelectedChannel, resolved.Refusal);
    }

    [Fact]
    public async Task AServiceNoConfiguredTunerCanReceiveIsRefusedForWantOfATuner()
    {
        Fixture fixture = Ready(kind: TunerKind.Satellite);

        TuningResolution resolved = await fixture.Directory.ResolveTuningAsync(Network, Service, Cancel);

        Assert.Equal(TuningRefusal.NoTunerForSystem, resolved.Refusal);
    }

    [Fact]
    public async Task ALedgerThatCannotBeReadIsUnknownRatherThanAMachineWithoutTuners()
    {
        Fixture fixture = Ready(capacityKnown: false);

        TuningResolution resolved = await fixture.Directory.ResolveTuningAsync(Network, Service, Cancel);

        Assert.Equal(TuningRefusal.CapacityUnknown, resolved.Refusal);
        Assert.False(resolved.CanTune);
    }

    [Fact]
    public async Task ABsServiceIsTakenByTheSameSatelliteSeatThatWouldTakeACs110One()
    {
        Fixture fixture = Ready(
            tuning: TuningParameters.Bs(15, new TransportStreamId(16_400)),
            kind: TunerKind.Satellite);

        Assert.True((await fixture.Directory.ResolveTuningAsync(Network, Service, Cancel)).CanTune);
    }

    [Fact]
    public async Task ACs110ServiceIsTakenByTheSameSatelliteSeatThatWouldTakeABsOne()
    {
        Fixture fixture = Ready(tuning: TuningParameters.Cs110(24), kind: TunerKind.Satellite);

        Assert.True((await fixture.Directory.ResolveTuningAsync(Network, Service, Cancel)).CanTune);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhetherAServiceCanBeTunedIsTheSameAnswerAsResolvingIt(bool reachable)
    {
        Fixture fixture = Ready(kind: reachable ? TunerKind.Terrestrial : TunerKind.Satellite);

        TuningResolution resolved = await fixture.Directory.ResolveTuningAsync(Network, Service, Cancel);
        bool canTune = await fixture.Directory.CanTuneAsync(Network, Service, Cancel);

        Assert.Equal(resolved.CanTune, canTune);
        Assert.Equal(reachable, canTune);
    }

    [Fact]
    public async Task TheSelectedChannelIsReadAgainRatherThanRememberedFromTheFirstCall()
    {
        Fixture fixture = Ready();

        Assert.Equal(
            TuningParameters.Terrestrial(27),
            (await fixture.Directory.ResolveTuningAsync(Network, Service, Cancel)).Tuning);

        fixture.Candidates.Candidates.Clear();
        fixture.Candidates.Candidates.Add(Candidate(TuningParameters.Terrestrial(31), selected: true));

        Assert.Equal(
            TuningParameters.Terrestrial(31),
            (await fixture.Directory.ResolveTuningAsync(Network, Service, Cancel)).Tuning);
    }

    private static CandidateChannel Candidate(TuningParameters tuning, bool selected)
        => CandidateChannel.Rehydrate(
            CandidateChannelId.New(),
            Network,
            Service,
            tuning,
            observedStreamId: null,
            isSelected: selected,
            selectionSource: selected ? SelectionSource.Manual : null,
            selectedAt: selected ? At : null,
            selectionMeasurement: null,
            lastMeasurement: null,
            needsRevalidation: false,
            rotationState: RotationState.Active,
            consecutiveFailures: 0,
            nextAttemptAt: null,
            needsAttentionSince: null,
            discoveredAt: At,
            lastSeenAt: At);

    private static Fixture Ready(
        bool selected = true,
        TuningParameters? tuning = null,
        TunerKind kind = TunerKind.Terrestrial,
        bool capacityKnown = true)
    {
        var services = new HeldServices();
        var candidates = new HeldCandidates();

        services.Services.Add(BroadcastService.Discover(
            Network,
            Service,
            "Reachable",
            ServiceCategory.Television,
            At));

        CandidateChannel candidate = Candidate(tuning ?? TuningParameters.Terrestrial(27), selected);
        candidates.Candidates.Add(candidate);

        TunerCapacity? capacity = capacityKnown
            ? new TunerCapacity([new TunerSeat("adapter0", BroadcastReception.Of(kind), Faulted: false)], [])
            : null;

        return new Fixture(
            new ServiceTuningDirectory(services, candidates, new HeldCapacity(capacity)),
            candidates,
            candidate);
    }

    private sealed record Fixture(
        ServiceTuningDirectory Directory,
        HeldCandidates Candidates,
        CandidateChannel Selected);

    private sealed class HeldCapacity(TunerCapacity? capacity) : ITunerCapacityDirectory
    {
        public Task<TunerCapacity?> ReadAsync(CancellationToken cancellationToken)
            => Task.FromResult(capacity);
    }
}
