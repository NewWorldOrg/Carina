using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Events;
using Carina.Domain.Quality;
using Carina.Infrastructure.Quality;

namespace Carina.Infrastructure.Tests.Quality;

public sealed class LockWatchRoundTests
{
    private const string FirstSatellite = "adapter0";

    private const string SecondSatellite = "adapter2";

    private static readonly DateTime Noon = SupplyWatchHarness.Noon;

    private static readonly TimeSpan BetweenPasses = TimeSpan.FromMinutes(5);

    private static readonly CancellationToken Cancel = CancellationToken.None;

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
            Assert.Equal(TunerFaults.CannotLockClassification, incident.Classification);
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
}
