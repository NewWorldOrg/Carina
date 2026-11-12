using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.TestSupport;

using Puppis.Contracts;

namespace Carina.Domain.Tests.Unit.Scans;

[Trait("Category", "Unit")]
public sealed class ChannelLineupDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

    private static readonly NetworkId Network = new(4);

    private static readonly TuningParameters Near = TuningParameters.Terrestrial(27);

    private static readonly TuningParameters Far = TuningParameters.Terrestrial(45);

    private static readonly TuningParameters Slot = TuningParameters.Cs110(2);

    private readonly HeldServices services = new();

    private readonly HeldCandidates candidates = new();

    private ChannelLineupDomainService Lineup => new(services, candidates);

    [Fact(DisplayName = "a service seen but not held is proposed as added, on every channel it was seen")]
    public async Task AServiceSeenButNotHeldIsAdded()
    {
        ScannedService seen = Scanned(101, "Fixture Service", 3, Seen(Near), Seen(Far));

        ScanDifference difference = await Lineup.ProposeAsync([seen], [Near, Far], [], Cancel);

        ScanServiceChange added = Assert.Single(difference.Added);
        Assert.Equal(new ServiceId(101), added.ServiceId);
        Assert.Equal([Near, Far], added.Channels.Select(channel => channel.Tuning));
        Assert.Equal(3, added.RemoteControlKeyId);
        Assert.True(added.Seen);
    }

    [Fact(DisplayName = "a held service seen as it is held proposes nothing")]
    public async Task AServiceSeenAsHeldProposesNothing()
    {
        Service(101, "Fixture Service");
        Candidate(101, Near);

        ScanDifference difference = await Lineup.ProposeAsync([Scanned(101, "Fixture Service", null, Seen(Near))], [Near], [], Cancel);

        Assert.True(difference.ChangesNothing);
    }

    [Fact(DisplayName = "BR-TD-003: a held service is changed when a channel is added, carries another stream, or is gone where the scan reached")]
    public async Task AHeldServiceIsChangedByItsChannels()
    {
        Service(101, "Fixture Service");
        Candidate(101, Near).CarriedBy(new TransportStreamId(0x7FE0));
        Candidate(101, Far);

        ScanDifference difference = await Lineup.ProposeAsync(
            [Scanned(101, "Fixture Service", null, Seen(Near, new TransportStreamId(0x7FE1)), Seen(Slot))],
            [Near, Far, Slot],
            [],
            Cancel);

        ScanServiceChange updated = Assert.Single(difference.Updated);
        Assert.Equal(
            [(ScanChangeKind.Added, Slot), (ScanChangeKind.Added, Near), (ScanChangeKind.Missing, Far)],
            updated.Channels.Select(channel => (channel.Kind, channel.Tuning)));
    }

    [Fact(DisplayName = "a held service is changed when its name, kind or remote-control key moved, and a key the scan did not see moves nothing")]
    public async Task AHeldServiceIsChangedByWhatDescribesIt()
    {
        Service(101, "Fixture Service");
        Candidate(101, Near);
        Service(102, "Fixture Service");
        Candidate(102, Far);
        Service(103, "Fixture Service");
        Candidate(103, Slot);

        ScanDifference difference = await Lineup.ProposeAsync(
            [
                Scanned(101, "Renamed", null, Seen(Near)),
                Scanned(102, "Fixture Service", 5, Seen(Far)),
                Scanned(103, "Fixture Service", null, Seen(Slot)),
            ],
            [Near, Far, Slot],
            [],
            Cancel);

        Assert.Equal([new ServiceId(101), new ServiceId(102)], difference.Updated.Select(change => change.ServiceId));
        Assert.Equal("Renamed", difference.Updated[0].Name);
        Assert.Equal(5, difference.Updated[1].RemoteControlKeyId);
    }

    [Fact(DisplayName = "BR-TD-002: a held service seen nowhere is missing when the scan reached all its channels, and changed when only some")]
    public async Task AHeldServiceSeenNowhereLosesWhatTheScanReached()
    {
        Service(101, "Fixture Service");
        Candidate(101, Near);
        Service(102, "Fixture Service");
        Candidate(102, Near);
        Candidate(102, Slot);
        Service(103, "Fixture Service");
        Candidate(103, Slot);

        ScanDifference difference = await Lineup.ProposeAsync([], [Near], [], Cancel);

        ScanServiceChange missing = Assert.Single(difference.Missing);
        ScanServiceChange updated = Assert.Single(difference.Updated);
        Assert.Equal(new ServiceId(101), missing.ServiceId);
        Assert.Equal(new ServiceId(102), updated.ServiceId);
        Assert.False(updated.Seen);
        Assert.Equal([Near], updated.Channels.Select(channel => channel.Tuning));
        Assert.DoesNotContain(difference.Services, change => change.ServiceId.Equals(new ServiceId(103)));
    }

    [Fact(DisplayName = "the departures from the rotation travel with the difference")]
    public async Task TheDeparturesTravelWithTheDifference()
    {
        RotationDeparture departure = new(Network, new ServiceId(101), Near, 6, At);

        ScanDifference difference = await Lineup.ProposeAsync([], [], [departure], Cancel);

        Assert.Equal([departure], difference.Departures);
    }

    [Fact(DisplayName = "BR-TD-002: applying writes only the systems the scan walked")]
    public async Task ApplyingWritesOnlyTheSystemsWalked()
    {
        Service(101, "Fixture Service");
        Candidate(101, Near);
        Candidate(101, Slot);
        ScanServiceChange change = Change(ScanChangeKind.Updated, 101, Gone(Near), Gone(Slot));

        ChannelLineupApplied applied = await Apply([change], TuneSystem.IsdbT);

        Assert.Equal([Slot], candidates.Candidates.Select(candidate => candidate.Tuning));
        Assert.Equal(1, applied.ChannelsRemoved);
        Assert.Equal(1, applied.ServicesUpdated);
    }

    [Fact(DisplayName = "BR-TD-002: a service left with no channel is removed")]
    public async Task AServiceLeftWithNoChannelIsRemoved()
    {
        Service(101, "Fixture Service");
        Candidate(101, Near);

        ChannelLineupApplied applied = await Apply([Change(ScanChangeKind.Missing, 101, Gone(Near))], TuneSystem.IsdbT);

        Assert.Empty(services.Services);
        Assert.Empty(candidates.Candidates);
        Assert.Equal(new ChannelLineupApplied(0, 0, 1, 0, 1), applied);
    }

    [Fact(DisplayName = "a service not held that the scan did not see is not added")]
    public async Task AServiceTheScanDidNotSeeIsNotAdded()
    {
        ChannelLineupApplied applied = await Apply([Change(ScanChangeKind.Updated, 101, seen: false, Gone(Near))], TuneSystem.IsdbT);

        Assert.Empty(services.Services);
        Assert.Equal(new ChannelLineupApplied(0, 0, 0, 0, 0), applied);
    }

    [Fact(DisplayName = "BR-TD-001: a service the scan found is added with the channels it found, and its best one is selected by the scan")]
    public async Task AServiceFoundIsAddedOnItsBestChannel()
    {
        ChannelLineupApplied applied = await Apply(
            [
                Change(
                    ScanChangeKind.Added,
                    101,
                    Seen(Near, measured: SignalMeasurement.WithLock(At, 18_000)),
                    Seen(Far, new TransportStreamId(0x7FE0), SignalMeasurement.WithLock(At, 24_000))),
            ],
            TuneSystem.IsdbT);

        BroadcastService added = Assert.Single(services.Services);
        Assert.Equal(3, added.RemoteControlKeyId);
        CandidateChannel selected = Assert.Single(candidates.Candidates, candidate => candidate.IsSelected);
        Assert.Equal(Far, selected.Tuning);
        Assert.Equal(SelectionSource.Scan, selected.SelectionSource);
        Assert.Equal(new TransportStreamId(0x7FE0), selected.ObservedStreamId);
        Assert.Equal(new ChannelLineupApplied(1, 0, 0, 2, 0), applied);
    }

    [Fact(DisplayName = "BR-TD-003: a channel already held takes the stream the scan saw on it, and stays the same candidate")]
    public async Task AChannelHeldTakesTheStreamSeen()
    {
        Service(101, "Fixture Service");
        CandidateChannel held = Candidate(101, Near);
        held.CarriedBy(new TransportStreamId(0x7FE0));

        await Apply([Change(ScanChangeKind.Updated, 101, Seen(Near, new TransportStreamId(0x7FE1)))], TuneSystem.IsdbT);

        Assert.Same(held, Assert.Single(candidates.Candidates));
        Assert.Equal(new TransportStreamId(0x7FE1), held.ObservedStreamId);
        Assert.Equal(1, candidates.Saves);
    }

    [Fact(DisplayName = "BR-TD-001: a channel selected by hand stays, and one a scan chose blind moves to the best, on services in the difference or not")]
    public async Task OnlyAChannelChosenBlindIsChosenAgain()
    {
        Service(101, "Fixture Service");
        CandidateChannel manual = Candidate(101, Near);
        manual.Select(SelectionSource.Manual, null, At);
        Candidate(101, Far).RecordTuningSuccess(SignalMeasurement.WithLock(At, 24_000), At);
        Service(102, "Fixture Service");
        CandidateChannel blind = Candidate(102, Near);
        blind.Select(SelectionSource.Scan, null, At);
        CandidateChannel better = Candidate(102, Far);
        better.RecordTuningSuccess(SignalMeasurement.WithLock(At, 24_000), At);
        Service(103, "Fixture Service");
        CandidateChannel elsewhere = Candidate(103, Slot);
        elsewhere.Select(SelectionSource.Scan, null, At);
        Candidate(103, TuningParameters.Cs110(4)).RecordTuningSuccess(SignalMeasurement.WithLock(At, 24_000), At);

        await Apply([Change(ScanChangeKind.Updated, 101, Seen(Near))], TuneSystem.IsdbT);

        Assert.True(manual.IsSelected);
        Assert.True(better.IsSelected);
        Assert.False(blind.IsSelected);
        Assert.True(elsewhere.IsSelected);
    }

    private Task<ChannelLineupApplied> Apply(IReadOnlyList<ScanServiceChange> changes, params TuneSystem[] covered)
        => Lineup.ApplyAsync(new ScanDifference(changes, []), covered.ToHashSet(), At, Cancel);

    private void Service(int serviceId, string name)
        => services.Services.Add(BroadcastService.Discover(Network, new ServiceId(serviceId), name, ServiceCategory.Television, At));

    private CandidateChannel Candidate(int serviceId, TuningParameters tuning)
    {
        CandidateChannel candidate = CandidateChannel.Discover(CandidateChannelId.New(), Network, new ServiceId(serviceId), tuning, At);
        candidates.Candidates.Add(candidate);

        return candidate;
    }

    private static ScannedService Scanned(int serviceId, string name, int? remoteControlKeyId, params ScanChannelChange[] channels)
        => new(Network, new ServiceId(serviceId), name, ServiceCategory.Television, remoteControlKeyId, channels);

    private static ScanServiceChange Change(ScanChangeKind kind, int serviceId, params ScanChannelChange[] channels)
        => Change(kind, serviceId, seen: true, channels);

    private static ScanServiceChange Change(ScanChangeKind kind, int serviceId, bool seen, params ScanChannelChange[] channels)
        => new(kind, Network, new ServiceId(serviceId), "Fixture Service", ServiceCategory.Television, channels, seen)
        {
            RemoteControlKeyId = 3,
        };

    private static ScanChannelChange Seen(
        TuningParameters tuning,
        TransportStreamId? stream = null,
        SignalMeasurement? measured = null)
        => new(ScanChangeKind.Added, tuning, stream, measured);

    private static ScanChannelChange Gone(TuningParameters tuning)
        => new(ScanChangeKind.Missing, tuning, null, null);
}
