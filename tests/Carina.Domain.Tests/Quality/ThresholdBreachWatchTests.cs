using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Quality;

public sealed class ThresholdBreachWatchTests
{
    private static readonly DateTime Noon = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<QualityThresholdStanding> Levels = QualityThresholdStanding.Over([], Noon);

    private static readonly QualityBands Bands = QualityThresholdStanding.Bands(Levels);

    private static readonly QualitySubject TheChannel = QualitySubject.Of(QualitySubjectKind.Channel, "32736-1024");

    private static readonly QualitySubject TheTuner = QualitySubject.Of(QualitySubjectKind.Tuner, "adapter0");

    [Fact(DisplayName = "BR-QD-020: a recording that lost more than the warning level names its channel, the level it passed and what was read")]
    public void ARecordingThatLostMoreThanTheWarningLevelNamesItsChannel()
    {
        ThresholdBreach breach = Assert.Single(ThresholdBreachWatch.Recorded(
            [QualityFactory.Row(dropped: 500, total: 1_000_000)],
            Bands));

        Assert.Equal(QualityThresholdKey.PacketsLostWarning, breach.Breached);
        Assert.Equal(TheChannel, breach.Subject);
        Assert.Equal(0.0005, breach.Observed);
        Assert.Equal(Level(QualityThresholdKey.PacketsLostWarning), breach.Applied);
    }

    [Fact(DisplayName = "BR-QD-020: several recordings of one channel beyond a level are one breach, read at the worst of them and at the heaviest level passed")]
    public void SeveralRecordingsOfOneChannelBeyondALevelAreOneBreach()
    {
        ThresholdBreach breach = Assert.Single(ThresholdBreachWatch.Recorded(
            [
                QualityFactory.Row(dropped: 500, total: 1_000_000),
                QualityFactory.Row(dropped: 5_000, total: 1_000_000),
                QualityFactory.Row(dropped: 0, total: 1_000_000),
            ],
            Bands));

        Assert.Equal(QualityThresholdKey.PacketsLostUnwatchable, breach.Breached);
        Assert.Equal(0.005, breach.Observed);
        Assert.Equal(Level(QualityThresholdKey.PacketsLostUnwatchable), breach.Applied);
    }

    [Fact(DisplayName = "BR-QD-020: each channel and each measure is a breach of its own")]
    public void EachChannelAndEachMeasureIsABreachOfItsOwn()
    {
        IReadOnlyList<ThresholdBreach> breaches = ThresholdBreachWatch.Recorded(
            [
                QualityFactory.Row(dropped: 500, total: 1_000_000, scrambled: 900_000, overflows: 3),
                QualityFactory.Row(dropped: 500, total: 1_000_000, service: 1_032),
            ],
            Bands);

        Assert.Equal(
            [
                (QualityThresholdKey.PacketsLostWarning, "32736-1024"),
                (QualityThresholdKey.PacketsLostWarning, "32736-1032"),
                (QualityThresholdKey.PacketsLeftScrambledUnwatchable, "32736-1024"),
                (QualityThresholdKey.Overflows, "32736-1024"),
            ],
            breaches.Select(breach => (breach.Breached, breach.Subject.Key)));
        Assert.All(breaches, breach => Assert.Equal(QualitySubjectKind.Channel, breach.Subject.Kind));
    }

    [Fact(DisplayName = "BR-QD-001: a recording nothing measured is no breach, and neither is one within every level")]
    public void ARecordingNothingMeasuredIsNoBreach()
    {
        Assert.Empty(ThresholdBreachWatch.Recorded(
            [
                QualityFactory.Row(dropped: null, total: null, scrambled: null),
                QualityFactory.Row(dropped: 0, total: 1_000_000),
            ],
            Bands));
    }

