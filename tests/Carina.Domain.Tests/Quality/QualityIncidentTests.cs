using Carina.Domain.Channels;
using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityIncidentTests
{
    private static readonly DateTime Detected = new(2026, 8, 8, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Later = Detected.AddMinutes(5);

    private static readonly Threshold Applied = Threshold.Provisionally(0.0002, observations: 0, Detected);

    [Fact(DisplayName = "BR-QV-002: the threshold an incident was judged against is kept as it stood then")]
    public void TheThresholdAnIncidentWasJudgedAgainstIsKeptAsItStoodThen()
    {
        QualityIncident incident = Detect();

        Assert.Equal(0.0002, incident.Applied.Current);
        Assert.True(incident.Applied.Provisional);
        Assert.Equal(QualityIncidentState.Detected, incident.State);
        Assert.False(incident.Restated);
    }

    [Fact(DisplayName = "BR-QS-002: an incident is told about once and then waits to be acknowledged")]
    public void AnIncidentIsToldAboutOnceAndThenWaitsToBeAcknowledged()
    {
        QualityIncident incident = Detect();
        incident.Notify(Later);

        Assert.Equal(QualityIncidentState.Notified, incident.State);
        Assert.Equal(Later, incident.NotifiedAt);
        Assert.Throws<InvalidOperationException>(() => incident.Notify(Later.AddMinutes(1)));
    }

    [Fact(DisplayName = "BR-QS-002: acknowledging says who did it and when, and throws nothing away")]
    public void AcknowledgingSaysWhoDidItAndWhenAndThrowsNothingAway()
    {
        QualityIncident incident = Detect();
        incident.Notify(Later);
        incident.Acknowledge(Later.AddMinutes(1), "operator");

        Assert.Equal(QualityIncidentState.Acknowledged, incident.State);
        Assert.Equal("operator", incident.AcknowledgedBy);
        Assert.Equal(Later.AddMinutes(1), incident.AcknowledgedAt);
        Assert.Equal(Detected, incident.DetectedAt);
    }

    [Fact]
    public void NobodyAcknowledgesAnIncidentTheyWereNeverToldAbout()
    {
        QualityIncident incident = Detect();

        Assert.Throws<InvalidOperationException>(() => incident.Acknowledge(Later, "operator"));
    }

    [Fact(DisplayName = "BR-QS-002: acknowledging holds for one occurrence and cannot be made to hold for the next")]
    public void AcknowledgingHoldsForOneOccurrenceAndCannotBeMadeToHoldForTheNext()
    {
        QualityIncident incident = Detect();
        incident.Notify(Later);
        incident.Acknowledge(Later.AddMinutes(1), "operator");
        incident.Resolve(Later.AddMinutes(2));

        Assert.Equal(QualityIncidentState.Resolved, incident.State);
        Assert.True(incident.HasSettled);
        Assert.Throws<InvalidOperationException>(() => incident.Resolve(Later.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => incident.Notify(Later.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => incident.Acknowledge(Later.AddMinutes(3), "operator"));
    }

    [Fact]
    public void AnIncidentNobodyWasEverToldAboutCanStillBeResolved()
    {
        QualityIncident incident = Detect();
        incident.Resolve(Later);

        Assert.Equal(QualityIncidentState.Resolved, incident.State);
        Assert.Null(incident.NotifiedAt);
    }

    [Fact(DisplayName = "BR-QD-002: an anomaly another domain owns is kept under that domain's own classification")]
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

    [Fact(DisplayName = "BR-QD-002: this domain's own anomaly borrows no other domain's classification")]
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

    [Fact(DisplayName = "BR-QD-008: an anomaly another domain owns says which classification it kept")]
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
            Applied,
            QualityIncidentState.Acknowledged,
            Later,
            Later.AddMinutes(1),
            "operator",
            null);

        Assert.Equal(QualityIncidentState.Acknowledged, incident.State);
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
            Applied,
            QualityIncidentState.Resolved,
            Later,
            null,
            null,
            null));

    [Fact]
    public void AnIncidentThatSaysItWasAcknowledgedSaysWhoAcknowledgedIt()
        => Assert.Throws<ArgumentException>(() => QualityIncident.Rehydrate(
            QualityIncidentId.New(),
            Detected,
            QualityThresholdKey.PacketsLostWarning,
            QualitySubject.Of(QualitySubjectKind.Recording, Guid.NewGuid().ToString("N")),
            0.004,
            QualityIncidentOwner.Quality,
            null,
            Applied,
            QualityIncidentState.Acknowledged,
            Later,
            Later.AddMinutes(1),
            null,
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
}
