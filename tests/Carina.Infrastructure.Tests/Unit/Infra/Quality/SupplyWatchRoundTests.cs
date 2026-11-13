using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Quality;
using Carina.Infrastructure.Tests.Fixtures.Quality;

using Puppis.Contracts;

namespace Carina.Infrastructure.Tests.Unit.Infra.Quality;

[Trait("Category", "Unit")]
public sealed class SupplyWatchRoundTests
{
    private const string TheChannel = "32736-1024";

    private const string FirstSatellite = "adapter0";

    private const string SecondSatellite = "adapter2";

    private static readonly DateTime Noon = SupplyWatchHarness.Noon;

    private static readonly TimeSpan Shipped = TimeSpan.FromSeconds(300);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan BetweenPasses = TimeSpan.FromMinutes(5);

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

    [Fact(DisplayName = "a visit overdue for less than a sweep takes to come round is not quiet, though the shared threshold has passed")]
    public async Task AVisitOverdueForLessThanASweepTakesToComeRoundIsNotQuiet()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Visited(Noon - SupplyWatchHarness.Collection.BetweenVisits - (Shipped * 2));

        SupplyWatchPass pass = await harness.Round().WatchAsync(Cancel);

        Assert.Empty(harness.Incidents.Incidents);
        Assert.Equal(
            new SupplySilenceStanding(SupplySilence.GuideVisits, 1, 0),
            Assert.Single(pass.Standing.Supplies, supply => supply.Silence is SupplySilence.GuideVisits));
    }

    [Fact(DisplayName = "the record of a visit ledger that went quiet keeps the sweep it was held against rather than the shared threshold")]
    public async Task TheRecordOfAVisitLedgerThatWentQuietKeepsTheSweepItWasHeldAgainst()
    {
        SupplyWatchHarness harness = new();
        TimeSpan round = SupplyWatchHarness.Collection.LongestBetweenAttempts();

        harness.HoldingNothing();
        harness.Visited(Noon - SupplyWatchHarness.Collection.BetweenVisits - round);

        SupplyWatchPass pass = await harness.Round().WatchAsync(Cancel);

        QualityIncident opened = Assert.Single(harness.Incidents.Incidents);

        Assert.Equal(round.TotalSeconds, opened.Observed);
        Assert.Equal(round.TotalSeconds, opened.Applied.Current);
        Assert.Equal(Shipped.TotalSeconds, pass.Standing.Applied.Current);
    }

    [Fact(DisplayName = "a visit ledger that went quiet clears once something is attempted on it again")]
    public async Task AVisitLedgerThatWentQuietClearsOnceSomethingIsAttemptedOnItAgain()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Visited(Noon - TimeSpan.FromHours(7));

        await harness.Round().WatchAsync(Cancel);

        harness.Supply.Readings.Clear();
        harness.Visited(Noon - SupplyWatchHarness.Collection.BetweenVisits - TimeSpan.FromMinutes(1));

        SupplyWatchPass second = await harness.Round().WatchAsync(Cancel);

        Assert.Equal(1, second.Resolved);
        Assert.Equal(QualityIncidentState.Resolved, Assert.Single(harness.Incidents.Incidents).State);
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

    [Fact(DisplayName = "BR-QD-020: a recording of the last day that lost more than the warning level opens an incident on its channel, and it is told about in the same pass")]
    public async Task ARecordingOfTheLastDayThatLostMoreThanTheWarningLevelOpensAnIncidentOnItsChannel()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Ledger.Rows.Add(Recorded(Noon - TimeSpan.FromHours(2), dropped: 500));

        SupplyWatchPass pass = await harness.Round().WatchAsync(Cancel);

        QualityIncident opened = Assert.Single(harness.Incidents.Incidents);

        Assert.Equal(QualityThresholdKey.PacketsLostWarning, opened.Breached);
        Assert.Equal(QualitySubject.Of(QualitySubjectKind.Channel, TheChannel), opened.Subject);
        Assert.Equal(0.0005, opened.Observed);
        Assert.Equal(QualityThresholdShapes.Of(QualityThresholdKey.PacketsLostWarning).Shipped, opened.Applied.Current);
        Assert.Equal(QualityIncidentOwner.Quality, opened.Owner);
        Assert.Equal(Noon, opened.DetectedAt);
        Assert.Equal(QualityIncidentState.Notified, opened.State);
        Assert.Equal(1, pass.Opened);
        Assert.Equal(1, pass.Notified);
        Assert.Equal([AppEventName.Quality], harness.Events.Signalled);
    }

    [Fact(DisplayName = "BR-QD-020: the readings are the ones of the day that ends at the pass")]
    public async Task TheReadingsAreTheOnesOfTheDayThatEndsAtThePass()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();

        await harness.Round().WatchAsync(Cancel);

        QualityPeriod recorded = Assert.Single(harness.Ledger.Asked);
        QualityPeriod received = Assert.Single(harness.Signals.Asked);

        Assert.Equal(Noon - TimeSpan.FromHours(24), recorded.From);
        Assert.Equal(Noon, recorded.Until);
        Assert.Equal(recorded, received);
    }

    [Fact(DisplayName = "BR-QS-002: a breach that goes on is neither opened again nor told about again")]
    public async Task ABreachThatGoesOnIsNeitherOpenedAgainNorToldAboutAgain()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Ledger.Rows.Add(Recorded(Noon - TimeSpan.FromHours(2), dropped: 500));

        await harness.Round().WatchAsync(Cancel);

        harness.Clock.Turn(TimeSpan.FromMinutes(5));
        harness.Ledger.Rows.Add(Recorded(Noon - TimeSpan.FromHours(1), dropped: 900));

        SupplyWatchPass second = await harness.Round().WatchAsync(Cancel);

        QualityIncident standing = Assert.Single(harness.Incidents.Incidents);

        Assert.Equal(Noon, standing.NotifiedAt);
        Assert.Equal(0.0005, standing.Observed);
        Assert.Equal(0, second.Opened);
        Assert.Equal(0, second.Notified);
        Assert.Equal(0, second.Resolved);
        Assert.Single(harness.Events.Signalled);
    }

    [Fact(DisplayName = "BR-QS-002: a breach is resolved once the recording that passed the level is older than a day, the record of it stays, and the same channel passing it again is a new one")]
    public async Task ABreachIsResolvedOnceTheRecordingIsOlderThanADayAndComingBackIsANewOne()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Ledger.Rows.Add(Recorded(Noon - TimeSpan.FromHours(2), dropped: 500));

        await harness.Round().WatchAsync(Cancel);

        harness.Clock.Turn(TimeSpan.FromHours(23));

        SupplyWatchPass cleared = await harness.Round().WatchAsync(Cancel);

        QualityIncident first = Assert.Single(harness.Incidents.Incidents);

        Assert.Equal(QualityIncidentState.Resolved, first.State);
        Assert.Equal(Noon + TimeSpan.FromHours(23), first.ResolvedAt);
        Assert.Equal(1, cleared.Resolved);

        harness.Ledger.Rows.Add(Recorded(Noon + TimeSpan.FromHours(22), dropped: 700));
        harness.Clock.Turn(TimeSpan.FromMinutes(5));

        await harness.Round().WatchAsync(Cancel);

        Assert.Equal(2, harness.Incidents.Incidents.Count);
        Assert.Equal(QualityIncidentState.Resolved, first.State);

        QualityIncident again = harness.Incidents.Incidents.Single(incident => !incident.HasSettled);

        Assert.NotEqual(first.Id, again.Id);
        Assert.Equal(0.0007, again.Observed);
        Assert.Equal(3, harness.Events.Signalled.Count);
    }

    [Fact(DisplayName = "BR-QV-002: a level moved after a breach was opened closes the breach it no longer names and leaves the level on the record as it was")]
    public async Task ALevelMovedAfterABreachWasOpenedLeavesTheLevelOnTheRecordAsItWas()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Ledger.Rows.Add(Recorded(Noon - TimeSpan.FromHours(2), dropped: 500));

        await harness.Round().WatchAsync(Cancel);

        harness.Clock.Turn(TimeSpan.FromMinutes(5));
        harness.Thresholds.Thresholds.Add(QualityThreshold.Rehydrate(
            QualityThresholdKey.PacketsLostWarning,
            Threshold.Of(0.0002, 0.0008, provisional: true, 0, Noon),
            "someone"));

        await harness.Round().WatchAsync(Cancel);

        QualityIncident closed = Assert.Single(harness.Incidents.Incidents);

        Assert.Equal(QualityIncidentState.Resolved, closed.State);
        Assert.Equal(0.0002, closed.Applied.Current);
    }

    [Fact(DisplayName = "BR-QD-020: a channel that got worse than its warning has the warning closed and the heavier level opened, so one stands for it at a time")]
    public async Task AChannelThatGotWorseHasTheWarningClosedAndTheHeavierLevelOpened()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Ledger.Rows.Add(Recorded(Noon - TimeSpan.FromHours(2), dropped: 500));

        await harness.Round().WatchAsync(Cancel);

        harness.Clock.Turn(TimeSpan.FromMinutes(5));
        harness.Ledger.Rows.Add(Recorded(Noon - TimeSpan.FromHours(1), dropped: 5_000));

        await harness.Round().WatchAsync(Cancel);

        QualityIncident standing = Assert.Single(harness.Incidents.Incidents, incident => !incident.HasSettled);

        Assert.Equal(QualityThresholdKey.PacketsLostUnwatchable, standing.Breached);
        Assert.Equal(0.005, standing.Observed);
        Assert.Equal(
            QualityThresholdKey.PacketsLostWarning,
            Assert.Single(harness.Incidents.Incidents, incident => incident.HasSettled).Breached);
    }

    [Fact(DisplayName = "BR-QD-020: a tuner whose signal read beyond a level over the last day opens an incident on the tuner")]
    public async Task ATunerWhoseSignalReadBeyondALevelOpensAnIncidentOnTheTuner()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Signals.Figures.Add(Figures(SupplyWatchHarness.Device, locked: 100, bitErrors: 0.01));

        await harness.Round().WatchAsync(Cancel);

        QualityIncident opened = Assert.Single(harness.Incidents.Incidents);

        Assert.Equal(QualityThresholdKey.BitErrorRateCeiling, opened.Breached);
        Assert.Equal(QualitySubject.Of(QualitySubjectKind.Tuner, SupplyWatchHarness.Device), opened.Subject);
        Assert.Equal(0.01, opened.Observed);
        Assert.False(opened.Restated);
    }

    [Fact(DisplayName = "BR-QD-021: one bad sample among the sound ones of the last day opens nothing")]
    public async Task OneBadSampleAmongTheSoundOnesOfTheLastDayOpensNothing()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Samples.Samples.AddRange(Enumerable.Range(1, 20).Select(turn => Sound(Noon.AddMinutes(-turn))));
        harness.Samples.Samples.Add(Bad(Noon.AddMinutes(-30)));

        SupplyWatchPass pass = await harness.RoundOverTheSamples().WatchAsync(Cancel);

        Assert.Empty(harness.Incidents.Incidents);
        Assert.Equal(0, pass.Opened);
    }

    [Fact(DisplayName = "BR-QD-021: a tuner that goes on reading badly is opened at what it usually read, and is closed once it reads well again")]
    public async Task ATunerThatGoesOnReadingBadlyIsOpenedAndIsClosedOnceItReadsWellAgain()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Samples.Samples.AddRange(Enumerable.Range(1, 4).Select(turn => Sound(Noon.AddMinutes(-30 - turn))));
        harness.Samples.Samples.AddRange(Enumerable.Range(1, 5).Select(turn => Bad(Noon.AddMinutes(-turn))));

        await harness.RoundOverTheSamples().WatchAsync(Cancel);

        Assert.Equal(
            [
                (QualityThresholdKey.CarrierToNoiseFloor, 6_000d),
                (QualityThresholdKey.BitErrorRateCeiling, 0.02),
            ],
            harness.Incidents.Incidents
                .OrderBy(incident => incident.Breached)
                .Select(incident => (incident.Breached, incident.Observed)));
        Assert.All(
            harness.Incidents.Incidents,
            incident => Assert.Equal(QualitySubject.Of(QualitySubjectKind.Tuner, SupplyWatchHarness.Device), incident.Subject));

        harness.Clock.Turn(TimeSpan.FromMinutes(10));
        harness.Samples.Samples.AddRange(Enumerable.Range(1, 2).Select(turn => Sound(Noon.AddMinutes(turn))));

        SupplyWatchPass pass = await harness.RoundOverTheSamples().WatchAsync(Cancel);

        Assert.Equal(2, pass.Resolved);
        Assert.All(harness.Incidents.Incidents, incident => Assert.True(incident.HasSettled));
    }

    [Fact(DisplayName = "BR-QD-024: a weak channel on a tuner that usually reads well opens incidents on that channel and tuner, and they close once it reads well")]
    public async Task AWeakChannelOnATunerThatUsuallyReadsWellOpensIncidentsOnThatChannelAndTuner()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingNothing();
        harness.Samples.Samples.AddRange(Enumerable.Range(1, 20).Select(turn => Sound(Noon.AddMinutes(-turn))));
        harness.Samples.Samples.AddRange(Enumerable.Range(1, 5).Select(turn => Bad(Noon.AddMinutes(-30 - turn), service: 2_048)));

        SupplyWatchPass pass = await harness.RoundOverTheSamples().WatchAsync(Cancel);

        Assert.Equal(2, pass.Opened);
        Assert.Equal(
            [
                (QualityThresholdKey.CarrierToNoiseFloor, 6_000d),
                (QualityThresholdKey.BitErrorRateCeiling, 0.02),
            ],
            harness.Incidents.Incidents
                .OrderBy(incident => incident.Breached)
                .Select(incident => (incident.Breached, incident.Observed)));
        Assert.All(
            harness.Incidents.Incidents,
            incident => Assert.Equal(
                QualitySubject.Of(QualitySubjectKind.Reception, $"32736-2048@{SupplyWatchHarness.Device}"),
                incident.Subject));

        harness.Clock.Turn(TimeSpan.FromMinutes(10));
        harness.Samples.Samples.AddRange(Enumerable.Range(1, 6).Select(turn => Sound(Noon.AddMinutes(turn), service: 2_048)));

        SupplyWatchPass later = await harness.RoundOverTheSamples().WatchAsync(Cancel);

        Assert.Equal(2, later.Resolved);
        Assert.All(harness.Incidents.Incidents, incident => Assert.True(incident.HasSettled));
    }

    [Fact(DisplayName = "BR-QD-020: a tuner the driver says cannot lock is restated once and is not opened again as a lock rate of this domain's own")]
    public async Task ATunerTheDriverSaysCannotLockIsNotOpenedAgainAsALockRateOfThisDomainsOwn()
    {
        SupplyWatchHarness harness = new();

        harness.Answering(SupplyWatchHarness.NotLocking("adapter0"));
        harness.Signals.Figures.Add(Figures("adapter0", locked: 0, bitErrors: null));

        await harness.Round().WatchAsync(Cancel);

        QualityIncident restated = Assert.Single(harness.Incidents.Incidents);

        Assert.True(restated.Restated);
        Assert.Equal(TunerTroubles.CannotLockClassification, restated.Classification);

        harness.Driver.Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Unreachable("nobody is listening");
        harness.Clock.Turn(TimeSpan.FromMinutes(5));

        await harness.Round().WatchAsync(Cancel);

        Assert.Same(restated, Assert.Single(harness.Incidents.Incidents));
        Assert.False(restated.HasSettled);
    }

    [Fact(DisplayName = "BR-QD-020: a driver that cannot be asked does not keep a breach the ledger names from being opened, nor one it no longer names from being closed")]
    public async Task ADriverThatCannotBeAskedDoesNotKeepABreachFromBeingOpenedOrClosed()
    {
        SupplyWatchHarness harness = new();

        harness.Driver.Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Unreachable("nobody is listening");
        harness.Ledger.Rows.Add(Recorded(Noon - TimeSpan.FromHours(2), dropped: 500));

        await harness.Round().WatchAsync(Cancel);

        QualityIncident opened = Assert.Single(harness.Incidents.Incidents);

        Assert.Equal(QualityThresholdKey.PacketsLostWarning, opened.Breached);

        harness.Clock.Turn(TimeSpan.FromHours(23));

        await harness.Round().WatchAsync(Cancel);

        Assert.True(opened.HasSettled);
    }

    [Fact(DisplayName = "BR-QD-020: a breach and a silence on the same tuner are told apart, and the one ending leaves the other standing")]
    public async Task ABreachAndASilenceOnTheSameTunerAreToldApart()
    {
        SupplyWatchHarness harness = new();

        harness.HoldingATuner(Noon - TimeSpan.FromMinutes(30));
        harness.Signals.Figures.Add(Figures(SupplyWatchHarness.Device, locked: 100, bitErrors: 0.01));

        await harness.Round().WatchAsync(Cancel);

        Assert.Equal(
            [QualityThresholdKey.BitErrorRateCeiling, QualityThresholdKey.SupplySilence],
            harness.Incidents.Incidents.Select(incident => incident.Breached).Order());

        harness.HoldingNothing();
        harness.Clock.Turn(TimeSpan.FromMinutes(5));

        await harness.Round().WatchAsync(Cancel);

        Assert.Equal(
            QualityThresholdKey.BitErrorRateCeiling,
            Assert.Single(harness.Incidents.Incidents, incident => !incident.HasSettled).Breached);
    }

    [Fact(DisplayName = "BR-QS-002: two satellite tuners that do not lock for fifteen hours stand as one incident each, told about once")]
    public async Task TwoSatelliteTunersThatDoNotLockForFifteenHoursStandAsOneIncidentEachToldAboutOnce()
    {
        SupplyWatchHarness harness = new();

        harness.Answering(
            SupplyWatchHarness.NotLocking(FirstSatellite),
            SupplyWatchHarness.NotLocking(SecondSatellite),
            SupplyWatchHarness.Idle(SupplyWatchHarness.Device));

        for (TimeSpan watched = TimeSpan.Zero; watched <= TimeSpan.FromHours(15); watched += BetweenPasses)
        {
            await harness.Round().WatchAsync(Cancel);

            harness.Clock.Turn(BetweenPasses);
        }

        Assert.Equal(2, harness.Incidents.Incidents.Count);
        Assert.Equal(
            [QualitySubject.Of(QualitySubjectKind.Tuner, FirstSatellite), QualitySubject.Of(QualitySubjectKind.Tuner, SecondSatellite)],
            harness.Incidents.Incidents.Select(incident => incident.Subject));
        Assert.All(harness.Incidents.Incidents, incident =>
        {
            Assert.Equal(QualityThresholdKey.LockRate, incident.Breached);
            Assert.Equal(QualityIncidentOwner.Tuner, incident.Owner);
            Assert.Equal(TunerTroubles.CannotLockClassification, incident.Classification);
            Assert.Equal(QualityIncidentState.Notified, incident.State);
            Assert.Equal(Noon, incident.DetectedAt);
        });
        Assert.Equal(AppEventName.Quality, Assert.Single(harness.Events.Signalled));
        Assert.Equal(2, (await harness.Incidents.ListUnsettledAsync(Cancel)).Count);
    }

    [Fact(DisplayName = "BR-QV-002: the lock rate level in force when a tuner could not lock is kept on the record of it")]
    public async Task TheLockRateLevelInForceWhenATunerCouldNotLockIsKeptOnTheRecordOfIt()
    {
        SupplyWatchHarness harness = new();

        harness.Thresholds.Thresholds.Add(QualityThreshold.Declare(
            QualityThresholdKey.LockRate,
            Threshold.Of(0.99, 0.5, provisional: true, 0, Noon)));
        harness.Answering(SupplyWatchHarness.NotLocking(FirstSatellite));

        await harness.Round().WatchAsync(Cancel);

        QualityIncident opened = Assert.Single(harness.Incidents.Incidents);

        Assert.Equal(0.5, opened.Applied.Current);
        Assert.Equal(0d, opened.Observed);
    }

    [Fact(DisplayName = "BR-QS-002: a tuner the user has turned off raises nothing, whatever its health last said")]
    public async Task ATunerTheUserHasTurnedOffRaisesNothing()
    {
        SupplyWatchHarness harness = new();

        harness.Answering(SupplyWatchHarness.TurnedOff(FirstSatellite), SupplyWatchHarness.TurnedOff(SecondSatellite));

        SupplyWatchPass pass = await harness.Round().WatchAsync(Cancel);

        Assert.Empty(harness.Incidents.Incidents);
        Assert.False(pass.SaysAnything);
        Assert.Empty(harness.Events.Signalled);
    }

    [Fact(DisplayName = "BR-QS-002: a tuner turned off while it could not lock leaves the list")]
    public async Task ATunerTurnedOffWhileItCouldNotLockLeavesTheList()
    {
        SupplyWatchHarness harness = new();

        harness.Answering(SupplyWatchHarness.NotLocking(FirstSatellite));

        await harness.Round().WatchAsync(Cancel);

        harness.Clock.Turn(BetweenPasses);
        harness.Events.Signalled.Clear();
        harness.Answering(SupplyWatchHarness.TurnedOff(FirstSatellite));

        SupplyWatchPass pass = await harness.Round().WatchAsync(Cancel);

        Assert.Equal(1, pass.Resolved);
        Assert.Equal(QualityIncidentState.Resolved, Assert.Single(harness.Incidents.Incidents).State);
        Assert.Empty(await harness.Incidents.ListUnsettledAsync(Cancel));
        Assert.Equal(AppEventName.Quality, Assert.Single(harness.Events.Signalled));
    }

    [Fact(DisplayName = "BR-QS-002: a tuner the driver hands out again is resolved, and failing to lock again after that is a new record")]
    public async Task ATunerTheDriverHandsOutAgainIsResolvedAndFailingToLockAgainIsANewRecord()
    {
        SupplyWatchHarness harness = new();

        harness.Answering(SupplyWatchHarness.NotLocking(FirstSatellite));

        await harness.Round().WatchAsync(Cancel);

        harness.Clock.Turn(BetweenPasses);
        harness.Answering(SupplyWatchHarness.Idle(FirstSatellite));

        await harness.Round().WatchAsync(Cancel);

        harness.Clock.Turn(BetweenPasses);
        harness.Answering(SupplyWatchHarness.NotLocking(FirstSatellite));

        await harness.Round().WatchAsync(Cancel);

        Assert.Equal(2, harness.Incidents.Incidents.Count);
        Assert.Single(harness.Incidents.Incidents, incident => incident.HasSettled);
        Assert.Single(await harness.Incidents.ListUnsettledAsync(Cancel));
    }

    [Fact(DisplayName = "BR-QS-002: a driver that cannot be asked leaves a tuner that could not lock where it stood")]
    public async Task ADriverThatCannotBeAskedLeavesATunerThatCouldNotLockWhereItStood()
    {
        SupplyWatchHarness harness = new();

        harness.Answering(SupplyWatchHarness.NotLocking(FirstSatellite));

        await harness.Round().WatchAsync(Cancel);

        harness.Clock.Turn(BetweenPasses);
        harness.Driver.Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Unreachable("the socket is not there");

        SupplyWatchPass pass = await harness.Round().WatchAsync(Cancel);

        Assert.Equal(0, pass.Resolved);
        Assert.Equal(QualityIncidentState.Notified, Assert.Single(harness.Incidents.Incidents).State);
    }

    [Fact(DisplayName = "BR-QD-007: a tuner that cannot lock holds no session, so it is not also counted as a supply that went quiet")]
    public async Task ATunerThatCannotLockIsNotAlsoCountedAsASupplyThatWentQuiet()
    {
        SupplyWatchHarness harness = new();

        harness.Answering(SupplyWatchHarness.NotLocking(FirstSatellite));

        await harness.Round().WatchAsync(Cancel);

        Assert.DoesNotContain(harness.Incidents.Incidents, incident => incident.Breached is QualityThresholdKey.SupplySilence);
    }

    [Fact(DisplayName = "BR-QD-018: two satellite tuners the driver says are failing to tune stand as one incident each under that kind")]
    public async Task TwoSatelliteTunersFailingToTuneStandAsOneIncidentEachUnderThatKind()
    {
        SupplyWatchHarness harness = new();

        harness.Answering(
            SupplyWatchHarness.FailingToTune(FirstSatellite),
            SupplyWatchHarness.FailingToTune(SecondSatellite),
            SupplyWatchHarness.Idle(SupplyWatchHarness.Device));

        await harness.Round().WatchAsync(Cancel);

        harness.Clock.Turn(BetweenPasses);

        await harness.Round().WatchAsync(Cancel);

        Assert.Equal(2, harness.Incidents.Incidents.Count);
        Assert.All(harness.Incidents.Incidents, incident =>
        {
            Assert.Equal(QualityIncidentOwner.Tuner, incident.Owner);
            Assert.Equal("TuneFailing", incident.Classification);
            Assert.Equal(QualityIncidentState.Notified, incident.State);
        });
    }

    [Fact(DisplayName = "BR-QD-018: a driver started again that no longer says the tuners are failing resolves their incidents")]
    public async Task ADriverStartedAgainThatNoLongerSaysTheTunersAreFailingResolvesTheirIncidents()
    {
        SupplyWatchHarness harness = new();

        harness.Answering(SupplyWatchHarness.FailingToTune(FirstSatellite));

        await harness.Round().WatchAsync(Cancel);

        harness.Clock.Turn(BetweenPasses);
        harness.Answering(SupplyWatchHarness.Idle(FirstSatellite));

        SupplyWatchPass pass = await harness.Round().WatchAsync(Cancel);

        Assert.Equal(1, pass.Resolved);
        Assert.Empty(await harness.Incidents.ListUnsettledAsync(Cancel));
    }

    [Fact(DisplayName = "BR-QD-018: a tuner whose failing to tune reaches the ceiling moves from one kind of incident to the other")]
    public async Task ATunerWhoseFailingToTuneReachesTheCeilingMovesFromOneKindToTheOther()
    {
        SupplyWatchHarness harness = new();

        harness.Answering(SupplyWatchHarness.FailingToTune(FirstSatellite));

        await harness.Round().WatchAsync(Cancel);

        harness.Clock.Turn(BetweenPasses);
        harness.Answering(SupplyWatchHarness.NotLocking(FirstSatellite));

        await harness.Round().WatchAsync(Cancel);

        QualityIncident standing = Assert.Single(await harness.Incidents.ListUnsettledAsync(Cancel));

        Assert.Equal(TunerTroubles.CannotLockClassification, standing.Classification);
        Assert.Single(harness.Incidents.Incidents, incident => incident.HasSettled && incident.Classification == "TuneFailing");
    }

    private static QualityLedgerRow Recorded(DateTime startedAt, long dropped)
        => QualityLedgerRow.Of(
            RecordingId.New(),
            new NetworkId(32_736),
            new ServiceId(1_024),
            TuneSystem.IsdbT,
            new TunerDeviceId(SupplyWatchHarness.Device),
            startedAt,
            DropCounters.Counted(dropped, 1_000_000),
            0,
            0,
            startedAt,
            0,
            0);

    private static QualitySignalSample Sound(DateTime at, int service = 1_024) => Sample(at, 30_000, 0, service);

    private static QualitySignalSample Bad(DateTime at, int service = 1_024) => Sample(at, 6_000, 20_000, service);

    private static QualitySignalSample Sample(DateTime at, int carrierToNoise, long errorBits, int service)
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse("live-1"),
            at,
            SessionPurpose.Live,
            new TunerDeviceId(SupplyWatchHarness.Device),
            new NetworkId(32_736),
            new ServiceId(service),
            SignalSample.WithLock(at, carrierToNoise, at, [new LayerBitErrorCounts(1, errorBits, 1_000_000)], at));

    private static SignalFigures Figures(string tuner, long locked, double? bitErrors)
        => new(
            new TunerDeviceId(tuner),
            100,
            locked,
            0,
            0,
            locked is 0 ? null : 30_000,
            locked is 0 ? null : 30_000,
            bitErrors,
            bitErrors,
            [],
            Noon);
}
