using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.TestSupport;

using Puppis.Contracts;

namespace Carina.Domain.Tests.Unit.Channels;

[Trait("Category", "Unit")]
public sealed class CandidateChannelDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

    private static readonly NetworkId Network = new(4);

    private static readonly ServiceId Service = new(101);

    private readonly HeldCandidates candidates = new();

    private readonly LedgerOnlyDriverClient driver = new();

    private CandidateChannelDomainService Domain => new(candidates, driver, new FixedTimeProvider(new DateTimeOffset(At)));

    [Fact(DisplayName = "BR-TD-010: a channel a tuner in service receives is added by hand, waiting to be checked")]
    public async Task AChannelATunerReceivesIsAdded()
    {
        driver.Tuners = Tuners(new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle));

        CandidateChannelAdmission admission = await Domain.AdmitAsync(Network, Service, TuningParameters.Terrestrial(27), Cancel);

        Assert.Null(admission.Refusal);
        CandidateChannel added = Assert.IsType<CandidateChannel>(admission.Added);
        Assert.True(added.BelongsTo(Network, Service));
        Assert.Equal(TuningParameters.Terrestrial(27), added.Tuning);
        Assert.True(added.NeedsRevalidation);
        Assert.Equal(At, added.DiscoveredAt);
        Assert.Empty(candidates.Candidates);
    }

    [Fact(DisplayName = "a service does not carry the same channel twice")]
    public async Task TheSameChannelIsNotAddedTwice()
    {
        candidates.Candidates.Add(CandidateChannel.Discover(CandidateChannelId.New(), Network, Service, TuningParameters.Terrestrial(27), At));
        driver.Tuners = Tuners(new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle));

        CandidateChannelAdmission admission = await Domain.AdmitAsync(Network, Service, TuningParameters.Terrestrial(27), Cancel);

        Assert.Equal(CandidateChannelRefusal.AlreadyHeld, admission.Refusal);
        Assert.Null(admission.Added);
    }

    [Fact(DisplayName = "a channel is not added while the driver cannot say which tuners it holds")]
    public async Task NothingIsAddedWhileTheDriverIsOutOfReach()
    {
        driver.Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Unreachable("no socket");

        CandidateChannelAdmission admission = await Domain.AdmitAsync(Network, Service, TuningParameters.Terrestrial(27), Cancel);

        Assert.Equal(CandidateChannelRefusal.DriverUnreachable, admission.Refusal);
        Assert.Null(admission.Added);
    }

    [Fact(DisplayName = "BR-TD-010: a channel no tuner in service receives is not added, naming the kind needed and the kinds in service")]
    public async Task AChannelNoTunerInServiceReceivesIsNotAdded()
    {
        driver.Tuners = Tuners(
            new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle),
            new TunerSnapshot("adapter1", TunerKind.Terrestrial, TunerState.Busy),
            new TunerSnapshot("adapter2", TunerKind.Satellite, TunerState.Disabled),
            new TunerSnapshot("adapter3", TunerKind.Satellite, TunerState.Faulted));

        CandidateChannelAdmission admission = await Domain.AdmitAsync(Network, Service, TuningParameters.Cs110(2), Cancel);

        Assert.Equal(CandidateChannelRefusal.NoTunerReceivesIt, admission.Refusal);
        Assert.Equal(TunerKind.Satellite, admission.Needed);
        Assert.Equal([TunerKind.Terrestrial], admission.InService);
        Assert.Null(admission.Added);
    }

    private static DriverCall<IReadOnlyList<TunerSnapshot>> Tuners(params TunerSnapshot[] tuners)
        => DriverCall<IReadOnlyList<TunerSnapshot>>.Reached(tuners);
}