    [Fact(DisplayName = "BR-QD-020: a tuner whose signal went beyond a level names the tuner, the level and what was read")]
    public void ATunerWhoseSignalWentBeyondALevelNamesTheTuner()
    {
        IReadOnlyList<ThresholdBreach> breaches = ThresholdBreachWatch.Received(
            [Figures("adapter0", samples: 100, locked: 90, carrierToNoise: 9_000, bitErrors: 0.01)],
            Levels,
            new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(
            [
                (QualityThresholdKey.LockRate, 0.9),
                (QualityThresholdKey.CarrierToNoiseFloor, 9_000d),
                (QualityThresholdKey.BitErrorRateCeiling, 0.01),
            ],
            breaches.Select(breach => (breach.Breached, breach.Observed)));
        Assert.All(breaches, breach => Assert.Equal(TheTuner, breach.Subject));
        Assert.All(breaches, breach => Assert.Equal(Level(breach.Breached), breach.Applied));
    }

    [Fact(DisplayName = "BR-QD-020: a tuner whose signal stayed within every level, or that nothing was taken from, is no breach")]
    public void ATunerWithinEveryLevelOrThatNothingWasTakenFromIsNoBreach()
    {
        Assert.Empty(ThresholdBreachWatch.Received(
            [
                Figures("adapter0", samples: 100, locked: 100, carrierToNoise: 30_000, bitErrors: 0),
                new SignalFigures(new TunerDeviceId("adapter1"), 10, 0, 0, 10, null, null, [], null),
                new SignalFigures(new TunerDeviceId("adapter2"), 10, 10, 0, 0, null, null, [], null),
            ],
            Levels,
            new HashSet<string>(StringComparer.Ordinal)));
    }

    [Fact(DisplayName = "BR-QD-020: a tuner the driver says cannot lock is not also a lock rate breach, and its other readings still are")]
    public void ATunerTheDriverSaysCannotLockIsNotAlsoALockRateBreach()
    {
        IReadOnlyList<ThresholdBreach> breaches = ThresholdBreachWatch.Received(
            [Figures("adapter0", samples: 100, locked: 0, carrierToNoise: 9_000, bitErrors: 0)],
            Levels,
            new HashSet<string>(StringComparer.Ordinal) { "adapter0" });

        Assert.Equal([QualityThresholdKey.CarrierToNoiseFloor], breaches.Select(breach => breach.Breached));
    }

    [Fact(DisplayName = "BR-QS-002: a breach nothing stands for is opened, as this domain's own and under no classification")]
    public void ABreachNothingStandsForIsOpened()
    {
        ThresholdBreach breach = Breach(QualityThresholdKey.PacketsLostWarning, TheChannel, 0.0005);

        ThresholdBreachWatchPlan plan = ThresholdBreachWatch.Plan([breach], []);

        Assert.Equal([breach], plan.ToOpen);
        Assert.Empty(plan.ToResolve);

        QualityIncidentId id = QualityIncidentId.New();
        QualityIncident opened = ThresholdBreachWatch.Open(id, breach, Noon);

        Assert.Equal(id, opened.Id);
        Assert.Equal(Noon, opened.DetectedAt);
        Assert.Equal(QualityThresholdKey.PacketsLostWarning, opened.Breached);
        Assert.Equal(TheChannel, opened.Subject);
        Assert.Equal(0.0005, opened.Observed);
        Assert.Equal(breach.Applied, opened.Applied);
        Assert.Equal(QualityIncidentOwner.Quality, opened.Owner);
        Assert.False(opened.Restated);
        Assert.Null(opened.Classification);
        Assert.Null(opened.Silence);
        Assert.Equal(QualityIncidentState.Detected, opened.State);
    }

    [Fact(DisplayName = "BR-QD-020: a breach that goes on is neither opened again nor resolved, however much worse it reads")]
    public void ABreachThatGoesOnIsNeitherOpenedAgainNorResolved()
    {
        QualityIncident standing = Standing(QualityThresholdKey.PacketsLostWarning, TheChannel, 0.0005);

        ThresholdBreachWatchPlan plan = ThresholdBreachWatch.Plan(
            [Breach(QualityThresholdKey.PacketsLostWarning, TheChannel, 0.0009)],
            [standing]);

        Assert.Empty(plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QS-002: a breach the reading no longer names is resolved")]
    public void ABreachTheReadingNoLongerNamesIsResolved()
    {
        QualityIncident standing = Standing(QualityThresholdKey.PacketsLostWarning, TheChannel, 0.0005);

        ThresholdBreachWatchPlan plan = ThresholdBreachWatch.Plan([], [standing]);

        Assert.Empty(plan.ToOpen);
        Assert.Equal([standing], plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QS-002: a breach that comes back after it was resolved is a new one")]
    public void ABreachThatComesBackAfterItWasResolvedIsANewOne()
    {
        QualityIncident settled = Standing(QualityThresholdKey.PacketsLostWarning, TheChannel, 0.0005);

        settled.Resolve(Noon);

        ThresholdBreach back = Breach(QualityThresholdKey.PacketsLostWarning, TheChannel, 0.0005);
        ThresholdBreachWatchPlan plan = ThresholdBreachWatch.Plan([back], [settled]);

        Assert.Equal([back], plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QD-020: a channel that got worse than its warning closes the warning and opens the heavier level")]
    public void AChannelThatGotWorseThanItsWarningClosesTheWarningAndOpensTheHeavierLevel()
    {
        QualityIncident warning = Standing(QualityThresholdKey.PacketsLostWarning, TheChannel, 0.0005);
        ThresholdBreach heavier = Breach(QualityThresholdKey.PacketsLostUnwatchable, TheChannel, 0.005);

        ThresholdBreachWatchPlan plan = ThresholdBreachWatch.Plan([heavier], [warning]);

        Assert.Equal([heavier], plan.ToOpen);
        Assert.Equal([warning], plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QD-020: the same level passed on another channel or another tuner is a breach of its own")]
    public void TheSameLevelPassedOnAnotherSubjectIsABreachOfItsOwn()
    {
        QualityIncident standing = Standing(QualityThresholdKey.PacketsLostWarning, TheChannel, 0.0005);
        ThresholdBreach elsewhere = Breach(
            QualityThresholdKey.PacketsLostWarning,
            QualitySubject.Of(QualitySubjectKind.Channel, "32736-1032"),
            0.0005);

        ThresholdBreachWatchPlan plan = ThresholdBreachWatch.Plan(
            [Breach(QualityThresholdKey.PacketsLostWarning, TheChannel, 0.0005), elsewhere],
            [standing]);

        Assert.Equal([elsewhere], plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QD-002: a supply that went quiet and a tuner's own trouble restated here are left to the watches that opened them")]
    public void WhatTheOtherWatchesOpenedIsLeftToThem()
    {
        QualityIncident quiet = QualityIncident.Detect(
            QualityIncidentId.New(),
            Noon,
            QualityThresholdKey.SupplySilence,
            TheTuner,
            600,
            Level(QualityThresholdKey.SupplySilence),
            silence: SupplySilence.SignalSamples);
        QualityIncident restated = TunerTroubleWatch.Restate(
            QualityIncidentId.New(),
            new TunerTrouble(new TunerDeviceId("adapter0"), TunerTroubleKind.NoLock),
            Noon,
            Level(QualityThresholdKey.LockRate));

        ThresholdBreachWatchPlan plan = ThresholdBreachWatch.Plan([], [quiet, restated]);

        Assert.Empty(plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }

    [Fact(DisplayName = "BR-QD-020: a lock rate breach of this domain's own stands apart from the tuner's trouble restated under the same level")]
    public void ALockRateBreachOfThisDomainsOwnStandsApartFromTheRestatedOne()
    {
        QualityIncident restated = TunerTroubleWatch.Restate(
            QualityIncidentId.New(),
            new TunerTrouble(new TunerDeviceId("adapter0"), TunerTroubleKind.Degraded),
            Noon,
            Level(QualityThresholdKey.LockRate));
        ThresholdBreach breach = Breach(QualityThresholdKey.LockRate, TheTuner, 0.5);

        ThresholdBreachWatchPlan plan = ThresholdBreachWatch.Plan([breach], [restated]);

        Assert.Equal([breach], plan.ToOpen);
        Assert.Empty(plan.ToResolve);
    }

    private static Threshold Level(QualityThresholdKey key) => Levels.First(level => level.Key == key).Setting;

    private static ThresholdBreach Breach(QualityThresholdKey key, QualitySubject subject, double observed)
        => new(key, subject, observed, Level(key));

    private static QualityIncident Standing(QualityThresholdKey key, QualitySubject subject, double observed)
        => ThresholdBreachWatch.Open(QualityIncidentId.New(), Breach(key, subject, observed), Noon);

    private static SignalFigures Figures(string tuner, long samples, long locked, int carrierToNoise, double bitErrors)
        => new(new TunerDeviceId(tuner), samples, locked, 0, 0, carrierToNoise, bitErrors, [], Noon);
}
