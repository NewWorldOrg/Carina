using Carina.Broadcast.Descriptors;
using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.Infrastructure.Scanning;
using Carina.Infrastructure.Tests.Fixtures.Clocks;
using Carina.TestSupport;

using Puppis.Contracts;

using HeldClock = Carina.Infrastructure.Tests.Fixtures.Clocks.HeldClock;

namespace Carina.Infrastructure.Tests.Unit.Infra.Scanning;

[Trait("Category", "Unit")]
public sealed class ChannelScanOrchestratorTests
{
    private const int SomeNetworkId = SyntheticStream.SomeNetworkId;

    private const int SomeStreamId = 50002;

    private const int SomeServiceId = 50101;

    private const int AnotherServiceId = 50102;

    private const int AThirdServiceId = 50103;

    private const int AnotherStreamId = 50003;

    private const int SomeBsSlot = 9;

    private const int GroundStreamId = 50_001;

    private const int GroundServiceId = 50_111;

    private const int SkyNetworkId = 4;

    private const int SkyStreamId = 50_002;

    private const int SkyServiceId = 50_101;

    private static readonly TuningParameters Channel53 = TuningParameters.Terrestrial(53);

    private static readonly TuningParameters Channel55 = TuningParameters.Terrestrial(55);

    private static readonly DateTime At = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly ScanSettings BacksOffTwice = ScanSettings.Default with
    {
        Rotation = new RotationBackoff(TimeSpan.FromMinutes(1), 2, TimeSpan.FromHours(1), 3),
    };

    private static readonly TuningParameters Channel57 = TuningParameters.Terrestrial(57);

    private static readonly TuningParameters Slot9 =
        TuningParameters.Bs(9, new TransportStreamId(SomeStreamId));

    private static readonly TuningParameters Slot11 =
        TuningParameters.Bs(11, new TransportStreamId(SomeStreamId));

