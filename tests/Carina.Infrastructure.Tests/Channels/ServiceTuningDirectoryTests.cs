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

    private static readonly ServiceId Unknown = new(999);

    [Fact]
    public async Task AServiceWithASelectedChannelAndATunerForItResolvesToWhereItTunes()
    {
        Fixture fixture = Ready();

        TuningResolution resolved = await fixture.Directory.ResolveTuningAsync(Network, Service, Cancel);

        Assert.True(resolved.CanTune);
        Assert.Equal(TuningParameters.Terrestrial(27), resolved.Tuning);
        Assert.Equal(fixture.Selected.Id, resolved.CandidateChannelId);
        Assert.False(resolved.Impaired);
    }

    [Theory]
    [InlineData(TuningRefusal.None)]
    [InlineData(TuningRefusal.NoSuchService)]
    [InlineData(TuningRefusal.NoSelectedChannel)]
    [InlineData(TuningRefusal.NoTunerForSystem)]
    [InlineData(TuningRefusal.CapacityUnknown)]
    [InlineData(TuningRefusal.LedgerUnreadable)]
    public async Task WhetherAServiceCanBeTunedIsTheSameAnswerAsResolvingItForEveryReason(
        TuningRefusal expected)
    {
        (Fixture fixture, ServiceId asked) = Arranged(expected);

        TuningResolution resolved = await fixture.Directory.ResolveTuningAsync(Network, asked, Cancel);
        bool canTune = await fixture.Directory.CanTuneAsync(Network, asked, Cancel);

        Assert.Equal(expected, resolved.Refusal);
        Assert.Equal(resolved.CanTune, canTune);
        Assert.Equal(expected is TuningRefusal.None, canTune);
    }

    [Fact]
    public async Task ALedgerTunerTheDriverHasNotLoadedLeavesTheAnswerUnknownRatherThanRefusedForever()
    {
        Fixture fixture = Ready(kind: TunerKind.Satellite, undetermined: ["adapter9"]);

        TuningResolution resolved = await fixture.Directory.ResolveTuningAsync(Network, Service, Cancel);

        Assert.Equal(TuningRefusal.CapacityUnknown, resolved.Refusal);
    }

    [Fact]
    public async Task ALedgerThatCouldNotBeReadAtAllIsADifferentAnswerFromASeatItCouldNotPlace()
    {
        Fixture unreadable = Ready(capacityKnown: false);
        Fixture unplaceable = Ready(kind: TunerKind.Satellite, undetermined: ["adapter9"]);

        Assert.Equal(
            TuningRefusal.LedgerUnreadable,
            (await unreadable.Directory.ResolveTuningAsync(Network, Service, Cancel)).Refusal);
        Assert.Equal(
            TuningRefusal.CapacityUnknown,
            (await unplaceable.Directory.ResolveTuningAsync(Network, Service, Cancel)).Refusal);
    }

    [Fact]
    public async Task AMachineThatDescribesEveryTunerRefusesForWantOfOneRatherThanForNotKnowing()
    {
        Fixture fixture = Ready(kind: TunerKind.Satellite);

        TuningResolution resolved = await fixture.Directory.ResolveTuningAsync(Network, Service, Cancel);

        Assert.Equal(TuningRefusal.NoTunerForSystem, resolved.Refusal);
    }

    [Fact]
    public async Task AServiceWhoseOnlyTunerIsFaultedStillTunesButIsCalledImpaired()
    {
        Fixture fixture = Ready(faulted: true);

        TuningResolution resolved = await fixture.Directory.ResolveTuningAsync(Network, Service, Cancel);

        Assert.True(resolved.CanTune);
        Assert.True(resolved.Impaired);
        Assert.True(await fixture.Directory.CanTuneAsync(Network, Service, Cancel));
    }

    [Fact]
    public async Task AServiceWithOneFaultedAndOneWorkingTunerIsNotImpaired()
    {
        Fixture fixture = Ready(seats:
        [
            new TunerSeat("adapter0", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: true),
            new TunerSeat("adapter1", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
        ]);

        Assert.False((await fixture.Directory.ResolveTuningAsync(Network, Service, Cancel)).Impaired);
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

    private static (Fixture Fixture, ServiceId Asked) Arranged(TuningRefusal refusal) => refusal switch
    {
        TuningRefusal.None => (Ready(), Service),
        TuningRefusal.NoSuchService => (Ready(), Unknown),
        TuningRefusal.NoSelectedChannel => (Ready(selected: false), Service),
        TuningRefusal.NoTunerForSystem => (Ready(kind: TunerKind.Satellite), Service),
        TuningRefusal.CapacityUnknown => (Ready(kind: TunerKind.Satellite, undetermined: ["adapter9"]), Service),
        _ => (Ready(capacityKnown: false), Service),
    };

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
        bool capacityKnown = true,
        bool faulted = false,
        IReadOnlyList<string>? undetermined = null,
        IReadOnlyList<TunerSeat>? seats = null)
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
            ? new TunerCapacity(
                seats ?? [new TunerSeat("adapter0", BroadcastReception.Of(kind), faulted)],
                undetermined ?? [])
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
