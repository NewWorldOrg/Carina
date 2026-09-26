using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Quality;

public sealed class LockWatchTests
{
    private static readonly DateTime Noon = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TunerFault Adapter0 = new(new TunerDeviceId("adapter0"), TuneFailureKind.NoLock);

    private static readonly TunerFault Adapter2 = new(new TunerDeviceId("adapter2"), TuneFailureKind.NoLock);

    private static readonly Threshold Applied = QualityThresholdShapes.AsShipped(QualityThresholdKey.LockRate, Noon);

    [Fact(DisplayName = "BR-QS-002: a tuner that cannot lock and has nothing standing for it is opened")]
    public void ATunerThatCannotLockAndHasNothingStandingForItIsOpened()
    {
        LockWatchPlan plan = LockWatch.Plan([Adapter0], []);

        Assert.Equal([Adapter0], plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QD-002: a tuner that cannot lock is restated as the tuner's own anomaly, under the tuner's own classification")]
    public void ATunerThatCannotLockIsRestatedAsTheTunersOwnAnomaly()
    {
        QualityIncidentId id = QualityIncidentId.New();

        QualityIncident restated = LockWatch.Restate(id, Adapter0, Noon, Applied);

        Assert.Equal(id, restated.Id);
        Assert.Equal(Noon, restated.DetectedAt);
        Assert.Equal(QualityThresholdKey.LockRate, restated.Breached);
        Assert.Equal(QualitySubject.Of(QualitySubjectKind.Tuner, "adapter0"), restated.Subject);
        Assert.Equal(0d, restated.Observed);
        Assert.Equal(QualityIncidentOwner.Tuner, restated.Owner);
        Assert.True(restated.Restated);
        Assert.Equal(nameof(TuneFailureKind.NoLock), restated.Classification);
        Assert.Null(restated.Silence);
        Assert.Equal(Applied, restated.Applied);
        Assert.Equal(QualityIncidentState.Detected, restated.State);
    }

    [Fact(DisplayName = "BR-QS-002: a tuner that still cannot lock is not opened again while its incident stands")]
    public void ATunerThatStillCannotLockIsNotOpenedAgainWhileItsIncidentStands()
    {
        QualityIncident standing = LockWatch.Restate(QualityIncidentId.New(), Adapter0, Noon, Applied);

        standing.Notify(Noon);

        LockWatchPlan plan = LockWatch.Plan([Adapter0], [standing]);

        Assert.Empty(plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QS-002: a tuner that is no longer said to be unable to lock has its incident resolved")]
    public void ATunerThatIsNoLongerSaidToBeUnableToLockHasItsIncidentResolved()
    {
        QualityIncident standing = LockWatch.Restate(QualityIncidentId.New(), Adapter0, Noon, Applied);

        LockWatchPlan plan = LockWatch.Plan([], [standing]);

        Assert.Empty(plan.ToOpen);
        Assert.Equal([standing], plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QS-002: a tuner that cannot lock again after its incident was resolved opens a new one")]
    public void ATunerThatCannotLockAgainAfterItsIncidentWasResolvedOpensANewOne()
    {
        QualityIncident settled = LockWatch.Restate(QualityIncidentId.New(), Adapter0, Noon, Applied);

        settled.Resolve(Noon.AddHours(1));

        LockWatchPlan plan = LockWatch.Plan([Adapter0], [settled]);

        Assert.Equal([Adapter0], plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QS-002: two tuners that cannot lock stand as one incident each, however many passes see them")]
    public void TwoTunersThatCannotLockStandAsOneIncidentEachHoweverManyPassesSeeThem()
    {
        List<QualityIncident> held = [];

        for (int pass = 0; pass < 100; pass++)
        {
            LockWatchPlan plan = LockWatch.Plan([Adapter0, Adapter2], held);

            held.AddRange(plan.ToOpen.Select(fault => LockWatch.Restate(QualityIncidentId.New(), fault, Noon, Applied)));

            Assert.Empty(plan.ToResolve);
        }

        Assert.Equal(
            [QualitySubject.Of(QualitySubjectKind.Tuner, "adapter0"), QualitySubject.Of(QualitySubjectKind.Tuner, "adapter2")],
            held.Select(incident => incident.Subject));
    }

    [Fact(DisplayName = "BR-QD-002: an incident this watch did not open is left where it stands")]
    public void AnIncidentThisWatchDidNotOpenIsLeftWhereItStands()
    {
        QualitySubject adapter0 = QualitySubject.Of(QualitySubjectKind.Tuner, "adapter0");
        QualityIncident quiet = QualityIncident.Detect(
            QualityIncidentId.New(),
            Noon,
            QualityThresholdKey.SupplySilence,
            adapter0,
            600,
            QualityThresholdShapes.AsShipped(QualityThresholdKey.SupplySilence, Noon),
            silence: SupplySilence.SignalSamples);
        QualityIncident measured = QualityIncident.Detect(
            QualityIncidentId.New(),
            Noon,
            QualityThresholdKey.LockRate,
            adapter0,
            0.4,
            Applied);
        QualityIncident noData = QualityIncident.Detect(
            QualityIncidentId.New(),
            Noon,
            QualityThresholdKey.LockRate,
            adapter0,
            0,
            Applied,
            QualityIncidentOwner.Tuner,
            nameof(TuneFailureKind.NoData));

        LockWatchPlan plan = LockWatch.Plan([], [quiet, measured, noData]);

        Assert.Empty(plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }
}
