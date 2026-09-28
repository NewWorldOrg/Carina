using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Quality;

public sealed class TunerTroubleWatchTests
{
    private static readonly DateTime Noon = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TunerTrouble Adapter0 = new(new TunerDeviceId("adapter0"), TunerTroubleKind.NoLock);

    private static readonly TunerTrouble Adapter2 = new(new TunerDeviceId("adapter2"), TunerTroubleKind.NoLock);

    private static readonly TunerTrouble Adapter0Failing = new(new TunerDeviceId("adapter0"), TunerTroubleKind.TuneFailing);

    private static readonly TunerTrouble Adapter2Failing = new(new TunerDeviceId("adapter2"), TunerTroubleKind.TuneFailing);

    private static readonly Threshold Applied = QualityThresholdShapes.AsShipped(QualityThresholdKey.LockRate, Noon);

    [Fact(DisplayName = "BR-QS-002: a tuner that cannot lock and has nothing standing for it is opened")]
    public void ATunerThatCannotLockAndHasNothingStandingForItIsOpened()
    {
        TunerTroubleWatchPlan plan = TunerTroubleWatch.Plan([Adapter0], []);

        Assert.Equal([Adapter0], plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QD-002: a tuner that cannot lock is restated as the tuner's own anomaly, under the tuner's own classification")]
    public void ATunerThatCannotLockIsRestatedAsTheTunersOwnAnomaly()
    {
        QualityIncidentId id = QualityIncidentId.New();

        QualityIncident restated = TunerTroubleWatch.Restate(id, Adapter0, Noon, Applied);

        Assert.Equal(id, restated.Id);
        Assert.Equal(Noon, restated.DetectedAt);
        Assert.Equal(QualityThresholdKey.LockRate, restated.Breached);
        Assert.Equal(QualitySubject.Of(QualitySubjectKind.Tuner, "adapter0"), restated.Subject);
        Assert.Equal(0d, restated.Observed);
        Assert.Equal(QualityIncidentOwner.Tuner, restated.Owner);
        Assert.True(restated.Restated);
        Assert.Equal(nameof(TunerTroubleKind.NoLock), restated.Classification);
        Assert.Null(restated.Silence);
        Assert.Equal(Applied, restated.Applied);
        Assert.Equal(QualityIncidentState.Detected, restated.State);
    }

    [Fact(DisplayName = "BR-QS-002: a tuner that still cannot lock is not opened again while its incident stands")]
    public void ATunerThatStillCannotLockIsNotOpenedAgainWhileItsIncidentStands()
    {
        QualityIncident standing = TunerTroubleWatch.Restate(QualityIncidentId.New(), Adapter0, Noon, Applied);

        standing.Notify(Noon);

        TunerTroubleWatchPlan plan = TunerTroubleWatch.Plan([Adapter0], [standing]);

        Assert.Empty(plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QS-002: a tuner that is no longer said to be unable to lock has its incident resolved")]
    public void ATunerThatIsNoLongerSaidToBeUnableToLockHasItsIncidentResolved()
    {
        QualityIncident standing = TunerTroubleWatch.Restate(QualityIncidentId.New(), Adapter0, Noon, Applied);

        TunerTroubleWatchPlan plan = TunerTroubleWatch.Plan([], [standing]);

        Assert.Empty(plan.ToOpen);
        Assert.Equal([standing], plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QS-002: a tuner that cannot lock again after its incident was resolved opens a new one")]
    public void ATunerThatCannotLockAgainAfterItsIncidentWasResolvedOpensANewOne()
    {
        QualityIncident settled = TunerTroubleWatch.Restate(QualityIncidentId.New(), Adapter0, Noon, Applied);

        settled.Resolve(Noon.AddHours(1));

        TunerTroubleWatchPlan plan = TunerTroubleWatch.Plan([Adapter0], [settled]);

        Assert.Equal([Adapter0], plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QS-002: two tuners that cannot lock stand as one incident each, however many passes see them")]
    public void TwoTunersThatCannotLockStandAsOneIncidentEachHoweverManyPassesSeeThem()
    {
        List<QualityIncident> held = [];

        for (int pass = 0; pass < 100; pass++)
        {
            TunerTroubleWatchPlan plan = TunerTroubleWatch.Plan([Adapter0, Adapter2], held);

            held.AddRange(plan.ToOpen.Select(fault => TunerTroubleWatch.Restate(QualityIncidentId.New(), fault, Noon, Applied)));

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

        TunerTroubleWatchPlan plan = TunerTroubleWatch.Plan([], [quiet, measured, noData]);

        Assert.Empty(plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QD-017: only the tuners an unsettled incident says cannot lock are named as unable to lock")]
    public void OnlyTheTunersAnUnsettledIncidentSaysCannotLockAreNamedAsUnableToLock()
    {
        QualityIncident standing = TunerTroubleWatch.Restate(QualityIncidentId.New(), Adapter0, Noon, Applied);
        QualityIncident settled = TunerTroubleWatch.Restate(QualityIncidentId.New(), Adapter2, Noon, Applied);
        QualityIncident noData = QualityIncident.Detect(
            QualityIncidentId.New(),
            Noon,
            QualityThresholdKey.LockRate,
            QualitySubject.Of(QualitySubjectKind.Tuner, "adapter3"),
            0,
            Applied,
            QualityIncidentOwner.Tuner,
            nameof(TuneFailureKind.NoData));

        settled.Resolve(Noon.AddHours(1));

        Assert.Equal(
            new Dictionary<string, TunerTroubleKind> { ["adapter0"] = TunerTroubleKind.NoLock },
            TunerTroubleWatch.Troubled([standing, settled, noData]));
    }

    [Fact(DisplayName = "BR-QD-018: a tuner that is failing to tune is restated under that kind of trouble")]
    public void ATunerThatIsFailingToTuneIsRestatedUnderThatKindOfTrouble()
    {
        TunerTroubleWatchPlan plan = TunerTroubleWatch.Plan([Adapter0Failing, Adapter2Failing], []);

        Assert.Equal([Adapter0Failing, Adapter2Failing], plan.ToOpen);

        QualityIncident restated = TunerTroubleWatch.Restate(QualityIncidentId.New(), Adapter0Failing, Noon, Applied);

        Assert.Equal("TuneFailing", restated.Classification);
        Assert.Equal(QualityIncidentOwner.Tuner, restated.Owner);
        Assert.True(restated.Restated);
    }

    [Fact(DisplayName = "BR-QD-018: a tuner whose trouble turns from failing to tune into not locking closes one record and opens another")]
    public void ATunerWhoseTroubleChangesKindClosesOneRecordAndOpensAnother()
    {
        QualityIncident failing = TunerTroubleWatch.Restate(QualityIncidentId.New(), Adapter0Failing, Noon, Applied);

        TunerTroubleWatchPlan plan = TunerTroubleWatch.Plan([Adapter0], [failing]);

        Assert.Equal([Adapter0], plan.ToOpen);
        Assert.Equal([failing], plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QD-018: a driver started again forgets every trouble, and each record of one is resolved")]
    public void ADriverStartedAgainForgetsEveryTroubleAndEachRecordIsResolved()
    {
        QualityIncident first = TunerTroubleWatch.Restate(QualityIncidentId.New(), Adapter0Failing, Noon, Applied);
        QualityIncident second = TunerTroubleWatch.Restate(QualityIncidentId.New(), Adapter2, Noon, Applied);

        TunerTroubleWatchPlan plan = TunerTroubleWatch.Plan([], [first, second]);

        Assert.Empty(plan.ToOpen);
        Assert.Equal([first, second], plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QD-017: a tuner said to be in two kinds of trouble at once is read by the worse of them")]
    public void ATunerSaidToBeInTwoKindsOfTroubleIsReadByTheWorse()
    {
        QualityIncident failing = TunerTroubleWatch.Restate(QualityIncidentId.New(), Adapter0Failing, Noon, Applied);
        QualityIncident failed = TunerTroubleWatch.Restate(
            QualityIncidentId.New(),
            new TunerTrouble(new TunerDeviceId("adapter0"), TunerTroubleKind.DeviceFailed),
            Noon,
            Applied);

        Assert.Equal(TunerTroubleKind.DeviceFailed, TunerTroubleWatch.Troubled([failing, failed])["adapter0"]);
        Assert.Equal(TunerTroubleKind.DeviceFailed, TunerTroubleWatch.Troubled([failed, failing])["adapter0"]);
    }
}
