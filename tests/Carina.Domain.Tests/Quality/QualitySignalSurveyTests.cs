using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Quality;

public sealed class QualitySignalSurveyTests
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TunerDeviceId Tuner = new("adapter3.frontend0");

    private static readonly IReadOnlyList<QualityThresholdStanding> Levels =
        QualityThresholdStanding.Over([], Noon);

    [Fact(DisplayName = "BR-QD-001: a tuner nothing has sampled reads as unmeasured, never as good")]
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

    [Fact(DisplayName = "BR-QD-004: a tuner that has been sampled reads as measured, with the moment it was")]
    public void ATunerThatHasBeenSampledReadsAsMeasuredWithTheMomentItWas()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [
                Sample(
                    Noon,
                    SignalSample.WithLock(
                        Noon,
                        34779,
                        Noon,
                        [new LayerBitErrorCounts(0, 0, 1671168)],
                        Noon)),
            ]);

        IReadOnlyList<QualitySignalRead> read = QualitySignalSurvey.Read(figures, [Tuner], Levels);

        Assert.All(read, one => Assert.Equal(QualityState.Good, QualityStates.Of(one.Reading)));
        Assert.Equal(Noon, read[0].LastTakenAt);
    }

    [Fact(DisplayName = "BR-QD-004: a frontend that never locked drags its lock rate under the level")]
    public void AFrontendThatNeverLockedDragsItsLockRateUnderTheLevel()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [Sample(Noon, SignalSample.WithoutLock(Noon))]);

        QualitySignalRead lockRate = Read(QualityThresholdKey.LockRate, figures);

        Assert.Equal(QualityState.AtOrAboveWarning, QualityStates.Of(lockRate.Reading));
        Assert.Equal(1, lockRate.Reading.BeyondThreshold);
    }

    [Fact(DisplayName = "BR-QD-009: a statistic the tuner does not keep reads as unsupported, not as unmeasured")]
    public void AStatisticTheTunerDoesNotKeepReadsAsUnsupportedNotAsUnmeasured()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [Sample(Noon, SignalSample.WithoutLock(Noon, [SignalQualityMetrics.Cnr]))]);

        Assert.Equal(
            QualityState.Unsupported,
            QualityStates.Of(Read(QualityThresholdKey.CarrierToNoiseFloor, figures).Reading));
    }

    [Fact(DisplayName = "BR-QD-014: a supply that went silent reads as unreachable while its measured count stands")]
    public void ASupplyThatWentSilentReadsAsUnreachableWhileItsMeasuredCountStands()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [
                Sample(Noon, SignalSample.WithLock(Noon, 34779, Noon)),
                Sample(Noon.AddSeconds(10), SignalSample.NotTaken(Noon.AddSeconds(10), SignalNotTaken.NothingReported)),
            ]);

        QualitySignalRead read = Read(QualityThresholdKey.CarrierToNoiseFloor, figures);

        Assert.Equal(QualityState.Unreachable, QualityStates.Of(read.Reading));
        Assert.Equal(1, read.Reading.Measured);
        Assert.Equal(0, read.Reading.BeyondThreshold);
    }

    [Fact(DisplayName = "BR-QD-004: a sample that could not be taken is left out of the lock rate's denominator")]
    public void ASampleThatCouldNotBeTakenIsLeftOutOfTheLockRatesDenominator()
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(
            [],
            [
                Sample(Noon, SignalSample.WithLock(Noon, 34779, Noon)),
                Sample(Noon.AddSeconds(10), SignalSample.NotTaken(Noon.AddSeconds(10), SignalNotTaken.NothingReported)),
            ]);

        Assert.Equal(1, Assert.Single(figures).LockRate);
    }

    [Fact(DisplayName = "BR-QS-003: a window answers for the samples it outlived")]
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
                    34779,
                    34779,
                    34779,
                    []),
            ],
            []);

        SignalFigures figure = Assert.Single(figures);

        Assert.Equal(360, figure.Samples);
        Assert.Equal(34779, figure.CarrierToNoiseLowest);
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
                    34779,
                    34779,
                    34779,
                    []),
            ],
            [Sample(Noon, SignalSample.WithLock(Noon, 12000, Noon))]);

        SignalFigures figure = Assert.Single(figures);

        Assert.Equal(361, figure.Samples);
        Assert.Equal(12000, figure.CarrierToNoiseLowest);
        Assert.Equal(Noon, figure.LastTakenAt);
    }

    private static QualitySignalRead Read(QualityThresholdKey key, IReadOnlyList<SignalFigures> figures)
        => QualitySignalSurvey.Read(figures, [Tuner], Levels).Single(one => one.Key == key);

    private static QualitySignalSample Sample(DateTime at, SignalSample signal)
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse("live-1"),
            at,
            SessionPurpose.Live,
            Tuner,
            new NetworkId(32736),
            new ServiceId(1024),
            signal);
}
