using Carina.Domain.Channels;
using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityIncidentTests
{
    private static readonly DateTime Detected = new(2026, 8, 8, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Later = Detected.AddMinutes(5);

    private static readonly Threshold Applied = Threshold.Provisionally(0.0002, observations: 0, Detected);

    [Fact(DisplayName = "the threshold an incident was judged against is kept as it stood then")]
    public void TheThresholdAnIncidentWasJudgedAgainstIsKeptAsItStoodThen()
    {
        QualityIncident incident = Detect();

        Assert.Equal(0.0002, incident.Applied.Current);
        Assert.True(incident.Applied.Provisional);
        Assert.Equal(QualityIncidentState.Detected, incident.State);
        Assert.False(incident.Restated);
    }

    [Fact(DisplayName = "an incident is told about once and then stands until its condition clears")]
    public void AnIncidentIsToldAboutOnceAndThenStandsUntilItsConditionClears()
    {
        QualityIncident incident = Detect();
        incident.Notify(Later);

        Assert.Equal(QualityIncidentState.Notified, incident.State);
        Assert.Equal(Later, incident.NotifiedAt);
        Assert.False(incident.HasSettled);
        Assert.Throws<InvalidOperationException>(() => incident.Notify(Later.AddMinutes(1)));
    }

    [Fact(DisplayName = "an incident only ever stands detected, told about, or resolved")]
    public void AnIncidentOnlyEverStandsDetectedToldAboutOrResolved()
        => Assert.Equal(
            [nameof(QualityIncidentState.Detected), nameof(QualityIncidentState.Notified), nameof(QualityIncidentState.Resolved)],
            Enum.GetNames<QualityIncidentState>());

    [Fact(DisplayName = "a resolved incident stays resolved and cannot be told about again")]
    public void AResolvedIncidentStaysResolvedAndCannotBeToldAboutAgain()
    {
        QualityIncident incident = Detect();
        incident.Notify(Later);
        incident.Resolve(Later.AddMinutes(2));

        Assert.Equal(QualityIncidentState.Resolved, incident.State);
        Assert.True(incident.HasSettled);
        Assert.Throws<InvalidOperationException>(() => incident.Resolve(Later.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => incident.Notify(Later.AddMinutes(3)));
    }

    [Fact]
    public void AnIncidentIsNotResolvedBeforeItWasToldAbout()
    {
        QualityIncident incident = Detect();
        incident.Notify(Later);

        Assert.Throws<ArgumentException>(() => incident.Resolve(Later.AddMinutes(-1)));
    }

    [Fact]
    public void AnIncidentNobodyWasEverToldAboutCanStillBeResolved()
    {
        QualityIncident incident = Detect();
        incident.Resolve(Later);

        Assert.Equal(QualityIncidentState.Resolved, incident.State);
        Assert.Null(incident.NotifiedAt);
    }

    [Fact(DisplayName = "an anomaly another domain owns is kept under that domain's own classification")]
    public void AnAnomalyAnotherDomainOwnsIsKeptUnderThatDomainsOwnClassification()
    {
        QualityIncident restated = QualityIncident.Detect(
            QualityIncidentId.New(),
            Detected,
            QualityThresholdKey.LockRate,
            QualitySubject.Of(QualitySubjectKind.Tuner, "adapter2"),
            0,
            Applied,
            QualityIncidentOwner.Tuner,
            nameof(TuneFailureKind.NoLock));

        Assert.True(restated.Restated);
        Assert.Equal(nameof(TuneFailureKind.NoLock), restated.Classification);
    }

    [Fact(DisplayName = "this domain's own anomaly borrows no other domain's classification")]
    public void ThisDomainsOwnAnomalyBorrowsNoOtherDomainsClassification()
        => Assert.Throws<ArgumentException>(() => QualityIncident.Detect(
            QualityIncidentId.New(),
            Detected,
            QualityThresholdKey.LockRate,
            QualitySubject.Of(QualitySubjectKind.Tuner, "adapter2"),
            0,
            Applied,
            QualityIncidentOwner.Quality,
            nameof(TuneFailureKind.NoLock)));

    [Fact(DisplayName = "an anomaly another domain owns says which classification it kept")]
    public void AnAnomalyAnotherDomainOwnsSaysWhichClassificationItKept()
        => Assert.Throws<ArgumentException>(() => QualityIncident.Detect(
            QualityIncidentId.New(),
            Detected,
            QualityThresholdKey.LockRate,
            QualitySubject.Of(QualitySubjectKind.Tuner, "adapter2"),
            0,
            Applied,
            QualityIncidentOwner.Tuner));

    [Fact]
    public void AnIncidentReadBackFromTheLedgerStandsWhereItsOwnTimesPutIt()
    {
        QualityIncident incident = QualityIncident.Rehydrate(
            QualityIncidentId.New(),
            Detected,
            QualityThresholdKey.PacketsLostWarning,
            QualitySubject.Of(QualitySubjectKind.Recording, Guid.NewGuid().ToString("N")),
            0.004,
            QualityIncidentOwner.Quality,
            null,
            null,
            Applied,
            QualityIncidentState.Notified,
            Later,
            null);

        Assert.Equal(QualityIncidentState.Notified, incident.State);
    }

    [Fact]
    public void AnIncidentReadBackStandingSomewhereItsTimesDenyIsRefused()
        => Assert.Throws<ArgumentException>(() => QualityIncident.Rehydrate(
            QualityIncidentId.New(),
            Detected,
            QualityThresholdKey.PacketsLostWarning,
            QualitySubject.Of(QualitySubjectKind.Recording, Guid.NewGuid().ToString("N")),
            0.004,
            QualityIncidentOwner.Quality,
            null,
            null,
            Applied,
            QualityIncidentState.Resolved,
            Later,
            null));

    [Fact]
    public void AnIncidentsOwnTimesOnlyEverReadForwards()
    {
        QualityIncident incident = Detect();

        Assert.Throws<ArgumentException>(() => incident.Notify(Detected.AddMinutes(-1)));
    }

    private static QualityIncident Detect()
        => QualityIncident.Detect(
            QualityIncidentId.New(),
            Detected,
            QualityThresholdKey.PacketsLostWarning,
            QualitySubject.Of(QualitySubjectKind.Recording, Guid.NewGuid().ToString("N")),
            0.004,
            Applied);

    [Fact(DisplayName = "a supply that went quiet says which of the four supplies it was")]
    public void ASupplyThatWentQuietSaysWhichOfTheFourSuppliesItWas()
        => Assert.Throws<ArgumentException>(() => QualityIncident.Detect(
            QualityIncidentId.New(),
            Detected,
            QualityThresholdKey.SupplySilence,
            QualitySubject.Of(QualitySubjectKind.Tuner, "adapter2"),
            300,
            Applied));

    [Fact(DisplayName = "nothing but a supply going quiet carries one of the four")]
    public void NothingButASupplyGoingQuietCarriesOneOfTheFour()
        => Assert.Throws<ArgumentException>(() => QualityIncident.Detect(
            QualityIncidentId.New(),
            Detected,
            QualityThresholdKey.PacketsLostWarning,
            QualitySubject.Of(QualitySubjectKind.Tuner, "adapter2"),
            0.004,
            Applied,
            silence: SupplySilence.SignalSamples));

    [Fact(DisplayName = "a supply going quiet under a name this domain does not know is refused")]
    public void ASupplyGoingQuietUnderANameThisDomainDoesNotKnowIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityIncident.Detect(
            QualityIncidentId.New(),
            Detected,
            QualityThresholdKey.SupplySilence,
            QualitySubject.Of(QualitySubjectKind.Tuner, "adapter2"),
            300,
            Applied,
            silence: (SupplySilence)9));
}