    private static readonly TunerCapacity GroundOnly =
        new([new TunerSeat("adapter0", [TuneSystem.IsdbT], Faulted: false)], []);

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
            SyntheticStream.Carrying(50004, new SyntheticService(SomeServiceId, "Carina One")).ToBytes());
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
            new SyntheticService(SomeServiceId, "Carina One")).ToBytes()));
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
        => ChannelScript.Carrying(SyntheticStream.Carrying(SomeStreamId, services).ToBytes());

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

    [Fact]
    public async Task AFrontendThatNeverLocksIsRecordedApartFromEveryOtherFailure()
    {
        ScanOutcome outcome = await ScanOne(ChannelScript.NoLock());

        Assert.Equal(ScanAttemptOutcome.NoLock, Single(outcome).Outcome);
    }

    [Fact]
    public async Task ADriverThatRefusesTheTuneIsAFailureToLockRatherThanAnEmptyStream()
    {
        ScanOutcome outcome = await ScanOne(new ChannelScript
        {
            Refusal = new DriverProblem("noDeviceOfThatKind", ["No tuner reaches that system."]),
        });

        ScanRunAttempt attempt = Single(outcome);

        Assert.Equal(ScanAttemptOutcome.NoLock, attempt.Outcome);
        Assert.Contains("noDeviceOfThatKind", attempt.Detail!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SessionRefusalTitles.NoLock, ScanAttemptOutcome.NoLock)]
    [InlineData(SessionRefusalTitles.NoData, ScanAttemptOutcome.LockedWithoutData)]
    [InlineData(null, ScanAttemptOutcome.NoLock)]
    public async Task ASessionHandedBackAlreadyFailedKeepsTheWayReceptionWentWrong(
        string? title,
        ScanAttemptOutcome expected)
    {
        ScanOutcome outcome = await ScanOne(new ChannelScript
        {
            State = SessionState.Failed,
            FailureCause = "the tuner could not receive.",
            FailureTitle = title,
        });

        Assert.Equal(expected, Single(outcome).Outcome);
    }

    [Fact]
    public async Task ALockedTunerWhoseDemuxDeliversNothingIsRecordedAsLockedWithoutData()
    {
        ScanOutcome outcome = await ScanOne(ChannelScript.Silent());

        ScanRunAttempt attempt = Single(outcome);

        Assert.Equal(ScanAttemptOutcome.LockedWithoutData, attempt.Outcome);
        Assert.True(attempt.Measurement!.Locked);
    }

    [Fact]
    public async Task BytesThatNeverCompleteTheTablesAreRecordedAsIncompleteRatherThanAsSilence()
    {
        ScanOutcome outcome = await ScanOne(ChannelScript.Carrying(new SyntheticStream
        {
            NetworkId = SyntheticStream.SomeNetworkId,
            TransportStreamId = SomeStreamId,
            Services = [new SyntheticService(SomeServiceId, "Carina One")],
            WithoutDescription = true,
        }.ToBytes()));

        ScanRunAttempt attempt = Single(outcome);

        Assert.Equal(ScanAttemptOutcome.IncompleteTables, attempt.Outcome);
        Assert.Contains("service description table", attempt.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingNetworkTableIsIncompleteEvenWhenTheServicesAreAllThere()
    {
        ScanOutcome outcome = await ScanOne(ChannelScript.Carrying(new SyntheticStream
        {
            NetworkId = SyntheticStream.SomeNetworkId,
            TransportStreamId = SomeStreamId,
            Services = [new SyntheticService(SomeServiceId, "Carina One")],
            WithoutNetwork = true,
        }.ToBytes()));

        ScanRunAttempt attempt = Single(outcome);

        Assert.Equal(ScanAttemptOutcome.IncompleteTables, attempt.Outcome);
        Assert.Contains("network information table", attempt.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASlotThatSilentlyCarriesAnotherStreamParsesCleanlyAndIsStillAFailure()
    {
        var slot = TuningParameters.Bs(SomeBsSlot, new TransportStreamId(SomeStreamId));
        var harness = new ScanHarness(new ScriptedDriverClient().Script(
            slot,
            ChannelScript.Carrying(SyntheticStream.Carrying(
                AnotherStreamId,
                new SyntheticService(SomeServiceId, "Carina One")).ToBytes())));

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Over([slot]), Cancel);
        ScanRunAttempt attempt = Single(outcome);

        Assert.Equal(ScanAttemptOutcome.UnexpectedStream, attempt.Outcome);
        Assert.Equal(AnotherStreamId, attempt.ObservedTransportStreamId!.Value);
        Assert.Contains($"stream {SomeStreamId}", attempt.Detail!, StringComparison.Ordinal);
        Assert.Contains($"stream {AnotherStreamId}", attempt.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TablesThatParseButDisagreeWithEachOtherAreRecordedAsAnUnexpectedStream()
    {
        ScanOutcome outcome = await ScanOne(ChannelScript.Carrying(new SyntheticStream
        {
            NetworkId = SyntheticStream.SomeNetworkId,
            TransportStreamId = SomeStreamId,
            TransportStreamIdInNetwork = AnotherStreamId,
            Services = [new SyntheticService(SomeServiceId, "Carina One")],
        }.ToBytes()));

        ScanRunAttempt attempt = Single(outcome);

        Assert.Equal(ScanAttemptOutcome.UnexpectedStream, attempt.Outcome);
        Assert.Equal(SomeStreamId, attempt.ObservedTransportStreamId!.Value);
    }

    [Fact]
    public async Task TheFourFailuresAreToldApartInsteadOfCollapsingIntoOneError()
    {
        ScriptedDriverClient driver = new ScriptedDriverClient()
            .Script(TuningParameters.Terrestrial(53), ChannelScript.NoLock())
            .Script(TuningParameters.Terrestrial(55), ChannelScript.Silent())
            .Script(TuningParameters.Terrestrial(57), ChannelScript.Carrying(new SyntheticStream
            {
                NetworkId = SyntheticStream.SomeNetworkId,
                TransportStreamId = SomeStreamId,
                Services = [new SyntheticService(SomeServiceId, "Carina One")],
                WithoutDescription = true,
            }.ToBytes()))
            .Script(TuningParameters.Bs(SomeBsSlot, new TransportStreamId(SomeStreamId)),
                ChannelScript.Carrying(SyntheticStream.Carrying(
                    AnotherStreamId,
                    new SyntheticService(SomeServiceId, "Carina One")).ToBytes()));

        ScanOutcome outcome = await new ScanHarness(driver).Orchestrator.RunAsync(
            ScanScope.Over([
                TuningParameters.Terrestrial(53),
                TuningParameters.Terrestrial(55),
                TuningParameters.Terrestrial(57),
                TuningParameters.Bs(SomeBsSlot, new TransportStreamId(SomeStreamId)),
            ]),
            Cancel);

        Assert.Equal(
            [
                ScanAttemptOutcome.NoLock,
                ScanAttemptOutcome.LockedWithoutData,
                ScanAttemptOutcome.IncompleteTables,
                ScanAttemptOutcome.UnexpectedStream,
            ],
            outcome.Failures.Select(attempt => attempt.Outcome));
        Assert.Equal(4, outcome.Failures.Select(attempt => attempt.Detail).Distinct().Count());
    }

    [Fact]
    public async Task AStreamThatCarriesBothTablesForTheStreamAskedForSucceeds()
    {
        ScanOutcome outcome = await ScanOne(ChannelScript.Carrying(SyntheticStream.Carrying(
            SomeStreamId,
            new SyntheticService(SomeServiceId, "Carina One")).ToBytes()));

        ScanRunAttempt attempt = Single(outcome);

        Assert.Equal(ScanAttemptOutcome.Succeeded, attempt.Outcome);
        Assert.Empty(outcome.Failures);
        Assert.Equal(SomeStreamId, attempt.ObservedTransportStreamId!.Value);
    }

    [Fact]
    public async Task AnAttemptCarriesWhatTheTunerMeasuredWhileItWasStreaming()
    {
        ScanOutcome outcome = await ScanOne(ChannelScript.Carrying(SyntheticStream.Carrying(
            SomeStreamId,
            new SyntheticService(SomeServiceId, "Carina One")).ToBytes()));

        ScanRunAttempt attempt = Single(outcome);

        Assert.NotNull(attempt.Measurement);
        Assert.True(attempt.Measurement.Locked);
        Assert.Equal(21_500, attempt.Measurement.CnrMilliDecibels);
    }

    [Fact]
    public async Task AStreamThatArrivedWholeIsNotDemotedByALockLostAtTheEndOfIt()
    {
        ScanOutcome outcome = await ScanOne(ChannelScript.Carrying(SyntheticStream.Carrying(
            SomeStreamId,
            new SyntheticService(SomeServiceId, "Carina One")).ToBytes()) with
        {
            Lock = SignalLock.NotLocked,
        });

        ScanRunAttempt attempt = Single(outcome);

        Assert.Equal(ScanAttemptOutcome.Succeeded, attempt.Outcome);
        Assert.False(attempt.Measurement!.Locked);
    }

    [Fact]
    public async Task AOneSegServiceIsProposedUnderItsOwnCategoryRatherThanAsTelevision()
    {
        ScanOutcome outcome = await ScanOne(ChannelScript.Carrying(new SyntheticStream
        {
            NetworkId = SyntheticStream.SomeNetworkId,
            TransportStreamId = SomeStreamId,
            Services =
            [
                new SyntheticService(SomeServiceId, "Carina One"),
                new SyntheticService(50108, "Carina One Mobile", ServiceKind.Television, PartiallyReceived: true),
            ],
        }.ToBytes()));

        Assert.Equal(
            [ServiceCategory.Television, ServiceCategory.OneSeg],
            outcome.Difference.Added.Select(change => change.Category));
    }

    [Fact]
    public async Task EveryScanSessionIsClosedEvenWhenTheChannelCarriesNothing()
    {
        ScriptedDriverClient driver = new ScriptedDriverClient().Script(Channel53, ChannelScript.Silent());
        var harness = new ScanHarness(driver);

        await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        Assert.Single(driver.Stopped);
    }

    private static async Task<ScanOutcome> ScanOne(ChannelScript script)
    {
        var harness = new ScanHarness(new ScriptedDriverClient().Script(Channel53, script));

        return await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
    }

    private static ScanRunAttempt Single(ScanOutcome outcome) => Assert.Single(outcome.Attempts);

    [Fact]
    public async Task AChannelThatKeepsFailingWaitsLongerEachTimeBeforeItIsTriedAgain()
    {
        ScanHarness harness = Failing();
        CandidateChannel candidate = harness.Candidates.Candidates[0];

        await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        TimeSpan first = candidate.NextAttemptAt!.Value - At;

        harness.Runs.Runs.Clear();
        await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        TimeSpan second = candidate.NextAttemptAt!.Value - At;

        Assert.Equal(RotationState.BackingOff, candidate.RotationState);
        Assert.Equal(TimeSpan.FromMinutes(1), first);
        Assert.Equal(TimeSpan.FromMinutes(2), second);
    }

    [Fact]
    public async Task AChannelThatFailsUpToTheCeilingLeavesTheRotationAndSaysSoOutLoud()
    {
        ScanHarness harness = Failing();
        CandidateChannel candidate = harness.Candidates.Candidates[0];

        for (int round = 0; round < 2; round++)
        {
            harness.Runs.Runs.Clear();
            await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        }

        ScanOutcome outcome = await LastRun(harness);
        RotationDeparture departure = Assert.Single(outcome.Difference.Departures);

        Assert.Equal(RotationState.NeedsAttention, candidate.RotationState);
        Assert.False(candidate.IsInRotation);
        Assert.Equal(Channel53, departure.Tuning);
        Assert.Equal(SomeServiceId, departure.ServiceId.Value);
        Assert.Equal(3, departure.ConsecutiveFailures);
    }

    [Fact]
    public async Task AChannelThatLeftTheRotationIsFoundAgainAmongThoseNeedingAttention()
    {
        ScanHarness harness = Failing();

        for (int round = 0; round < 3; round++)
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
        ScanHarness harness = Failing();

        harness.Runs.Runs.Clear();
        ScanOutcome first = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        harness.Runs.Runs.Clear();
        ScanOutcome second = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        harness.Runs.Runs.Clear();
        ScanOutcome third = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        harness.Runs.Runs.Clear();
        ScanOutcome fourth = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        Assert.Empty(first.Difference.Departures);
        Assert.Empty(second.Difference.Departures);
        Assert.Single(third.Difference.Departures);
        Assert.Empty(fourth.Difference.Departures);
    }

    [Fact]
    public async Task AChannelThatCarriesItsServiceAgainIsPutBackIntoTheRotation()
    {
        ScanHarness harness = Failing();
        CandidateChannel candidate = harness.Candidates.Candidates[0];

        await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);
        Assert.Equal(RotationState.BackingOff, candidate.RotationState);

        harness.Runs.Runs.Clear();
        harness.Driver.Script(Channel53, ChannelScript.Carrying(SyntheticStream.Carrying(
            SomeStreamId,
            new SyntheticService(SomeServiceId, "Carina One")).ToBytes()));

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
                new SyntheticService(50109, "Carina Nine")).ToBytes())),
            settings: BacksOffTwice);
        Store(harness);

        await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        Assert.Equal(1, harness.Candidates.Candidates[0].ConsecutiveFailures);
    }

    [Fact]
    public async Task AChannelThatLockedAndDeliveredNothingIsCountedAsAFailure()
    {
        var harness = new ScanHarness(
            new ScriptedDriverClient().Script(Channel53, ChannelScript.Silent()),
            settings: BacksOffTwice);
        Store(harness);

        await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        Assert.Equal(1, harness.Candidates.Candidates[0].ConsecutiveFailures);
    }

    [Theory]
    [InlineData("tables that never completed")]
    [InlineData("tables that disagree with each other")]
    public async Task AChannelThatWasReceivedIsNotBlamedForWhatTheTablesSaid(string heard)
    {
        var harness = new ScanHarness(
            new ScriptedDriverClient().Script(Channel53, ChannelScript.Carrying(new SyntheticStream
            {
                NetworkId = SomeNetworkId,
                TransportStreamId = SomeStreamId,
                TransportStreamIdInNetwork = heard == "tables that disagree with each other" ? SomeStreamId + 1 : null,
                Services = [new SyntheticService(SomeServiceId, "Carina One")],
                WithoutDescription = heard == "tables that never completed",
            }.ToBytes())),
            settings: BacksOffTwice);
        Store(harness);

        await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        CandidateChannel candidate = harness.Candidates.Candidates[0];

        Assert.Equal(0, candidate.ConsecutiveFailures);
        Assert.Equal(RotationState.Active, candidate.RotationState);
    }

    [Fact]
    public async Task ASlotThatCarriesAnotherStreamThanItWasTunedForIsCountedAsAFailure()
    {
        var slot = TuningParameters.Bs(1, new TransportStreamId(SomeStreamId));
        var harness = new ScanHarness(
            new ScriptedDriverClient().Script(slot, ChannelScript.Carrying(SyntheticStream.Carrying(
                SomeStreamId + 1,
                new SyntheticService(SomeServiceId, "Carina One")).ToBytes())),
            settings: BacksOffTwice);
        var networkId = new NetworkId(SomeNetworkId);
        var serviceId = new ServiceId(SomeServiceId);

        harness.Services.Services.Add(
            BroadcastService.Discover(networkId, serviceId, "Carina One", ServiceCategory.Television, At));
        harness.Candidates.Candidates.Add(
            CandidateChannel.Discover(CandidateChannelId.New(), networkId, serviceId, slot, At));

        await harness.Orchestrator.RunAsync(ScanScope.Over([slot]), Cancel);

        Assert.Equal(1, harness.Candidates.Candidates[0].ConsecutiveFailures);
    }

    [Fact]
    public async Task AChannelNoScanWalkedIsLeftWhereItWas()
    {
        ScanHarness harness = Failing();

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

    private sealed class Stopping(ScanStop stop) : IScanRunObserver
    {
        public ScanStop Stop => stop;

        public void Started(ScanRun run)
        {
        }
    }

    private static async Task<ScanOutcome> StoppedAsync(ScanStop stop)
    {
        var stream = PacedStream.InChunksOf(
            SyntheticStream.Carrying(50002, new SyntheticService(50101, "Carina One")).ToBytes(),
            188);
        ScriptedDriverClient driver = new ScriptedDriverClient().Script(Channel53, new ChannelScript { Paced = () => stream });
        var harness = new ScanHarness(driver);

        using var stopping = new CancellationTokenSource();

        Task<ScanOutcome> scan = Task.Run(
            () => harness.Orchestrator.RunAsync(
                ScanScope.Over([Channel53, Channel55]),
                new Stopping(stop),
                stopping.Token),
            CancellationToken.None);

        stream.AwaitParkedBefore(1);
        await stopping.CancelAsync();

        return await scan;
    }

    [Fact]
    public async Task AScanTheOperatorStoppedIsRecordedAsCancelled()
    {
        ScanOutcome outcome = await StoppedAsync(ScanStop.AsRequested);

        Assert.Equal(ScanRunState.Cancelled, outcome.State);
        Assert.Equal(ScanConclusion.CancelledReason, outcome.Run!.Reason);
    }

    [Fact]
    public async Task ADeploymentRestartIsNotRecordedAsSomethingTheOperatorDid()
    {
        ScanOutcome outcome = await StoppedAsync(ScanStop.BecauseTheAppIsStopping);

        Assert.NotEqual(ScanRunState.Cancelled, outcome.State);
        Assert.Equal(ScanRunState.Failed, outcome.State);
        Assert.Equal(ScanConclusion.AppStoppingReason, outcome.Run!.Reason);
    }

    [Fact]
    public async Task TheRemoteControlNumberTheStreamDeclaresRidesOnTheProposal()
    {
        ScriptedDriverClient driver = new ScriptedDriverClient().Script(
            Channel53,
            ChannelScript.Carrying(new SyntheticStream
            {
                NetworkId = SyntheticStream.SomeNetworkId,
                TransportStreamId = SomeStreamId,
                Services = [new SyntheticService(SomeServiceId, "Carina One")],
                RemoteControlKeyId = 6,
            }.ToBytes()));

        ScanOutcome outcome = await new ScanHarness(driver).Orchestrator.RunAsync(
            ScanScope.Over([Channel53]),
            Cancel);

        ScanServiceChange added = Assert.Single(outcome.Difference.Added);

        Assert.Equal(6, added.RemoteControlKeyId);
    }

    [Fact]
    public async Task AServiceUnchangedButNewlyNumberedIsStillProposed()
    {
        ScriptedDriverClient driver = new ScriptedDriverClient().Script(
            Channel53,
            ChannelScript.Carrying(new SyntheticStream
            {
                NetworkId = SyntheticStream.SomeNetworkId,
                TransportStreamId = SomeStreamId,
                Services = [new SyntheticService(SomeServiceId, "Carina One")],
                RemoteControlKeyId = 6,
            }.ToBytes()));
        var harness = new ScanHarness(driver);

        harness.Services.Services.Add(BroadcastService.Discover(
            new NetworkId(SyntheticStream.SomeNetworkId),
            new ServiceId(SomeServiceId),
            "Carina One",
            ServiceCategory.Television,
            StillClock.Now.UtcDateTime));
        harness.Candidates.Candidates.Add(CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(SyntheticStream.SomeNetworkId),
            new ServiceId(SomeServiceId),
            Channel53,
            StillClock.Now.UtcDateTime));

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(
            ScanScope.Over([Channel53]),
            Cancel);

        ScanServiceChange updated = Assert.Single(outcome.Difference.Updated);

        Assert.Equal(6, updated.RemoteControlKeyId);
    }

    [Fact]
    public async Task ACandidateThatNeverHeardItsStreamIdHearsItOnTheNextScan()
    {
        ScriptedDriverClient driver = new ScriptedDriverClient().Script(
            Channel53,
            ChannelScript.Carrying(SyntheticStream.Carrying(
                SomeStreamId,
                new SyntheticService(SomeServiceId, "Carina One")).ToBytes()));
        var harness = new ScanHarness(driver);

        harness.Services.Services.Add(BroadcastService.Discover(
            new NetworkId(SyntheticStream.SomeNetworkId),
            new ServiceId(SomeServiceId),
            "Carina One",
            ServiceCategory.Television,
            StillClock.Now.UtcDateTime));
        harness.Candidates.Candidates.Add(CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(SyntheticStream.SomeNetworkId),
            new ServiceId(SomeServiceId),
            Channel53,
            StillClock.Now.UtcDateTime));

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(
            ScanScope.Over([Channel53]),
            Cancel);

        ScanServiceChange updated = Assert.Single(outcome.Difference.Updated);
        ScanChannelChange restated = Assert.Single(updated.Channels);

        Assert.Equal(SomeStreamId, restated.TransportStreamId!.Value);
    }

    [Fact]
    public async Task AStreamThatDeclaresNoNumberProposesNone()
    {
        ScriptedDriverClient driver = new ScriptedDriverClient().Script(
            Channel53,
            ChannelScript.Carrying(SyntheticStream.Carrying(
                SomeStreamId,
                new SyntheticService(SomeServiceId, "Carina One")).ToBytes()));

        ScanOutcome outcome = await new ScanHarness(driver).Orchestrator.RunAsync(
            ScanScope.Over([Channel53]),
            Cancel);

        Assert.Null(Assert.Single(outcome.Difference.Added).RemoteControlKeyId);
    }

    [Fact]
    public async Task TwoSatelliteSlotsCarryingTheOneStreamProposeThatStreamOnce()
    {
        var carrying = ChannelScript.Carrying(SyntheticStream.Carrying(
            SomeStreamId,
            new SyntheticService(SomeServiceId, "Carina One")).ToBytes());
        ScriptedDriverClient driver = new ScriptedDriverClient()
            .Script(Slot9, carrying)
            .Script(Slot11, carrying);

        ScanOutcome outcome = await new ScanHarness(driver).Orchestrator.RunAsync(
            ScanScope.Over([Slot9, Slot11]),
            Cancel);

        Assert.Equal(2, outcome.Attempts.Count);
        Assert.Empty(outcome.Failures);

        ScanServiceChange added = Assert.Single(outcome.Difference.Added);

        Assert.Equal(Slot9, Assert.Single(added.Channels).Tuning);
        Assert.Contains(
            ScanTargetNames.Of(Slot9),
            outcome.Attempts[1].Detail!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSameServiceOnTwoStreamsKeepsBothAsCandidates()
    {
        ScriptedDriverClient driver = new ScriptedDriverClient()
            .Script(Channel53, ChannelScript.Carrying(SyntheticStream.Carrying(
                SomeStreamId,
                new SyntheticService(SomeServiceId, "Carina One")).ToBytes()))
            .Script(Channel55, ChannelScript.Carrying(SyntheticStream.Carrying(
                50003,
                new SyntheticService(SomeServiceId, "Carina One")).ToBytes()));

        ScanOutcome outcome = await new ScanHarness(driver).Orchestrator.RunAsync(
            ScanScope.Over([Channel53, Channel55]),
            Cancel);

        ScanServiceChange added = Assert.Single(outcome.Difference.Added);

        Assert.Equal([Channel53, Channel55], added.Channels.Select(channel => channel.Tuning));
    }

    [Fact]
    public async Task ABusyTunerIsWaitedForOnAWideningIntervalRatherThanAtAFixedOne()
    {
        var clock = new HurriedClock();
        ScriptedDriverClient driver = new ScriptedDriverClient
        {
            BusyRefusalsRemaining = 3,
        }.Script(Channel53, ChannelScript.Carrying(SyntheticStream.Carrying(
            SomeStreamId,
            new SyntheticService(SomeServiceId, "Carina One")).ToBytes()));

        ScanOutcome outcome = await Harness(driver, clock).Orchestrator.RunAsync(
            ScanScope.Over([Channel53]),
            Cancel);

        Assert.Equal([2d, 4, 8], clock.Waits.Select(wait => wait.TotalSeconds));
        Assert.Equal(ScanRunState.Completed, outcome.State);
    }

    [Fact]
    public async Task ATunerThatStaysBusyEndsTheScanWithAReasonInsteadOfRetryingForever()
    {
        var clock = new HurriedClock();
        var driver = new ScriptedDriverClient
        {
            BusyRefusalsRemaining = int.MaxValue,
        };

        ScanOutcome outcome = await Harness(driver, clock).Orchestrator.RunAsync(
            ScanScope.Over([Channel53, Channel55]),
            Cancel);

        Assert.Equal([2d, 4, 8, 16], clock.Waits.Select(wait => wait.TotalSeconds));
        Assert.Equal(ScanRunState.Failed, outcome.State);
        Assert.StartsWith(ChannelScanOrchestrator.BusyReason, outcome.Run!.Reason, StringComparison.Ordinal);
        Assert.Contains("Every usable tuner is busy.", outcome.Run.Reason, StringComparison.Ordinal);
        Assert.Empty(outcome.Attempts);
    }

    [Fact]
    public async Task AChannelThatDidNotLockIsRecordedAndTheSweepWalksOn()
    {
        ScriptedDriverClient driver = new ScriptedDriverClient()
            .Script(Channel53, ChannelScript.Carrying(SyntheticStream.Carrying(
                SomeStreamId,
                new SyntheticService(SomeServiceId, "Carina One")).ToBytes()))
            .Script(Channel55, new ChannelScript
            {
                Refusal = new DriverProblem(
                    "noLock",
                    ["/dev/dvb/adapter0/frontend0: the frontend did not lock within 5 seconds."]),
            })
            .Script(Channel57, ChannelScript.Carrying(SyntheticStream.Carrying(
                50003,
                new SyntheticService(50102, "Carina Two")).ToBytes()));

        ScanOutcome outcome = await new ScanHarness(driver).Orchestrator.RunAsync(
            ScanScope.Over([Channel53, Channel55, Channel57]),
            Cancel);

        Assert.Equal(ScanRunState.Completed, outcome.State);
        Assert.Equal(
            [ScanAttemptOutcome.Succeeded, ScanAttemptOutcome.NoLock, ScanAttemptOutcome.Succeeded],
            outcome.Attempts.Select(attempt => attempt.Outcome));
        Assert.Contains("did not lock", outcome.Attempts[1].Detail!, StringComparison.Ordinal);
        Assert.Equal(2, outcome.Difference.Added.Count);
    }

    [Fact]
    public async Task AStreamTheDriverToreDownIsRecordedOnItsBytesAndTheSweepWalksOn()
    {
        ScriptedDriverClient driver = new ScriptedDriverClient()
            .Script(Channel53, ChannelScript.Carrying(SyntheticStream.Carrying(
                SomeStreamId,
                new SyntheticService(SomeServiceId, "Carina One")).ToBytes()))
            .Script(Channel55, new ChannelScript { Paced = () => PacedStream.Torn() })
            .Script(Channel57, ChannelScript.Carrying(SyntheticStream.Carrying(
                50003,
                new SyntheticService(50102, "Carina Two")).ToBytes()));

        ScanOutcome outcome = await new ScanHarness(driver).Orchestrator.RunAsync(
            ScanScope.Over([Channel53, Channel55, Channel57]),
            Cancel);

        Assert.Equal(ScanRunState.Completed, outcome.State);
        Assert.Equal(
            [
                ScanAttemptOutcome.Succeeded,
                ScanAttemptOutcome.LockedWithoutData,
                ScanAttemptOutcome.Succeeded,
            ],
            outcome.Attempts.Select(attempt => attempt.Outcome));
    }

    [Fact]
    public async Task AnAttemptThatRunsOutOfPatienceIsRecordedOnWhatItManagedToRead()
    {
        var clock = new HeldClock();
        var stream = PacedStream.InChunksOf(
            SyntheticStream.Carrying(SomeStreamId, new SyntheticService(SomeServiceId, "Carina One")).ToBytes(),
            188);
        ScriptedDriverClient driver = new ScriptedDriverClient().Script(Channel53, new ChannelScript { Paced = () => stream });
        var harness = new ScanHarness(driver, clock);

        Task<ScanOutcome> scan = Task.Run(() => harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel), Cancel);

        stream.Allow(1);
        stream.AwaitParkedBefore(2);
        clock.FireOnePending("the patience of the attempt");

        ScanOutcome outcome = await scan;

        Assert.Equal(ScanAttemptOutcome.IncompleteTables, Assert.Single(outcome.Attempts).Outcome);
        Assert.Equal(ScanRunState.Completed, outcome.State);
    }

    [Fact]
    public async Task ADriverThatComesBackAsAnotherInstanceEndsTheScanRatherThanFinishingOnHalfOfIt()
    {
        var stream = PacedStream.InChunksOf(
            SyntheticStream.Carrying(SomeStreamId, new SyntheticService(SomeServiceId, "Carina One")).ToBytes(),
            188);
        ScriptedDriverClient driver = new ScriptedDriverClient().Script(Channel53, new ChannelScript { Paced = () => stream });
        var harness = new ScanHarness(driver);

        Task<ScanOutcome> scan = Task.Run(
            () => harness.Orchestrator.RunAsync(ScanScope.Over([Channel53, Channel55]), Cancel),
            Cancel);

        stream.AwaitParkedBefore(1);
        harness.RestartTheDriver();

        ScanOutcome outcome = await scan;

        Assert.Equal(ScanRunState.Interrupted, outcome.State);
        Assert.Equal([Channel53], driver.Started);
        Assert.True(outcome.Difference.ChangesNothing);
    }

    [Fact]
    public async Task ADriverThatGoesAwayMidWalkFailsTheScanInsteadOfProposingWhatItAlreadyHas()
    {
        var carrying = ChannelScript.Carrying(SyntheticStream.Carrying(
            SomeStreamId,
            new SyntheticService(SomeServiceId, "Carina One")).ToBytes());
        ScriptedDriverClient driver = new ScriptedDriverClient
        {
            UnreachableFrom = "the socket went away",
        }
            .Script(Channel53, carrying)
            .Script(Channel55, carrying);

        ScanOutcome outcome = await new ScanHarness(driver).Orchestrator.RunAsync(
            ScanScope.Over([Channel53, Channel55, Channel57]),
            Cancel);

        Assert.Equal(ScanRunState.Failed, outcome.State);
        Assert.Equal("the socket went away", outcome.Run!.Reason);
        Assert.True(outcome.Difference.ChangesNothing);
    }

    [Fact]
    public async Task ASecondScanIsRefusedAndNamesTheOneAlreadyRunning()
    {
        var harness = new ScanHarness(new ScriptedDriverClient());
        var running = ScanRun.Start(ScanRunId.New(), "instance-a", DateTime.UtcNow);
        harness.Runs.Runs.Add(running);

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        Assert.False(outcome.WasStarted);
        Assert.Equal(running.Id, outcome.AlreadyRunning);
    }

    [Fact]
    public async Task AScanIsNotStartedAtAllWhileTheDriverIsOutOfReach()
    {
        var harness = new ScanHarness(new ScriptedDriverClient { GreetingFailure = "no socket" });

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Over([Channel53]), Cancel);

        Assert.False(outcome.WasStarted);
        Assert.Equal("no socket", outcome.CouldNotStartBecause);
        Assert.Empty(harness.Runs.Runs);
    }

    [Fact]
    public async Task TheWholeRangeOfASystemIsWalkedWhenNoTargetsAreNamed()
    {
        var harness = new ScanHarness(new ScriptedDriverClient());

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Of(TuneSystem.IsdbT), Cancel);

        Assert.Equal(50, outcome.Attempts.Count);
        Assert.Equal(
            TuningParameters.Terrestrial(BroadcastStandards.TerrestrialFirstChannel),
            outcome.Attempts[0].Tuning);
        Assert.Equal(
            TuningParameters.Terrestrial(BroadcastStandards.TerrestrialLastChannel),
            outcome.Attempts[^1].Tuning);
    }

    [Fact]
    public async Task TheSatelliteSlotsWalkedAreTheStreamsAlreadyRecordedForThem()
    {
        var harness = new ScanHarness(new ScriptedDriverClient());
        harness.SatelliteStreams.Streams.Add(
            SatelliteTransportStream.Rehydrate(9, 0, new TransportStreamId(SomeStreamId)));
        harness.SatelliteStreams.Streams.Add(
            SatelliteTransportStream.Rehydrate(3, 0, new TransportStreamId(50003)));

        ScanOutcome outcome = await harness.Orchestrator.RunAsync(ScanScope.Of(TuneSystem.IsdbSBs), Cancel);

        Assert.Equal(
            [
                TuningParameters.Bs(3, new TransportStreamId(50003)),
                TuningParameters.Bs(9, new TransportStreamId(SomeStreamId)),
            ],
            outcome.Attempts.Select(attempt => attempt.Tuning));
    }

    private static ScanHarness Harness(ScriptedDriverClient driver, TimeProvider clock)
        => new(
            driver,
            clock,
            ScanSettings.Default with { AttemptPatience = Timeout.InfiniteTimeSpan });

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
                new ChannelLineupDomainService(harness.Services, harness.Candidates),
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
