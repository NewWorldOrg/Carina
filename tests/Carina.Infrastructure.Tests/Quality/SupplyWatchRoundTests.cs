using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Events;
using Carina.Domain.Quality;
using Carina.Infrastructure.Quality;

namespace Carina.Infrastructure.Tests.Quality;

public sealed class SupplyWatchRoundTests
{
    private static readonly DateTime Noon = SupplyWatchHarness.Noon;

    private static readonly TimeSpan Shipped = TimeSpan.FromSeconds(300);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "a recording in flight that has stopped being written to is quiet")]
    public async Task ARecordingInFlightThatHasStoppedBeingWrittenToIsQuiet()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Writing(Noon - TimeSpan.FromHours(1), TimeSpan.FromMinutes(50), Noon);

        await harness.Round().WatchAsync(Cancel);

        QualityIncident opened = Assert.Single(harness.Incidents.Incidents);

        Assert.Equal(QualityThresholdKey.SupplySilence, opened.Breached);
        Assert.Equal(SupplySilence.RecordingProgress, opened.Silence);
        Assert.Equal(QualitySubjectKind.Recording, opened.Subject.Kind);
        Assert.Equal(TimeSpan.FromMinutes(10).TotalSeconds, opened.Observed);
    }

    [Fact(DisplayName = "a recording in flight whose measurement has stopped moving is quiet")]
    public async Task ARecordingInFlightWhoseMeasurementHasStoppedMovingIsQuiet()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Writing(Noon - TimeSpan.FromHours(1), TimeSpan.FromHours(1), Noon - TimeSpan.FromMinutes(20));

        await harness.Round().WatchAsync(Cancel);

        QualityIncident opened = Assert.Single(harness.Incidents.Incidents);

        Assert.Equal(SupplySilence.RecordingMeasurement, opened.Silence);
    }

    [Fact(DisplayName = "a recording that is being written to and measured is not quiet")]
    public async Task ARecordingThatIsBeingWrittenToAndMeasuredIsNotQuiet()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Writing(Noon - TimeSpan.FromHours(1), TimeSpan.FromHours(1), Noon);

        await harness.Round().WatchAsync(Cancel);

        Assert.Empty(harness.Incidents.Incidents);
    }

    [Fact(DisplayName = "a tuner holding a session that no sample has come from is quiet")]
    public async Task ATunerHoldingASessionThatNoSampleHasComeFromIsQuiet()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingATuner(Noon - TimeSpan.FromMinutes(30));

        await harness.Round().WatchAsync(Cancel);

        QualityIncident opened = Assert.Single(harness.Incidents.Incidents);

        Assert.Equal(SupplySilence.SignalSamples, opened.Silence);
        Assert.Equal(SupplyWatchHarness.Device, opened.Subject.Key);
    }

    [Fact(DisplayName = "a tuner holding nothing is idle rather than quiet")]
    public async Task ATunerHoldingNothingIsIdleRatherThanQuiet()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();

        await harness.Round().WatchAsync(Cancel);

        Assert.Empty(harness.Incidents.Incidents);
    }

    [Fact(DisplayName = "a tuner whose session has only just opened is not quiet yet")]
    public async Task ATunerWhoseSessionHasOnlyJustOpenedIsNotQuietYet()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingATuner(Noon - TimeSpan.FromSeconds(30));

        await harness.Round().WatchAsync(Cancel);

        Assert.Empty(harness.Incidents.Incidents);
    }

    [Fact(DisplayName = "a visit the back-off has left overdue for longer than the threshold is quiet")]
    public async Task AVisitTheBackOffHasLeftOverdueForLongerThanTheThresholdIsQuiet()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Visited(Noon - TimeSpan.FromHours(7));

        await harness.Round().WatchAsync(Cancel);

        QualityIncident opened = Assert.Single(harness.Incidents.Incidents);

        Assert.Equal(SupplySilence.GuideVisits, opened.Silence);
        Assert.Equal(QualitySubject.TheGuideLedger, opened.Subject);
    }

    [Fact(DisplayName = "a visit that is not due again yet is not quiet")]
    public async Task AVisitThatIsNotDueAgainYetIsNotQuiet()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Visited(Noon - TimeSpan.FromHours(1));

        await harness.Round().WatchAsync(Cancel);

        Assert.Empty(harness.Incidents.Incidents);
    }

    [Fact(DisplayName = "the threshold a pass holds silence against is the one the setting carries now")]
    public async Task TheThresholdAPassHoldsSilenceAgainstIsTheOneTheSettingCarriesNow()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Writing(Noon - TimeSpan.FromHours(1), TimeSpan.FromMinutes(58), Noon);
        harness.Thresholds.Thresholds.Add(QualityThreshold.Declare(
            QualityThresholdKey.SupplySilence,
            Threshold.Of(Shipped.TotalSeconds, 60, provisional: true, 0, Noon)));

        SupplyWatchPass pass = await harness.Round().WatchAsync(Cancel);

        Assert.Equal(60, pass.Standing.Applied.Current);
        Assert.Equal(1, pass.Opened);
    }

    [Fact(DisplayName = "the threshold a silence was held against is kept on the record of it")]
    public async Task TheThresholdASilenceWasHeldAgainstIsKeptOnTheRecordOfIt()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Writing(Noon - TimeSpan.FromHours(1), TimeSpan.FromMinutes(50), Noon);

        await harness.Round().WatchAsync(Cancel);

        QualityIncident opened = Assert.Single(harness.Incidents.Incidents);

        Assert.Equal(Shipped.TotalSeconds, opened.Applied.Current);
        Assert.True(opened.Applied.Provisional);
    }

    [Fact(DisplayName = "a silence that goes on is neither opened again nor told about again")]
    public async Task ASilenceThatGoesOnIsNeitherOpenedAgainNorToldAboutAgain()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Writing(Noon - TimeSpan.FromHours(1), TimeSpan.FromMinutes(50), Noon);

        await harness.Round().WatchAsync(Cancel);

        harness.Events.Signalled.Clear();

        SupplyWatchPass second = await harness.Round().WatchAsync(Cancel);

        Assert.Single(harness.Incidents.Incidents);
        Assert.Equal(0, second.Opened);
        Assert.Equal(0, second.Notified);
        Assert.Empty(harness.Events.Signalled);
    }

    [Fact(DisplayName = "a silence is told about once when it starts and once when it ends")]
    public async Task ASilenceIsToldAboutOnceWhenItStartsAndOnceWhenItEnds()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingATuner(Noon - TimeSpan.FromMinutes(30));

        await harness.Round().WatchAsync(Cancel);

        Assert.Equal(AppEventName.Quality, Assert.Single(harness.Events.Signalled));
        Assert.Equal(QualityIncidentState.Notified, Assert.Single(harness.Incidents.Incidents).State);

        harness.Events.Signalled.Clear();
        harness.HoldingNothing();

        SupplyWatchPass second = await harness.Round().WatchAsync(Cancel);

        Assert.Equal(1, second.Resolved);
        Assert.Equal(AppEventName.Quality, Assert.Single(harness.Events.Signalled));
        Assert.Equal(QualityIncidentState.Resolved, Assert.Single(harness.Incidents.Incidents).State);
    }

    [Fact(DisplayName = "the same supply going quiet again is a new record rather than the old one reopened")]
    public async Task TheSameSupplyGoingQuietAgainIsANewRecordRatherThanTheOldOneReopened()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingATuner(Noon - TimeSpan.FromMinutes(30));

        await harness.Round().WatchAsync(Cancel);

        harness.HoldingNothing();

        await harness.Round().WatchAsync(Cancel);

        harness.HoldingATuner(Noon - TimeSpan.FromMinutes(30));

        await harness.Round().WatchAsync(Cancel);

        Assert.Equal(2, harness.Incidents.Incidents.Count);
        Assert.Single(harness.Incidents.Incidents, incident => incident.HasSettled);
    }

    [Fact(DisplayName = "a driver that will not say what it holds names no tuner as quiet")]
    public async Task ADriverThatWillNotSayWhatItHoldsNamesNoTunerAsQuiet()
    {
        SupplyWatchHarness harness = new();

        harness.Driver.Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Unreachable("the socket is not there");

        SupplyWatchPass pass = await harness.Round().WatchAsync(Cancel);

        Assert.False(pass.Standing.TunersWereAsked);
        Assert.Empty(harness.Incidents.Incidents);
    }

    [Fact(DisplayName = "a driver that cannot be asked leaves the silence it can no longer see standing")]
    public async Task ADriverThatCannotBeAskedLeavesTheSilenceItCanNoLongerSeeStanding()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingATuner(Noon - TimeSpan.FromMinutes(30));

        await harness.Round().WatchAsync(Cancel);

        QualityIncident opened = Assert.Single(harness.Incidents.Incidents);

        Assert.Equal(QualityIncidentState.Notified, opened.State);

        harness.Events.Signalled.Clear();
        harness.Driver.Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Unreachable("the socket is not there");

        SupplyWatchPass second = await harness.Round().WatchAsync(Cancel);

        Assert.False(second.Standing.TunersWereAsked);
        Assert.Equal(0, second.Opened);
        Assert.Equal(0, second.Resolved);
        Assert.Same(opened, Assert.Single(harness.Incidents.Incidents));
        Assert.Equal(QualityIncidentState.Notified, opened.State);
        Assert.Null(opened.ResolvedAt);
        Assert.Empty(harness.Events.Signalled);
    }

    [Fact(DisplayName = "the four supplies are counted apart from one another")]
    public async Task TheFourSuppliesAreCountedApartFromOneAnother()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingATuner(Noon - TimeSpan.FromMinutes(30));
        harness.Writing(Noon - TimeSpan.FromHours(1), TimeSpan.FromMinutes(50), Noon - TimeSpan.FromMinutes(20));
        harness.Visited(Noon - TimeSpan.FromHours(7));

        SupplyWatchPass pass = await harness.Round().WatchAsync(Cancel);

        Assert.Equal(4, pass.Standing.Supplies.Count);
        Assert.All(pass.Standing.Supplies, supply => Assert.Equal(1, supply.Quiet));
        Assert.Equal(
            [
                SupplySilence.RecordingProgress,
                SupplySilence.RecordingMeasurement,
                SupplySilence.SignalSamples,
                SupplySilence.GuideVisits,
            ],
            harness.Incidents.Incidents.Select(incident => incident.Silence!.Value).Order());
    }

    [Fact(DisplayName = "what the last pass read is what the supply health answers from")]
    public async Task WhatTheLastPassReadIsWhatTheSupplyHealthAnswersFrom()
    {
        SupplyWatchHarness harness = new();

        Assert.Null(harness.Board.Latest);

        harness.HoldingATuner(Noon - TimeSpan.FromMinutes(30));

        await harness.Round().WatchAsync(Cancel);

        SupplyStanding held = harness.Board.Latest!;

        Assert.NotNull(held);

        Assert.Equal(Noon, held.At);
        Assert.True(held.TunersWereAsked);
        Assert.Equal(Shipped.TotalSeconds, held.Applied.Current);
        Assert.Equal(
            1,
            Assert.Single(held.Supplies, supply => supply.Silence is SupplySilence.SignalSamples).Quiet);
    }
}
