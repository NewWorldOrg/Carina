using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Events;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Quality;

namespace Carina.Infrastructure.Tests.Quality;

public sealed class ThresholdBreachRoundTests
{
    private const string TheChannel = "32736-1024";

    private static readonly DateTime Noon = SupplyWatchHarness.Noon;

    private static readonly CancellationToken Cancel = CancellationToken.None;

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
            startedAt);

    private static QualitySignalSample Sound(DateTime at) => Sample(at, 30_000, 0);

    private static QualitySignalSample Bad(DateTime at) => Sample(at, 6_000, 20_000);

    private static QualitySignalSample Sample(DateTime at, int carrierToNoise, long errorBits)
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse("live-1"),
            at,
            SessionPurpose.Live,
            new TunerDeviceId(SupplyWatchHarness.Device),
            new NetworkId(32_736),
            new ServiceId(1_024),
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
