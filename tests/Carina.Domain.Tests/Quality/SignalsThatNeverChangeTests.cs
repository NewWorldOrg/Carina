using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Quality;

public sealed class SignalsThatNeverChangeTests
{
    private static readonly DateTime Noon = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Between = TimeSpan.FromSeconds(10);

    private static readonly TunerDeviceId Tuner = new("adapter3.frontend0");

    private static readonly IReadOnlyList<QualityThresholdStanding> Levels =
        QualityThresholdStanding.Over([], Noon);

    [Fact(DisplayName = "BR-QD-004: a frontend answering the same figure for a minute is read a minute's worth of times, and the newest moment is the one the tuner says")]
    public void AFrontendAnsweringTheSameFigureForAMinuteIsReadAMinutesWorthOfTimes()
    {
        SignalFigures figures = Assert.Single(QualitySignalSurvey.Figures([], Frozen(6, 30_000)));

        Assert.Equal(6, figures.Samples);
        Assert.Equal(6, figures.Locked);
        Assert.Equal(30_000, figures.CarrierToNoiseLowest);
        Assert.Equal(Noon + (Between * 5), figures.LastTakenAt);
    }

    [Fact(DisplayName = "BR-QD-001: the same cold figure repeated is the one reading it was, not a tuner that measured well six times")]
    public void TheSameColdFigureRepeatedIsTheOneReadingItWas()
    {
        QualitySignalRead read = QualitySignalSurvey
            .Read(QualitySignalSurvey.Figures([], Frozen(6, 6_000)), [Tuner], Levels)
            .Single(one => one.Key == QualityThresholdKey.CarrierToNoiseFloor);

        Assert.Equal(QualityState.AtOrAboveWarning, QualityStates.Of(read.Reading));
        Assert.Equal(1, read.Reading.Measured);
        Assert.Equal(1, read.Reading.BeyondThreshold);
    }

    [Fact(DisplayName = "BR-QD-004: a figure the frontend never took again keeps the moment it was taken, however often it was asked for")]
    public void AFigureTheFrontendNeverTookAgainKeepsTheMomentItWasTaken()
    {
        IReadOnlyList<QualitySignalSample> asked =
        [
            .. Enumerable
                .Range(0, 6)
                .Select(step => Sample(
                    Noon + (Between * step),
                    "live-1",
                    SignalSample.WithLock(Noon, 30_000, Noon))),
        ];

        Assert.All(asked, sample => Assert.Equal(Noon, sample.Signal.CarrierToNoiseReadAt));
        Assert.Equal(Noon + (Between * 5), asked[^1].TakenAt);
        Assert.Equal(Between * 5, asked[^1].TakenAt - asked[^1].Signal.CarrierToNoiseReadAt);
    }

    [Fact(DisplayName = "BR-QD-005: counters that began again at a lower number are not differenced across the session that started them over")]
    public void CountersThatBeganAgainAtALowerNumberAreNotDifferencedAcrossTheSession()
    {
        IReadOnlyList<QualitySignalSample> across =
        [
            Sample(Noon, "live-1", Counted(Noon, 8, 4_000_000)),
            Sample(Noon + Between, "live-2", Counted(Noon + Between, 3, 500_000)),
        ];

        SignalFigures figures = Assert.Single(QualitySignalSurvey.Figures([], across));

        Assert.Equal(2, figures.Samples);
        Assert.Equal(2, figures.Taken);
        Assert.Equal(0, figures.Unmeasured);
        Assert.Equal(0, figures.Unreachable);
        Assert.Equal(6e-6, figures.BitErrorRateHighest.GetValueOrDefault(), 12);
    }

    [Fact(DisplayName = "BR-QD-005: a session that started over is filed beside the one before it, and neither loses which session it was")]
    public void ASessionThatStartedOverIsFiledBesideTheOneBeforeIt()
    {
        QualitySignalSample before = Sample(Noon, "live-1", Counted(Noon, 8, 4_000_000));
        QualitySignalSample after = Sample(Noon + Between, "live-2", Counted(Noon + Between, 3, 500_000));

        Assert.NotEqual(before.Session.Value, after.Session.Value);
        Assert.Equal("live-1", before.Session.Value);
        Assert.Equal("live-2", after.Session.Value);
    }

    private static IReadOnlyList<QualitySignalSample> Frozen(int readings, int carrierToNoise)
        =>
        [
            .. Enumerable
                .Range(0, readings)
                .Select(step => Noon + (Between * step))
                .Select(at => Sample(at, "live-1", SignalSample.WithLock(at, carrierToNoise, at))),
        ];

    private static SignalSample Counted(DateTime at, long errorBits, long totalBits)
        => SignalSample.WithLock(
            at,
            null,
            null,
            [new LayerBitErrorCounts(0, errorBits, totalBits)],
            at);

    private static QualitySignalSample Sample(DateTime at, string session, SignalSample signal)
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse(session),
            at,
            SessionPurpose.Live,
            Tuner,
            new NetworkId(32736),
            new ServiceId(1024),
            signal);
}
