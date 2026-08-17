using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.Infrastructure.Scanning;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Scanning;

public sealed class ScanApplierTests
{
    private const int Terrestrial = 53;
    private const int OtherTerrestrial = 55;
    private const int SatelliteSlot = 5;
    private const int SatelliteStream = 50001;

    private static readonly DateTime At = StillClock.Now.UtcDateTime;

    private readonly HeldServices services = new();
    private readonly HeldCandidates candidates = new();
    private readonly RecordingAppEvents events = new();

    private ScanApplier Applier
        => new(services, candidates, new UnguardedWrites(), events, new StillClock());

    private static ScanServiceChange Change(
        ScanChangeKind kind,
        int serviceId,
        string name,
        params ScanChannelChange[] channels)
        => Change(kind, serviceId, name, seen: true, channels);

    private static ScanServiceChange Change(
        ScanChangeKind kind,
        int serviceId,
        string name,
        bool seen,
        params ScanChannelChange[] channels)
        => new(
            kind,
            new NetworkId(1),
            new ServiceId(serviceId),
            name,
            ServiceCategory.Television,
            channels,
            seen);

    private static ScanChannelChange Channel(ScanChangeKind kind, TuningParameters tuning, int? cnr = 21_500)
        => new(
            kind,
            tuning,
            tuning.TransportStreamId,
            cnr is null ? null : SignalMeasurement.WithLock(At, cnr));

    private static TuningParameters Satellite()
        => TuningParameters.Bs(SatelliteSlot, new TransportStreamId(SatelliteStream));

    private void Seed(int serviceId, string name, params TuningParameters[] tunings)
    {
        services.Services.Add(BroadcastService.Discover(
            new NetworkId(1),
            new ServiceId(serviceId),
            name,
            ServiceCategory.Television,
            At));

        foreach (var tuning in tunings)
        {
            candidates.Candidates.Add(CandidateChannel.Discover(
                CandidateChannelId.New(),
                new NetworkId(1),
                new ServiceId(serviceId),
                tuning,
                At));
        }
    }

