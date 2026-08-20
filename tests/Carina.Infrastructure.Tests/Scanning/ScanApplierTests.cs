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
    private const int ThirdTerrestrial = 57;
    private const int SatelliteSlot = 5;
    private const int SatelliteStream = 50001;
    private const int OtherSatelliteSlot = 9;
    private const int OtherSatelliteStream = 50002;

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

    private static ScanChannelChange Channel(
        ScanChangeKind kind,
        TuningParameters tuning,
        SignalMeasurement measurement)
        => new(kind, tuning, tuning.TransportStreamId, measurement);

    private static TuningParameters Satellite()
        => TuningParameters.Bs(SatelliteSlot, new TransportStreamId(SatelliteStream));

    private static TuningParameters OtherSatellite()
        => TuningParameters.Bs(OtherSatelliteSlot, new TransportStreamId(OtherSatelliteStream));

    [Fact]
    public async Task AScanPickMadeWithNothingToCompareMovesToTheChannelThatMeasuredBest()
    {
        Seed(101, "Two ways in", TuningParameters.Terrestrial(Terrestrial));
        candidates.Candidates[0].RecordTuningSuccess(SignalMeasurement.WithLock(At, 18_000), At);

        await candidates.SelectAsync(
            candidates.Candidates[0].Id,
            SelectionSource.Scan,
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
                        Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(OtherTerrestrial), 31_000)),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        CandidateChannel selected = Assert.Single(candidates.Candidates, candidate => candidate.IsSelected);

        Assert.Equal(OtherTerrestrial, selected.Tuning.PhysicalChannel);
    }

    [Fact]
    public async Task TheChannelTheScanSettlesOnCarriesTheMeasurementItWasChosenOn()
    {
        Seed(101, "Two ways in", TuningParameters.Terrestrial(Terrestrial));
        candidates.Candidates[0].RecordTuningSuccess(SignalMeasurement.WithLock(At, 18_000), At);

        await candidates.SelectAsync(
            candidates.Candidates[0].Id,
            SelectionSource.Scan,
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
                        Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(OtherTerrestrial), 31_000)),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        CandidateChannel selected = Assert.Single(candidates.Candidates, candidate => candidate.IsSelected);

        Assert.Equal(31_000, selected.SelectionMeasurement?.CnrMilliDecibels);
    }

    [Fact]
    public async Task AHandPickedChannelStaysWhereItWasPutWhenAScanFindsABetterOne()
    {
        Seed(101, "Two ways in", TuningParameters.Terrestrial(Terrestrial));

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
                        Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(OtherTerrestrial), 31_000)),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        CandidateChannel selected = Assert.Single(candidates.Candidates, candidate => candidate.IsSelected);

        Assert.Equal(Terrestrial, selected.Tuning.PhysicalChannel);
        Assert.Equal(SelectionSource.Manual, selected.SelectionSource);
    }

    [Fact]
    public async Task AChannelAlreadyChosenAgainstAMeasurementIsNotMovedByALaterScan()
    {
        Seed(101, "Two ways in", TuningParameters.Terrestrial(Terrestrial));

        await candidates.SelectAsync(
            candidates.Candidates[0].Id,
            SelectionSource.Scan,
            SignalMeasurement.WithLock(At, 18_000),
            At,
            CancellationToken.None);

        await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Updated,
                        101,
                        "Two ways in",
                        Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(OtherTerrestrial), 31_000)),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        CandidateChannel selected = Assert.Single(candidates.Candidates, candidate => candidate.IsSelected);

        Assert.Equal(Terrestrial, selected.Tuning.PhysicalChannel);
    }

    [Fact]
    public async Task LosingTheChannelTheScanHadPickedBlindStillLeavesTheServiceWithNowhereToTune()
    {
        Seed(101, "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));

        await candidates.SelectAsync(
            candidates.Candidates[0].Id,
            SelectionSource.Scan,
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
    public async Task AChannelThatNeverLockedIsNotChosenOverOneThatDid()
    {
        await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Added,
                        101,
                        "Arrived",
                        Channel(
                            ScanChangeKind.Added,
                            TuningParameters.Terrestrial(Terrestrial),
                            SignalMeasurement.WithoutLock(At)),
                        Channel(
                            ScanChangeKind.Added,
                            TuningParameters.Terrestrial(OtherTerrestrial),
                            SignalMeasurement.WithLock(At, 9_000))),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        CandidateChannel selected = Assert.Single(candidates.Candidates, candidate => candidate.IsSelected);

        Assert.Equal(OtherTerrestrial, selected.Tuning.PhysicalChannel);
    }

    [Fact]
    public async Task ANewServiceKeepsTheRemoteControlNumberTheStreamDeclared()
    {
        await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Added,
                        101,
                        "Terrestrial one",
                        Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(Terrestrial)))
                        with
                    { RemoteControlKeyId = 1 },
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Equal(1, Assert.Single(services.Services).RemoteControlKeyId);
    }

    [Fact]
    public async Task AScanThatDidNotHearTheNumberDoesNotEraseTheOneWeHold()
    {
        Seed(101, "Terrestrial one", TuningParameters.Terrestrial(Terrestrial));
        services.Services[0].RemoteControlledBy(4);

        await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Updated,
                        101,
                        "Terrestrial one renamed",
                        Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(OtherTerrestrial))),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Equal(4, Assert.Single(services.Services).RemoteControlKeyId);
    }

    [Fact]
    public async Task ARenumberedStreamMovesTheServiceToItsNewNumber()
    {
        Seed(101, "Terrestrial one", TuningParameters.Terrestrial(Terrestrial));
        services.Services[0].RemoteControlledBy(4);

        await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Updated,
                        101,
                        "Terrestrial one renamed",
                        Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(OtherTerrestrial)))
                        with
                    { RemoteControlKeyId = 6 },
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Equal(6, Assert.Single(services.Services).RemoteControlKeyId);
    }

    private void Seed(int serviceId, string name, params TuningParameters[] tunings)
    {
        services.Services.Add(BroadcastService.Discover(
            new NetworkId(1),
            new ServiceId(serviceId),
            name,
            ServiceCategory.Television,
            At));

        foreach (TuningParameters tuning in tunings)
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

        ScanApplication applied = await Applier.ApplyAsync(
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
                        seen: false,
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

        ScanApplication applied = await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Missing,
                        101,
                        "Two ways in",
                        seen: false,
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

        ScanApplication applied = await Applier.ApplyAsync(
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

        ScanApplication applied = await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Missing,
                        201,
                        "Satellite one",
                        seen: false,
                        Channel(ScanChangeKind.Missing, Satellite(), cnr: null)),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Equal(0, applied.ChannelsRemoved);
        Assert.Equal(0, applied.ServicesRemoved);
        Assert.Equal(0, applied.ServicesUpdated);
        Assert.Single(candidates.Candidates);
    }

    [Fact]
    public async Task ANewServiceArrivesWithItsChannelAndASelectionAttributedToTheScan()
    {
        ScanApplication applied = await Applier.ApplyAsync(
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

        CandidateChannel selected = Assert.Single(candidates.Candidates, candidate => candidate.IsSelected);

        Assert.Equal(OtherTerrestrial, selected.Tuning.PhysicalChannel);
    }

    [Fact]
    public async Task AnUpdatedServiceKeepsItsIdentityWhileItsNameAndChannelsMove()
    {
        Seed(101, "Old name", TuningParameters.Terrestrial(Terrestrial));

        ScanApplication applied = await Applier.ApplyAsync(
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

        ScanApplication applied = await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Missing,
                        101,
                        "Departed",
                        seen: false,
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
        DateTime discovered = At.AddHours(-1);
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

        ScanApplication applied = await Applier.ApplyAsync(
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

        Assert.Equal(discovered, services.Services[0].LastSeenAt);
        Assert.Equal(1, applied.ServicesUpdated);
        Assert.Equal(1, applied.ChannelsRemoved);
        Assert.Equal(
            [OtherTerrestrial],
            candidates.Candidates.Select(candidate => candidate.Tuning.PhysicalChannel));
    }

    [Fact]
    public async Task EveryConfirmedChangeIsAccountedForInWhatTheApplySaysItDid()
    {
        Seed(101, "Kept", TuningParameters.Terrestrial(Terrestrial));
        Seed(102, "Quietened", TuningParameters.Terrestrial(Terrestrial), TuningParameters.Terrestrial(OtherTerrestrial));
        Seed(103, "Left", TuningParameters.Terrestrial(Terrestrial));

        var difference = new ScanDifference(
            [
                Change(
                    ScanChangeKind.Added,
                    104,
                    "Arrived",
                    Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(Terrestrial))),
                Change(
                    ScanChangeKind.Updated,
                    101,
                    "Kept, renamed",
                    Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(OtherTerrestrial))),
                Change(
                    ScanChangeKind.Updated,
                    102,
                    "Quietened",
                    seen: false,
                    Channel(ScanChangeKind.Missing, TuningParameters.Terrestrial(Terrestrial), cnr: null)),
                Change(
                    ScanChangeKind.Missing,
                    103,
                    "Left",
                    seen: false,
                    Channel(ScanChangeKind.Missing, TuningParameters.Terrestrial(Terrestrial), cnr: null)),
            ],
            []);

        ScanApplication applied = await Applier.ApplyAsync(difference, [TuneSystem.IsdbT], CancellationToken.None);

        Assert.Equal(difference.Added.Count, applied.ServicesAdded);
        Assert.Equal(difference.Updated.Count, applied.ServicesUpdated);
        Assert.Equal(difference.Missing.Count, applied.ServicesRemoved);
    }

    [Fact]
    public async Task AServiceThatWasAlreadyGoneIsNotCountedAsOneThisApplyRemoved()
    {
        ScanApplication applied = await Applier.ApplyAsync(
            new ScanDifference(
                [
                    Change(
                        ScanChangeKind.Missing,
                        101,
                        "Already gone",
                        seen: false,
                        Channel(ScanChangeKind.Missing, TuningParameters.Terrestrial(Terrestrial), cnr: null)),
                ],
                []),
            [TuneSystem.IsdbT],
            CancellationToken.None);

        Assert.Equal(0, applied.ServicesRemoved);
    }

    [Fact]
    public async Task AServiceTheScanDidNotReceiveIsNotEnteredWhenNothingHoldsItEither()
    {
        ScanApplication applied = await Applier.ApplyAsync(
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

        ScanApplication applied = await Applier.ApplyAsync(
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

        ScanApplication applied = await Applier.ApplyAsync(
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

    [Fact]
    public async Task AServiceWhoseChannelsAllStayedTheSameIsStillMovedOffTheOneChosenBlind()
    {
        SeedMeasured(101, "Two ways in", (Terrestrial, 16_000), (OtherTerrestrial, 35_000));

        await candidates.SelectAsync(
            candidates.Candidates[0].Id,
            SelectionSource.Scan,
            null,
            At,
            CancellationToken.None);

        await Applier.ApplyAsync(ScanDifference.Nothing, [TuneSystem.IsdbT], CancellationToken.None);

        CandidateChannel selected = Assert.Single(candidates.Candidates, candidate => candidate.IsSelected);

        Assert.Equal(OtherTerrestrial, selected.Tuning.PhysicalChannel);
        Assert.Equal(35_000, selected.SelectionMeasurement?.CnrMilliDecibels);
        Assert.Equal(SelectionSource.Scan, selected.SelectionSource);
    }

    [Fact]
    public async Task ReconsideringAServiceTheDifferenceNeverNamedLeavesItOneChannelAndNoMore()
    {
        SeedMeasured(
            101,
            "Three ways in",
            (Terrestrial, 16_000),
            (OtherTerrestrial, 35_000),
            (ThirdTerrestrial, 29_000));

        await candidates.SelectAsync(
            candidates.Candidates[0].Id,
            SelectionSource.Scan,
            null,
            At,
            CancellationToken.None);

        await Applier.ApplyAsync(ScanDifference.Nothing, [TuneSystem.IsdbT], CancellationToken.None);

        CandidateChannel selected = Assert.Single(candidates.Candidates, candidate => candidate.IsSelected);

        Assert.Equal(OtherTerrestrial, selected.Tuning.PhysicalChannel);
    }

    [Fact]
    public async Task AHandPickedChannelIsLeftAloneByAScanThatNeverNamedItsService()
    {
        SeedMeasured(101, "Two ways in", (Terrestrial, 16_000), (OtherTerrestrial, 35_000));

        await candidates.SelectAsync(
            candidates.Candidates[0].Id,
            SelectionSource.Manual,
            null,
            At,
            CancellationToken.None);

        await Applier.ApplyAsync(ScanDifference.Nothing, [TuneSystem.IsdbT], CancellationToken.None);

        CandidateChannel selected = Assert.Single(candidates.Candidates, candidate => candidate.IsSelected);

        Assert.Equal(Terrestrial, selected.Tuning.PhysicalChannel);
        Assert.Equal(SelectionSource.Manual, selected.SelectionSource);
    }

    [Fact]
    public async Task AServiceWithNowhereToTuneIsNotGivenSomewhereByAScanThatNeverNamedIt()
    {
        SeedMeasured(101, "Two ways in", (Terrestrial, 16_000), (OtherTerrestrial, 35_000));

        await Applier.ApplyAsync(ScanDifference.Nothing, [TuneSystem.IsdbT], CancellationToken.None);

        Assert.DoesNotContain(candidates.Candidates, candidate => candidate.IsSelected);
    }

    [Fact]
    public async Task AChannelChosenAgainstAMeasurementIsNotMovedByAScanThatNeverNamedItsService()
    {
        SeedMeasured(101, "Two ways in", (Terrestrial, 16_000), (OtherTerrestrial, 35_000));

        await candidates.SelectAsync(
            candidates.Candidates[0].Id,
            SelectionSource.Scan,
            SignalMeasurement.WithLock(At, 16_000),
            At,
            CancellationToken.None);

        await Applier.ApplyAsync(ScanDifference.Nothing, [TuneSystem.IsdbT], CancellationToken.None);

        CandidateChannel selected = Assert.Single(candidates.Candidates, candidate => candidate.IsSelected);

        Assert.Equal(Terrestrial, selected.Tuning.PhysicalChannel);
    }

    [Fact]
    public async Task AServiceOfASystemTheScanNeverWalkedKeepsTheChannelChosenBlind()
    {
        Seed(201, "Satellite one", Satellite(), OtherSatellite());
        candidates.Candidates[0].RecordTuningSuccess(SignalMeasurement.WithLock(At, 16_000), At);
        candidates.Candidates[1].RecordTuningSuccess(SignalMeasurement.WithLock(At, 35_000), At);

        await candidates.SelectAsync(
            candidates.Candidates[0].Id,
            SelectionSource.Scan,
            null,
            At,
            CancellationToken.None);

        await Applier.ApplyAsync(ScanDifference.Nothing, [TuneSystem.IsdbT], CancellationToken.None);

        CandidateChannel selected = Assert.Single(candidates.Candidates, candidate => candidate.IsSelected);

        Assert.Equal(SatelliteSlot, selected.Tuning.PhysicalChannel);
        Assert.Null(selected.SelectionMeasurement);
    }

    [Fact]
    public async Task ServicesOnOneStreamStayOnOneChannelWhenOnlyOneOfTheirCandidatesLosesItsReading()
    {
        SeedMeasured(101, "First on the stream", (Terrestrial, 36_000), (OtherTerrestrial, 30_000));
        SeedMeasured(102, "Second on the stream", (Terrestrial, 36_000), (OtherTerrestrial, 30_000));

        candidates.Candidates
            .Single(candidate => candidate.ServiceId.Value == 101
                                 && candidate.Tuning.PhysicalChannel == Terrestrial)
            .RecordTuningSuccess(SignalMeasurement.WithLock(At.AddHours(1)), At.AddHours(1));

        int[] onTheStream = [101, 102];

        foreach (int serviceId in onTheStream)
        {
            await candidates.SelectAsync(
                candidates.Candidates
                    .Single(candidate => candidate.ServiceId.Value == serviceId
                                         && candidate.Tuning.PhysicalChannel == OtherTerrestrial)
                    .Id,
                SelectionSource.Scan,
                null,
                At,
                CancellationToken.None);
        }

        await Applier.ApplyAsync(ScanDifference.Nothing, [TuneSystem.IsdbT], CancellationToken.None);

        int[] chosen =
        [
            .. candidates.Candidates
                .Where(candidate => candidate.IsSelected)
                .Select(candidate => candidate.Tuning.PhysicalChannel),
        ];

        Assert.Equal([Terrestrial, Terrestrial], chosen);
    }

    private void SeedMeasured(int serviceId, string name, params (int PhysicalChannel, int Cnr)[] measured)
    {
        Seed(serviceId, name, [.. measured.Select(channel => TuningParameters.Terrestrial(channel.PhysicalChannel))]);

        foreach ((int physicalChannel, int cnr) in measured)
        {
            candidates.Candidates
                .Single(candidate => candidate.ServiceId.Value == serviceId
                                     && candidate.Tuning.PhysicalChannel == physicalChannel)
                .RecordTuningSuccess(SignalMeasurement.WithLock(At, cnr), At);
        }
    }
}
