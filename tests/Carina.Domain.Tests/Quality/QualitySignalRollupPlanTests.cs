using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Quality;

public sealed class QualitySignalRollupPlanTests
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AWindowKeepsHowOftenTheTunerLockedInIt()
    {
        IReadOnlyList<QualitySignalRollup> rolled = QualitySignalRollupPlan.Over(
            [
                Sample(Noon, SignalSample.WithLock(Noon, 30000, Noon)),
                Sample(Noon.AddSeconds(10), SignalSample.WithoutLock(Noon.AddSeconds(10))),
            ],
            QualityWindow.Hour);

        QualitySignalRollup window = Assert.Single(rolled);

        Assert.Equal(Noon, window.WindowStart);
        Assert.Equal(2, window.Samples);
        Assert.Equal(1, window.Locked);
        Assert.Equal(0.5, window.LockRate);
    }

    [Fact(DisplayName = "a window counts what could not be taken apart from what was measured")]
    public void AWindowCountsWhatCouldNotBeTakenApartFromWhatWasMeasured()
    {
        IReadOnlyList<QualitySignalRollup> rolled = QualitySignalRollupPlan.Over(
            [
                Sample(Noon, SignalSample.WithLock(Noon, 30000, Noon)),
                Sample(Noon.AddSeconds(10), SignalSample.WithoutLock(Noon.AddSeconds(10))),
                Sample(Noon.AddSeconds(20), SignalSample.NotTaken(Noon.AddSeconds(20), SignalNotTaken.NothingReported)),
            ],
            QualityWindow.Hour);

        QualitySignalRollup window = Assert.Single(rolled);

        Assert.Equal(3, window.Samples);
        Assert.Equal(1, window.Unmeasured);
        Assert.Equal(1, window.Unreachable);
    }

    [Fact]
    public void EachMinuteIsRolledUpOnItsOwn()
    {
        IReadOnlyList<QualitySignalRollup> rolled = QualitySignalRollupPlan.Over(
            [
                Sample(Noon, SignalSample.WithLock(Noon, 30000, Noon)),
                Sample(Noon.AddMinutes(1), SignalSample.WithLock(Noon.AddMinutes(1), 31000, Noon.AddMinutes(1))),
            ],
            QualityWindow.Minute);

        Assert.Equal([Noon, Noon.AddMinutes(1)], rolled.Select(window => window.WindowStart));
    }

    [Fact]
    public void TheColdestAndWarmestReadingsOfAWindowAreKeptBesideItsAverage()
    {
        IReadOnlyList<QualitySignalRollup> rolled = QualitySignalRollupPlan.Over(
            [
                Sample(Noon, SignalSample.WithLock(Noon, 30000, Noon)),
                Sample(Noon.AddSeconds(10), SignalSample.WithLock(Noon.AddSeconds(10), 31000, Noon.AddSeconds(10))),
            ],
            QualityWindow.Hour);

        QualitySignalRollup window = Assert.Single(rolled);

        Assert.Equal(30000, window.CarrierToNoiseLowest);
        Assert.Equal(31000, window.CarrierToNoiseHighest);
        Assert.Equal(30500.0, window.CarrierToNoiseAverage);
    }

    [Fact(DisplayName = "a window rolls each broadcast layer up on its own")]
    public void AWindowRollsEachBroadcastLayerUpOnItsOwn()
    {
        IReadOnlyList<QualitySignalRollup> rolled = QualitySignalRollupPlan.Over(
            [
                Sample(
                    Noon,
                    SignalSample.WithLock(
                        Noon,
                        bitErrors: [new LayerBitErrorCounts(0, 1, 100), new LayerBitErrorCounts(1, 3, 100)],
                        bitErrorsReadAt: Noon)),
            ],
            QualityWindow.Hour);

        QualitySignalRollup window = Assert.Single(rolled);

        Assert.Equal([0, 1], window.BitErrors.Select(rate => rate.Layer));
        Assert.Equal(0.01, window.BitErrors[0].Average);
        Assert.Equal(0.03, window.BitErrors[1].Average);
    }

    [Fact(DisplayName = "a counter that rewound at a session boundary is never differenced across it")]
    public void ACounterThatRewoundAtASessionBoundaryIsNeverDifferencedAcrossIt()
    {
        IReadOnlyList<QualitySignalRollup> rolled = QualitySignalRollupPlan.Over(
            [
                Sample(
                    Noon,
                    SignalSample.WithLock(
                        Noon,
                        bitErrors: [new LayerBitErrorCounts(0, 4, 58000000)],
                        bitErrorsReadAt: Noon),
                    session: "live-1"),
                Sample(
                    Noon.AddSeconds(10),
                    SignalSample.WithLock(
                        Noon.AddSeconds(10),
                        bitErrors: [new LayerBitErrorCounts(0, 1, 18000000)],
                        bitErrorsReadAt: Noon.AddSeconds(10)),
                    session: "live-2"),
            ],
            QualityWindow.Hour);

        QualitySignalRollup window = Assert.Single(rolled);

        Assert.Equal(((4d / 58000000) + (1d / 18000000)) / 2, window.BitErrors[0].Average, 12);
        Assert.Equal(4d / 58000000, window.BitErrors[0].Highest, 12);
    }

    [Fact]
    public void TwoTunersOnTheSameChannelAreRolledUpApart()
    {
        IReadOnlyList<QualitySignalRollup> rolled = QualitySignalRollupPlan.Over(
            [
                Sample(Noon, SignalSample.WithLock(Noon, 30000, Noon), tuner: "adapter0"),
                Sample(Noon, SignalSample.WithLock(Noon, 20000, Noon), tuner: "adapter1"),
            ],
            QualityWindow.Hour);

        Assert.Equal(["adapter0", "adapter1"], rolled.Select(window => window.Tuner.Value));
    }

    [Fact]
    public void ARunWithNoSamplesInItWritesNoWindows()
        => Assert.Empty(QualitySignalRollupPlan.Over([], QualityWindow.Hour));

    [Fact(DisplayName = "the window a rollup resumes from is the one after the last it wrote")]
    public void TheWindowARollupResumesFromIsTheOneAfterTheLastItWrote()
    {
        QualityRollupSpan span = Assert.IsType<QualityRollupSpan>(
            QualitySignalRollupPlan.Span(
                Noon.AddMinutes(30),
                QualityWindow.Hour,
                Noon.AddHours(-2),
                TimeSpan.FromDays(7)));

        Assert.Equal(Noon.AddHours(-1), span.From);
        Assert.Equal(Noon, span.Until);
    }

    [Fact]
    public void TheWindowStillFillingUpIsLeftAlone()
        => Assert.Null(QualitySignalRollupPlan.Span(
            Noon.AddMinutes(30),
            QualityWindow.Hour,
            Noon.AddHours(-1),
            TimeSpan.FromDays(7)));

    [Fact]
    public void ARollupThatHasNeverRunReachesBackAsFarAsTheSamplesAreKept()
    {
        QualityRollupSpan span = Assert.IsType<QualityRollupSpan>(
            QualitySignalRollupPlan.Span(Noon, QualityWindow.Hour, null, TimeSpan.FromHours(3)));

        Assert.Equal(Noon.AddHours(-3), span.From);
    }

    private static QualitySignalSample Sample(
        DateTime at,
        SignalSample signal,
        string session = "live-1",
        string tuner = "adapter0")
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse(session),
            at,
            SessionPurpose.Live,
            new TunerDeviceId(tuner),
            new NetworkId(32736),
            new ServiceId(1024),
            signal);
}