    [Fact]
    public async Task ATerrestrialOnlyApplicationLeavesSatelliteServicesWhereTheyWere()
    {
        Seed(101, "Terrestrial one", TuningParameters.Terrestrial(Terrestrial));
        Seed(201, "Satellite one", Satellite());

        var applied = await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Added,
                        102,
                        "Terrestrial two",
                        Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(OtherTerrestrial))),
                    Change(
                        ScanChangeKind.Missing,
                        201,
                        "Satellite one",
                        Channel(ScanChangeKind.Missing, Satellite(), cnr: null)),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Equal(1, applied.ServicesAdded);
        Assert.Equal(0, applied.ServicesRemoved);
        Assert.Equal(0, applied.ChannelsRemoved);
        Assert.Contains(services.Services, service => service.ServiceId.Value == 201);
        Assert.Contains(candidates.Candidates, candidate => candidate.Tuning.Equals(Satellite()));
    }

    [Fact]
    public async Task AServiceKeepsChannelsThatWentAwayBetweenTheProposalAndTheApply()
    {
        Seed(101, "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));

        await candidates.RemoveAsync(candidates.Candidates[0].Id, CancellationToken.None);

        candidates.Candidates.Add(CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(1),
            new ServiceId(101),
            Satellite(),
            At));

        var applied = await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Missing,
                        101,
                        "Two ways in",
                        Channel(ScanChangeKind.Missing, TuningParameters.Terrestrial(Terrestrial), cnr: null),
                        Channel(ScanChangeKind.Missing, TuningParameters.Terrestrial(OtherTerrestrial), cnr: null)),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Equal(0, applied.ServicesRemoved);
        Assert.Single(services.Services);
        Assert.Single(candidates.Candidates, candidate => candidate.Tuning.Equals(Satellite()));
    }

    [Fact]
    public async Task AChannelTheProposalNeverNamedSurvivesARemovalOfItsSiblings()
    {
        Seed(101, "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));

        var applied = await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Updated,
                        101,
                        "Two ways in",
                        Channel(ScanChangeKind.Missing, TuningParameters.Terrestrial(Terrestrial), cnr: null)),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Equal(1, applied.ChannelsRemoved);
        Assert.Equal(0, applied.ServicesRemoved);
        Assert.Single(candidates.Candidates);
    }

    [Fact]
    public async Task AMissingChannelOfASystemThatWasNotWalkedIsNotRemoved()
    {
        Seed(201, "Satellite one", Satellite());

        var applied = await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Missing,
                        201,
                        "Satellite one",
                        Channel(ScanChangeKind.Missing, Satellite(), cnr: null)),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Equal(0, applied.ChannelsRemoved);
        Assert.Equal(0, applied.ServicesRemoved);
        Assert.Single(candidates.Candidates);
    }

    [Fact]
    public async Task ANewServiceArrivesWithItsChannelAndASelectionAttributedToTheScan()
    {
        var applied = await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Added,
                        101,
                        "Arrived",
                        Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(Terrestrial))),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Equal(1, applied.ServicesAdded);
        Assert.Equal(1, applied.ChannelsAdded);
        Assert.Single(candidates.Candidates);
        Assert.True(candidates.Candidates[0].IsSelected);
        Assert.Equal(SelectionSource.Scan, candidates.Candidates[0].SelectionSource);
    }

    [Fact]
    public async Task ANewServiceSeenOnSeveralChannelsSelectsTheOneThatMeasuredBest()
    {
        await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Added,
                        101,
                        "Arrived",
                        Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(Terrestrial), 18_000),
                        Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(OtherTerrestrial), 27_000)),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        var selected = Assert.Single(candidates.Candidates, candidate => candidate.IsSelected);

        Assert.Equal(OtherTerrestrial, selected.Tuning.PhysicalChannel);
    }

    [Fact]
    public async Task AnUpdatedServiceKeepsItsIdentityWhileItsNameAndChannelsMove()
    {
        Seed(101, "Old name", TuningParameters.Terrestrial(Terrestrial));

        var applied = await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Updated,
                        101,
                        "New name",
                        Channel(ScanChangeKind.Missing, TuningParameters.Terrestrial(Terrestrial), cnr: null),
                        Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(OtherTerrestrial))),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Equal(1, applied.ServicesUpdated);
        Assert.Equal("New name", services.Services[0].Name);
        Assert.Equal(
            [OtherTerrestrial],
            candidates.Candidates.Select(candidate => candidate.Tuning.PhysicalChannel));
    }

    [Fact]
    public async Task AServiceWhoseEverySeenChannelIsGoneLeavesWithThem()
    {
        Seed(101, "Departed", TuningParameters.Terrestrial(Terrestrial));

        var applied = await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Missing,
                        101,
                        "Departed",
                        Channel(ScanChangeKind.Missing, TuningParameters.Terrestrial(Terrestrial), cnr: null)),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Equal(1, applied.ServicesRemoved);
        Assert.Equal(1, applied.ChannelsRemoved);
        Assert.Empty(services.Services);
        Assert.Empty(candidates.Candidates);
    }

    [Fact]
    public async Task AServiceTheScanDidNotReceiveIsNotStampedAsSeenJustNow()
    {
        // Seeded before the clock the applier reads, so a stamp would be visible as a move.
        var discovered = At.AddHours(-1);
        services.Services.Add(BroadcastService.Discover(
            new NetworkId(1), new ServiceId(101), "Went quiet", ServiceCategory.Television, discovered));
        candidates.Candidates.Add(CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(1),
            new ServiceId(101),
            TuningParameters.Terrestrial(Terrestrial),
            discovered));
        candidates.Candidates.Add(CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(1),
            new ServiceId(101),
            TuningParameters.Terrestrial(OtherTerrestrial),
            discovered));

        var applied = await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Updated,
                        101,
                        "Went quiet",
                        seen: false,
                        Channel(ScanChangeKind.Missing, TuningParameters.Terrestrial(Terrestrial), cnr: null)),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        // The one channel it was reached on is gone, so the disappearance is what was applied.
        // Moving the clock here would have the service last seen by the scan that lost it.
        Assert.Equal(discovered, services.Services[0].LastSeenAt);
        Assert.Equal(0, applied.ServicesUpdated);
        Assert.Equal(1, applied.ChannelsRemoved);
        Assert.Equal(
            [OtherTerrestrial],
            candidates.Candidates.Select(candidate => candidate.Tuning.PhysicalChannel));
    }

    [Fact]
    public async Task AServiceTheScanDidNotReceiveIsNotEnteredWhenNothingHoldsItEither()
    {
        var applied = await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Missing,
                        101,
                        "Never here",
                        seen: false,
                        Channel(ScanChangeKind.Missing, TuningParameters.Terrestrial(Terrestrial), cnr: null)),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        // Entering it would date both its discovery and its last sighting to the scan that
        // established it was not received.
        Assert.Empty(services.Services);
        Assert.Equal(0, applied.ServicesAdded);
        Assert.Equal(0, applied.ServicesUpdated);
    }

    [Fact]
    public async Task RemovingTheSelectedChannelLeavesTheServiceWithNowhereToTuneRatherThanARepointing()
    {
        Seed(101, "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));

        await candidates.SelectAsync(
            candidates.Candidates[0].Id,
            SelectionSource.Manual,
            null,
            At,
            CancellationToken.None);

        await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Updated,
                        101,
                        "Two ways in",
                        Channel(ScanChangeKind.Missing, TuningParameters.Terrestrial(Terrestrial), cnr: null)),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Single(candidates.Candidates);
        Assert.DoesNotContain(candidates.Candidates, candidate => candidate.IsSelected);
    }

    [Fact]
    public async Task AChannelAlreadyKnownIsNotProposedTwiceIntoTheSameService()
    {
        Seed(101, "Known", TuningParameters.Terrestrial(Terrestrial));

        var applied = await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Updated,
                        101,
                        "Known",
                        Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(Terrestrial))),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Equal(0, applied.ChannelsAdded);
        Assert.Single(candidates.Candidates);
    }

    [Fact]
    public async Task ADifferenceThatChangesNothingWritesNothingAndStillSaysSo()
    {
        Seed(101, "Untouched", TuningParameters.Terrestrial(Terrestrial));

        var applied = await Applier.ApplyAsync(
            ScanDifference.Nothing,
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Equal(0, applied.ServicesAdded);
        Assert.Equal(0, applied.ServicesUpdated);
        Assert.Equal(0, applied.ServicesRemoved);
        Assert.Single(services.Services);
    }

    [Fact]
    public async Task ApplyingTellsTheScreenSomethingChanged()
    {
        await Applier.ApplyAsync(
            ScanDifference.Nothing,
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Equal([AppEvents.Tuners], events.Signalled);
    }
}
