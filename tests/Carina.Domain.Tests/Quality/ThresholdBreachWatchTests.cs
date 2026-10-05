using Carina.Contracts;
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
                new SignalFigures(new TunerDeviceId("adapter1"), 10, 0, 0, 10, null, null, null, null, [], null),
                new SignalFigures(new TunerDeviceId("adapter2"), 10, 10, 0, 0, null, null, null, null, [], null),
            ],
            Levels,
            new HashSet<string>(StringComparer.Ordinal)));
    }

    [Fact(DisplayName = "BR-QD-021: a tuner whose worst moment went beyond a level while it usually read within it is no breach")]
    public void ATunerWhoseWorstMomentWentBeyondALevelWhileItUsuallyReadWithinItIsNoBreach()
    {
        Assert.Empty(ThresholdBreachWatch.Received(
            [
                Figures(
                    "adapter0",
                    samples: 100,
                    locked: 100,
                    carrierToNoise: 30_000,
                    bitErrors: 0,
                    carrierToNoiseLowest: 6_000,
                    bitErrorsHighest: 0.02),
            ],
            Levels,
            new HashSet<string>(StringComparer.Ordinal)));
    }

    [Fact(DisplayName = "BR-QD-021: a breach of a signal level keeps what the tuner usually read, not the worst moment of it")]
    public void ABreachOfASignalLevelKeepsWhatTheTunerUsuallyRead()
    {
        IReadOnlyList<ThresholdBreach> breaches = ThresholdBreachWatch.Received(
            [
                Figures(
                    "adapter0",
                    samples: 100,
                    locked: 100,
                    carrierToNoise: 9_000,
                    bitErrors: 0.01,
                    carrierToNoiseLowest: 5_000,
                    bitErrorsHighest: 0.02),
            ],
            Levels,
            new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(
            [
                (QualityThresholdKey.CarrierToNoiseFloor, 9_000d),
                (QualityThresholdKey.BitErrorRateCeiling, 0.01),
            ],
            breaches.Select(breach => (breach.Breached, breach.Observed)));
    }

    [Fact(DisplayName = "BR-QD-021: one bad sample among sound ones opens nothing, a tuner that goes on reading badly is opened, and it is closed once it reads well again")]
    public void OneBadSampleOpensNothingGoingOnBadlyOpensAndReadingWellAgainCloses()
    {
        HashSet<string> none = new(StringComparer.Ordinal);
        List<QualitySignalSample> taken = [.. Enumerable.Range(0, 5).Select(turn => Sound(Noon.AddSeconds(10 * turn)))];

        taken.Add(Bad(Noon.AddMinutes(1)));

        Assert.Empty(ThresholdBreachWatch.Received(QualitySignalSurvey.Figures([], taken), Levels, none));

        taken.AddRange(Enumerable.Range(0, 5).Select(turn => Bad(Noon.AddMinutes(2).AddSeconds(10 * turn))));

        IReadOnlyList<ThresholdBreach> breaches =
            ThresholdBreachWatch.Received(QualitySignalSurvey.Figures([], taken), Levels, none);
        ThresholdBreachWatchPlan opening = ThresholdBreachWatch.Plan(breaches, []);

        Assert.Equal(
            [
                (QualityThresholdKey.CarrierToNoiseFloor, 6_000d),
                (QualityThresholdKey.BitErrorRateCeiling, 0.02),
            ],
            opening.ToOpen.Select(breach => (breach.Breached, breach.Observed)));

        IReadOnlyList<QualityIncident> standing =
            [.. opening.ToOpen.Select(breach => ThresholdBreachWatch.Open(QualityIncidentId.New(), breach, Noon))];

        taken.AddRange(Enumerable.Range(0, 2).Select(turn => Sound(Noon.AddMinutes(3).AddSeconds(10 * turn))));

        ThresholdBreachWatchPlan closing = ThresholdBreachWatch.Plan(
            ThresholdBreachWatch.Received(QualitySignalSurvey.Figures([], taken), Levels, none),
            standing);

        Assert.Empty(closing.ToOpen);
        Assert.Equal(standing, closing.ToResolve);
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

    [Fact(DisplayName = "BR-QD-024: a channel that usually read beyond a signal level on a tuner names the channel and the tuner, and one within it is no breach")]
    public void AChannelThatUsuallyReadBeyondASignalLevelOnATunerNamesTheChannelAndTheTuner()
    {
        IReadOnlyList<ThresholdBreach> breaches = ThresholdBreachWatch.ReceivedByChannel(
            [
                Reception(1_024, "adapter0", carrierToNoise: 30_000, bitErrors: 0),
                Reception(2_048, "adapter0", carrierToNoise: 9_000, bitErrors: 0.01),
            ],
            Levels,
            []);

        Assert.Equal(
            [
                (QualityThresholdKey.CarrierToNoiseFloor, "32736-2048@adapter0", 9_000d),
                (QualityThresholdKey.BitErrorRateCeiling, "32736-2048@adapter0", 0.01),
            ],
            breaches.Select(breach => (breach.Breached, breach.Subject.Key, breach.Observed)));
        Assert.All(breaches, breach => Assert.Equal(QualitySubjectKind.Reception, breach.Subject.Kind));
        Assert.All(breaches, breach => Assert.Equal(Level(breach.Breached), breach.Applied));
    }

    [Fact(DisplayName = "BR-QD-024: a channel on a tuner already named for the same level is not named again, and is for another level")]
    public void AChannelOnATunerAlreadyNamedForTheSameLevelIsNotNamedAgain()
    {
        IReadOnlyList<ThresholdBreach> breaches = ThresholdBreachWatch.ReceivedByChannel(
            [
                Reception(2_048, "adapter0", carrierToNoise: 9_000, bitErrors: 0.01),
                Reception(2_048, "adapter1", carrierToNoise: 9_000, bitErrors: 0),
            ],
            Levels,
            [Breach(QualityThresholdKey.CarrierToNoiseFloor, TheTuner, 9_000)]);

        Assert.Equal(
            [
                (QualityThresholdKey.CarrierToNoiseFloor, "32736-2048@adapter1"),
                (QualityThresholdKey.BitErrorRateCeiling, "32736-2048@adapter0"),
            ],
            breaches.Select(breach => (breach.Breached, breach.Subject.Key)));
    }

    [Fact(DisplayName = "BR-QD-024: a channel is never named for its lock rate")]
    public void AChannelIsNeverNamedForItsLockRate()
        => Assert.Empty(ThresholdBreachWatch.ReceivedByChannel(
            [
                new ReceptionFigures(
                    new NetworkId(32_736),
                    new ServiceId(2_048),
                    Figures("adapter0", samples: 100, locked: 10, carrierToNoise: 30_000, bitErrors: 0)),
            ],
            Levels,
            []));

    private static Threshold Level(QualityThresholdKey key) => Levels.First(level => level.Key == key).Setting;

    private static ThresholdBreach Breach(QualityThresholdKey key, QualitySubject subject, double observed)
        => new(key, subject, observed, Level(key));

    private static QualityIncident Standing(QualityThresholdKey key, QualitySubject subject, double observed)
        => ThresholdBreachWatch.Open(QualityIncidentId.New(), Breach(key, subject, observed), Noon);

    private static QualitySignalSample Sound(DateTime at) => Sample(at, 30_000, 0);

    private static QualitySignalSample Bad(DateTime at) => Sample(at, 6_000, 20_000);

    private static QualitySignalSample Sample(DateTime at, int carrierToNoise, long errorBits)
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse("live-1"),
            at,
            SessionPurpose.Live,
            new TunerDeviceId("adapter0"),
            new NetworkId(32_736),
            new ServiceId(1_024),
            SignalSample.WithLock(at, carrierToNoise, at, [new LayerBitErrorCounts(1, errorBits, 1_000_000)], at));

    private static SignalFigures Figures(
        string tuner,
        long samples,
        long locked,
        int carrierToNoise,
        double bitErrors,
        int? carrierToNoiseLowest = null,
        double? bitErrorsHighest = null)
        => new(
            new TunerDeviceId(tuner),
            samples,
            locked,
            0,
            0,
            carrierToNoiseLowest ?? carrierToNoise,
            carrierToNoise,
            bitErrorsHighest ?? bitErrors,
            bitErrors,
            [],
            Noon);

    private static ReceptionFigures Reception(int service, string tuner, int carrierToNoise, double bitErrors)
        => new(
            new NetworkId(32_736),
            new ServiceId(service),
            Figures(tuner, samples: 100, locked: 100, carrierToNoise, bitErrors));
}
