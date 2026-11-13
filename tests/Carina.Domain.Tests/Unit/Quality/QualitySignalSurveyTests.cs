using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

using Puppis.Contracts;

namespace Carina.Domain.Tests.Unit.Quality;

[Trait("Category", "Unit")]
public sealed class QualitySignalSurveyTests
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Between = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Watched = TimeSpan.FromHours(15);

    private static readonly TunerDeviceId Tuner = new("adapter3.frontend0");

    private static readonly IReadOnlyList<QualityThresholdStanding> Levels =
        QualityThresholdStanding.Over([], Noon);

    [Fact(DisplayName = "a tuner nothing has sampled reads as unmeasured, never as good")]
    public void ATunerNothingHasSampledReadsAsUnmeasuredNeverAsGood()
    {
        IReadOnlyList<QualitySignalRead> read = QualitySignalSurvey.Read([], [Tuner], Levels);

        Assert.Equal(QualitySignalSurvey.Keys, read.Select(one => one.Key));

        foreach (QualitySignalRead one in read)
        {
            Assert.Equal(QualityState.Unmeasured, QualityStates.Of(one.Reading));
            Assert.Equal(1, one.Reading.Subjects);
            Assert.Equal(0, one.Reading.Measured);
            Assert.Null(one.LastTakenAt);
        }
    }

    [Fact]
    public void NoTunerAtAllReadsAsNothingToMeasure()
    {
        IReadOnlyList<QualitySignalRead> read = QualitySignalSurvey.Read([], [], Levels);

        Assert.All(read, one => Assert.Equal(QualityState.NothingToMeasure, QualityStates.Of(one.Reading)));
    }

    [Fact(DisplayName = "a tuner that has been sampled reads as measured, with the moment it was")]
    public void ATunerThatHasBeenSampledReadsAsMeasuredWithTheMomentItWas()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [
                Sample(
                    Noon,
                    SignalSample.WithLock(
                        Noon,
                        30000,
                        Noon,
                        [new LayerBitErrorCounts(0, 0, 1600000)],
                        Noon)),
            ]);

        IReadOnlyList<QualitySignalRead> read = QualitySignalSurvey.Read(figures, [Tuner], Levels);

        Assert.All(read, one => Assert.Equal(QualityState.Good, QualityStates.Of(one.Reading)));
        Assert.Equal(Noon, read[0].LastTakenAt);
    }

    [Fact(DisplayName = "a frontend that never locked drags its lock rate under the level")]
    public void AFrontendThatNeverLockedDragsItsLockRateUnderTheLevel()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [Sample(Noon, SignalSample.WithoutLock(Noon))]);

        QualitySignalRead lockRate = Read(QualityThresholdKey.LockRate, figures);

        Assert.Equal(QualityState.AtOrAboveWarning, QualityStates.Of(lockRate.Reading));
        Assert.Equal(1, lockRate.Reading.BeyondThreshold);
    }

    [Fact(DisplayName = "a statistic the tuner does not keep reads as unsupported, not as unmeasured")]
    public void AStatisticTheTunerDoesNotKeepReadsAsUnsupportedNotAsUnmeasured()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [Sample(Noon, SignalSample.WithoutLock(Noon, [SignalQualityMetrics.Cnr]))]);

        Assert.Equal(
            QualityState.Unsupported,
            QualityStates.Of(Read(QualityThresholdKey.CarrierToNoiseFloor, figures).Reading));
    }

    [Fact(DisplayName = "a supply nothing at all could be taken from is the one that reads as unreachable")]
    public void ASupplyNothingAtAllCouldBeTakenFromIsTheOneThatReadsAsUnreachable()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [
                Sample(Noon, SignalSample.NotTaken(Noon, SignalNotTaken.NothingReported)),
                Sample(Noon.AddSeconds(10), SignalSample.NotTaken(Noon.AddSeconds(10), SignalNotTaken.DriverUnreachable)),
            ]);

        foreach (QualitySignalRead read in QualitySignalSurvey.Read(figures, [Tuner], Levels))
        {
            Assert.Equal(QualityState.Unreachable, QualityStates.Of(read.Reading));
            Assert.Equal(0, read.Reading.Measured);
            Assert.Equal(0, read.Reading.BeyondThreshold);
        }
    }

    [Fact(DisplayName = "a supply that kept answering is not called unreachable for the samples it missed")]
    public void ASupplyThatKeptAnsweringIsNotCalledUnreachableForTheSamplesItMissed()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [
                Sample(Noon, SignalSample.WithLock(Noon, 30000, Noon)),
                Sample(Noon.AddSeconds(10), SignalSample.NotTaken(Noon.AddSeconds(10), SignalNotTaken.NothingReported)),
            ]);

        QualitySignalRead read = Read(QualityThresholdKey.CarrierToNoiseFloor, figures);

        Assert.Equal(QualityState.Good, QualityStates.Of(read.Reading));
        Assert.Equal(1, read.Reading.Measured);
        Assert.Equal(0, read.Reading.BeyondThreshold);
    }

    [Fact(DisplayName = "a silent supply beside a measured one still says how much was measured and how much went beyond")]
    public void ASilentSupplyBesideAMeasuredOneStillSaysHowMuchWasMeasuredAndHowMuchWentBeyond()
    {
        TunerDeviceId silent = new("adapter3.frontend1");

        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [
                Sample(Noon, SignalSample.WithLock(Noon, 6000, Noon)),
                Sample(Noon.AddSeconds(10), SignalSample.NotTaken(Noon.AddSeconds(10), SignalNotTaken.NothingReported), silent),
            ]);

        QualitySignalRead read = QualitySignalSurvey
            .Read(figures, [Tuner, silent], Levels)
            .Single(one => one.Key == QualityThresholdKey.CarrierToNoiseFloor);

        Assert.Equal(QualityState.Unreachable, QualityStates.Of(read.Reading));
        Assert.Equal(2, read.Reading.Subjects);
        Assert.Equal(1, read.Reading.Measured);
        Assert.Equal(1, read.Reading.BeyondThreshold);
    }

    [Fact(DisplayName = "a sample that could not be taken is left out of the lock rate's denominator")]
    public void ASampleThatCouldNotBeTakenIsLeftOutOfTheLockRatesDenominator()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [
                Sample(Noon, SignalSample.WithLock(Noon, 30000, Noon)),
                Sample(Noon.AddSeconds(10), SignalSample.NotTaken(Noon.AddSeconds(10), SignalNotTaken.NothingReported)),
            ]);

        Assert.Equal(1, Assert.Single(figures).LockRate);
    }

    [Fact(DisplayName = "a window answers for the samples it outlived")]
    public void AWindowAnswersForTheSamplesItOutlived()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [
                QualitySignalRollup.Rehydrate(
                    QualityWindow.Hour,
                    Noon.AddHours(-1),
                    Tuner,
                    new NetworkId(32736),
                    new ServiceId(1024),
                    360,
                    360,
                    0,
                    0,
                    30000,
                    30000,
                    30000,
                    []),
            ],
            []);

        SignalFigures figure = Assert.Single(figures);

        Assert.Equal(360, figure.Samples);
        Assert.Equal(30000, figure.CarrierToNoiseLowest);
        Assert.Equal(1, figure.LockRate);
        Assert.Equal(Noon.AddHours(-1), figure.LastTakenAt);
    }

    [Fact]
    public void WhatAWindowHoldsAndWhatTheLatestSamplesHoldAreCountedTogether()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [
                QualitySignalRollup.Rehydrate(
                    QualityWindow.Hour,
                    Noon.AddHours(-1),
                    Tuner,
                    new NetworkId(32736),
                    new ServiceId(1024),
                    360,
                    360,
                    0,
                    0,
                    30000,
                    30000,
                    30000,
                    []),
            ],
            [Sample(Noon, SignalSample.WithLock(Noon, 12000, Noon))]);

        SignalFigures figure = Assert.Single(figures);

        Assert.Equal(361, figure.Samples);
        Assert.Equal(12000, figure.CarrierToNoiseLowest);
        Assert.Equal(Noon, figure.LastTakenAt);
    }

    [Fact(DisplayName = "BR-QD-021: one bad sample among sound ones leaves the tuner within its levels, and the worst of it is still kept")]
    public void OneBadSampleAmongSoundOnesLeavesTheTunerWithinItsLevels()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [
                .. Enumerable.Range(0, 9).Select(turn => Sample(Noon.AddSeconds(10 * turn), Read(30_000 + turn, 0))),
                Sample(Noon.AddSeconds(90), Read(6_000, 20_000)),
            ]);

        SignalFigures figure = Assert.Single(figures);

        Assert.Equal(30_004, figure.CarrierToNoiseUsual);
        Assert.Equal(0, figure.BitErrorRateUsual);
        Assert.Equal(6_000, figure.CarrierToNoiseLowest);
        Assert.Equal(0.02, figure.BitErrorRateHighest);
        Assert.Equal(QualityState.Good, QualityStates.Of(Read(QualityThresholdKey.CarrierToNoiseFloor, figures).Reading));
        Assert.Equal(QualityState.Good, QualityStates.Of(Read(QualityThresholdKey.BitErrorRateCeiling, figures).Reading));
    }

    [Fact(DisplayName = "BR-QD-021: a tuner more than half of whose samples read beyond a level is beyond it")]
    public void ATunerMoreThanHalfOfWhoseSamplesReadBeyondALevelIsBeyondIt()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [
                Sample(Noon, Read(30_000, 0)),
                Sample(Noon.AddSeconds(10), Read(31_000, 0)),
                Sample(Noon.AddSeconds(20), Read(6_000, 20_000)),
                Sample(Noon.AddSeconds(30), Read(7_000, 15_000)),
                Sample(Noon.AddSeconds(40), Read(8_000, 10_000)),
            ]);

        SignalFigures figure = Assert.Single(figures);

        Assert.Equal(8_000, figure.CarrierToNoiseUsual);
        Assert.Equal(0.01, figure.BitErrorRateUsual);
        Assert.Equal(
            QualityState.AtOrAboveWarning,
            QualityStates.Of(Read(QualityThresholdKey.CarrierToNoiseFloor, figures).Reading));
        Assert.Equal(
            QualityState.AtOrAboveWarning,
            QualityStates.Of(Read(QualityThresholdKey.BitErrorRateCeiling, figures).Reading));
    }

    [Fact(DisplayName = "BR-QD-021: a tuner exactly half of whose samples read beyond a level is not beyond it")]
    public void ATunerExactlyHalfOfWhoseSamplesReadBeyondALevelIsNotBeyondIt()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [
                Sample(Noon, Read(30_000, 0)),
                Sample(Noon.AddSeconds(10), Read(6_000, 20_000)),
            ]);

        SignalFigures figure = Assert.Single(figures);

        Assert.Equal(30_000, figure.CarrierToNoiseUsual);
        Assert.Equal(0, figure.BitErrorRateUsual);
    }

    [Fact(DisplayName = "BR-QD-021: a sample is read by the layer that erred most")]
    public void ASampleIsReadByTheLayerThatErredMost()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [
                Sample(
                    Noon,
                    SignalSample.WithLock(
                        Noon,
                        30_000,
                        Noon,
                        [new LayerBitErrorCounts(0, 0, 1_000_000), new LayerBitErrorCounts(1, 500, 1_000_000)],
                        Noon)),
            ]);

        Assert.Equal(0.0005, Assert.Single(figures).BitErrorRateUsual);
    }

    [Fact(DisplayName = "BR-QD-021: a window that outlived its samples weighs as many as carried a figure in it, at what it averaged")]
    public void AWindowThatOutlivedItsSamplesWeighsAsManyAsCarriedAFigureInIt()
    {
        QualitySignalRollup sound = Rolled(Noon.AddHours(-2), samples: 6, unmeasured: 1, 30_000, 29_000, 31_000, 0);
        QualitySignalRollup bad = Rolled(Noon.AddHours(-1), samples: 4, unmeasured: 0, 9_000, 6_000, 12_000, 0.01);

        SignalFigures mostlySound = Assert.Single(QualitySignalSurvey.Figures([sound, bad], []));

        Assert.Equal(30_000, mostlySound.CarrierToNoiseUsual);
        Assert.Equal(0, mostlySound.BitErrorRateUsual);
        Assert.Equal(6_000, mostlySound.CarrierToNoiseLowest);

        SignalFigures mostlyBad = Assert.Single(QualitySignalSurvey.Figures(
            [sound, bad],
            [
                Sample(Noon, Read(8_000, 20_000)),
                Sample(Noon.AddSeconds(10), Read(8_500, 20_000)),
            ]));

        Assert.Equal(9_000, mostlyBad.CarrierToNoiseUsual);
        Assert.Equal(0.01, mostlyBad.BitErrorRateUsual);
    }

    [Fact(DisplayName = "BR-QD-021: a tuner that read no figure has no usual reading")]
    public void ATunerThatReadNoFigureHasNoUsualReading()
    {
        SignalFigures figure = Assert.Single(QualitySignalSurvey.Figures(
            [],
            [Sample(Noon, SignalSample.WithoutLock(Noon))]));

        Assert.Null(figure.CarrierToNoiseUsual);
        Assert.Null(figure.BitErrorRateUsual);
    }

    [Fact(DisplayName = "BR-QD-024: what each channel read on each tuner is gathered apart, at what it usually read")]
    public void WhatEachChannelReadOnEachTunerIsGatheredApart()
    {
        TunerDeviceId other = new("adapter4.frontend0");
        IReadOnlyList<QualitySignalWindow> windows =
        [
            QualitySignalWindow.Of(Sample(Noon, Read(30_000, 0))),
            QualitySignalWindow.Of(Sample(Noon.AddSeconds(10), Read(31_000, 0))),
            QualitySignalWindow.Of(Sample(Noon.AddSeconds(20), Read(12_000, 20_000), service: 2_048)),
            QualitySignalWindow.Of(Sample(Noon.AddSeconds(30), Read(13_000, 30_000), service: 2_048)),
            QualitySignalWindow.Of(Sample(Noon.AddSeconds(40), Read(25_000, 0), other, service: 2_048)),
        ];

        IReadOnlyList<ReceptionFigures> figures = QualitySignalSurvey.ByReception(windows);

        Assert.Equal(
            [
                ("adapter3.frontend0", 1_024, 31_000d, 0d),
                ("adapter3.frontend0", 2_048, 13_000d, 0.02),
                ("adapter4.frontend0", 2_048, 25_000d, 0d),
            ],
            figures.Select(one => (
                one.Figures.Tuner.Value,
                one.Service.Value,
                one.Figures.CarrierToNoiseUsual!.Value,
                one.Figures.BitErrorRateUsual!.Value)));
        Assert.All(figures, one => Assert.Equal(32736, one.Network.Value));
        Assert.Equal(
            QualitySubject.Of(QualitySubjectKind.Reception, "32736-2048@adapter4.frontend0"),
            figures[2].Subject);
    }

    [Fact(DisplayName = "a tuner that answered for fifteen hours without once locking reads as beyond the level, never as unmeasured")]
    public void ATunerThatAnsweredForFifteenHoursWithoutOnceLockingReadsAsBeyondTheLevel()
    {
        QualitySignalRead read = Read(QualityThresholdKey.LockRate, QualitySignalSurvey.Figures(Hours(), []));

        Assert.Equal(QualityState.AtOrAboveWarning, QualityStates.Of(read.Reading));
        Assert.Equal(1, read.Reading.Measured);
        Assert.Equal(1, read.Reading.BeyondThreshold);
    }

    [Fact(DisplayName = "the carrier to noise of a tuner that never locked reads as unmeasured rather than as a cold figure it never had")]
    public void TheCarrierToNoiseOfATunerThatNeverLockedReadsAsUnmeasured()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures([], Unlocked());

        SignalFigures figure = Assert.Single(figures);

        Assert.Equal(5_400, figure.Samples);
        Assert.Equal(0, figure.Locked);
        Assert.Equal(0d, figure.LockRate);
        Assert.Null(figure.CarrierToNoiseLowest);

        Assert.Equal(
            QualityState.Unmeasured,
            QualityStates.Of(Read(QualityThresholdKey.CarrierToNoiseFloor, figures).Reading));
        Assert.Equal(
            QualityState.AtOrAboveWarning,
            QualityStates.Of(Read(QualityThresholdKey.LockRate, figures).Reading));
    }

    [Fact(DisplayName = "a frontend answering the same figure for a minute is read a minute's worth of times, and the newest moment is the one the tuner says")]
    public void AFrontendAnsweringTheSameFigureForAMinuteIsReadAMinutesWorthOfTimes()
    {
        SignalFigures figures = Assert.Single(QualitySignalSurvey.Figures([], Frozen(6, 30_000)));

        Assert.Equal(6, figures.Samples);
        Assert.Equal(6, figures.Locked);
        Assert.Equal(30_000, figures.CarrierToNoiseLowest);
        Assert.Equal(Noon + (Between * 5), figures.LastTakenAt);
    }

    [Fact(DisplayName = "the same cold figure repeated is the one reading it was, not a tuner that measured well six times")]
    public void TheSameColdFigureRepeatedIsTheOneReadingItWas()
    {
        QualitySignalRead read = QualitySignalSurvey
            .Read(QualitySignalSurvey.Figures([], Frozen(6, 6_000)), [Tuner], Levels)
            .Single(one => one.Key == QualityThresholdKey.CarrierToNoiseFloor);

        Assert.Equal(QualityState.AtOrAboveWarning, QualityStates.Of(read.Reading));
        Assert.Equal(1, read.Reading.Measured);
        Assert.Equal(1, read.Reading.BeyondThreshold);
    }

    [Fact(DisplayName = "a figure asked for again and again is last taken when the frontend took it, not when it was last asked for")]
    public void AFigureAskedForAgainAndAgainIsLastTakenWhenTheFrontendTookIt()
    {
        IReadOnlyList<QualitySignalSample> asked =
        [
            .. Enumerable
                .Range(0, 6)
                .Select(step => Sample(
                    Noon + (Between * step),
                    SignalSample.WithLock(Noon + (Between * step), 30_000, Noon))),
        ];

        SignalFigures figures = Assert.Single(QualitySignalSurvey.Figures([], asked));
        QualitySignalRead read = QualitySignalSurvey
            .Read([figures], [Tuner], Levels)
            .Single(one => one.Key == QualityThresholdKey.CarrierToNoiseFloor);

        Assert.Equal(Noon, figures.LastTakenAt);
        Assert.Equal(Noon, read.LastTakenAt);
    }

    [Fact(DisplayName = "counters that began again at a lower number are not differenced across the session that started them over")]
    public void CountersThatBeganAgainAtALowerNumberAreNotDifferencedAcrossTheSession()
    {
        IReadOnlyList<QualitySignalSample> across =
        [
            Sample(Noon, Counted(Noon, 8, 4_000_000)),
            Sample(Noon + Between, Counted(Noon + Between, 3, 500_000), session: "live-2"),
        ];

        SignalFigures figures = Assert.Single(QualitySignalSurvey.Figures([], across));

        Assert.Equal(2, figures.Samples);
        Assert.Equal(2, figures.Taken);
        Assert.Equal(0, figures.Unmeasured);
        Assert.Equal(0, figures.Unreachable);
        Assert.Equal(6e-6, figures.BitErrorRateHighest.GetValueOrDefault(), 12);
    }

    private static SignalSample Read(int carrierToNoise, long errorBits)
        => SignalSample.WithLock(
            Noon,
            carrierToNoise,
            Noon,
            [new LayerBitErrorCounts(1, errorBits, 1_000_000)],
            Noon);

    private static QualitySignalRollup Rolled(
        DateTime start,
        long samples,
        long unmeasured,
        double average,
        int lowest,
        int highest,
        double errorRate)
        => QualitySignalRollup.Rehydrate(
            QualityWindow.Hour,
            start,
            Tuner,
            new NetworkId(32736),
            new ServiceId(1024),
            samples,
            samples - unmeasured,
            unmeasured,
            0,
            average,
            lowest,
            highest,
            [new LayerErrorRate(0, 0, 0), new LayerErrorRate(1, errorRate, errorRate * 2)]);

    private static QualitySignalRead Read(QualityThresholdKey key, IReadOnlyList<SignalFigures> figures)
        => QualitySignalSurvey.Read(figures, [Tuner], Levels).Single(one => one.Key == key);

    private static QualitySignalSample Sample(
        DateTime at,
        SignalSample signal,
        TunerDeviceId? tuner = null,
        int service = 1024,
        string session = "live-1")
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse(session),
            at,
            SessionPurpose.Live,
            tuner ?? Tuner,
            new NetworkId(32736),
            new ServiceId(service),
            signal);

    private static SignalSample Counted(DateTime at, long errorBits, long totalBits)
        => SignalSample.WithLock(
            at,
            null,
            null,
            [new LayerBitErrorCounts(0, errorBits, totalBits)],
            at);

    private static IReadOnlyList<QualitySignalSample> Frozen(int readings, int carrierToNoise)
        =>
        [
            .. Enumerable
                .Range(0, readings)
                .Select(step => Noon + (Between * step))
                .Select(at => Sample(at, SignalSample.WithLock(at, carrierToNoise, at))),
        ];

    private static IReadOnlyList<QualitySignalRollup> Hours()
        =>
        [
            .. Enumerable
                .Range(0, (int)Watched.TotalHours)
                .Select(hour => QualitySignalRollup.Rehydrate(
                    QualityWindow.Hour,
                    Noon - Watched + TimeSpan.FromHours(hour),
                    Tuner,
                    new NetworkId(4),
                    new ServiceId(101),
                    360,
                    0,
                    0,
                    0,
                    null,
                    null,
                    null,
                    [])),
        ];

    private static IReadOnlyList<QualitySignalSample> Unlocked()
        =>
        [
            .. Enumerable
                .Range(0, (int)(Watched / Between))
                .Select(step => Noon - Watched + (Between * step))
                .Select(at => QualitySignalSample.Rehydrate(
                    "instance-a",
                    SessionId.Parse("guide-1"),
                    at,
                    SessionPurpose.Survey,
                    Tuner,
                    new NetworkId(4),
                    new ServiceId(101),
                    SignalSample.WithoutLock(at))),
        ];
}
