using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Quality;

public sealed class QualitySignalRollupTests
{
    private static readonly DateTime WindowStart = new(2026, 8, 8, 3, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-QS-003: a window keeps how often the tuner locked after its samples are gone")]
    public void AWindowKeepsHowOftenTheTunerLockedAfterItsSamplesAreGone()
    {
        QualitySignalRollup rollup = Rollup(samples: 360, locked: 90);

        Assert.Equal(0.25, rollup.LockRate);
        Assert.Equal(QualityWindow.Minute, rollup.Granularity);
    }

    [Fact(DisplayName = "BR-QD-001: a window counts what was not measured beside what was")]
    public void AWindowCountsWhatWasNotMeasuredBesideWhatWas()
    {
        QualitySignalRollup rollup = QualitySignalRollup.Rehydrate(
            QualityWindow.Hour,
            WindowStart,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            360,
            360,
            12,
            8,
            null,
            null,
            null,
            null);

        Assert.Equal(12, rollup.Unmeasured);
        Assert.Equal(8, rollup.Unreachable);
        Assert.Null(rollup.CarrierToNoiseAverage);
    }

    [Fact]
    public void AWindowThatSawNoSamplesAtAllHasNoRateToGive()
        => Assert.Null(Rollup(samples: 0, locked: 0).LockRate);

    [Fact]
    public void AWindowCannotHaveLockedMoreOftenThanItLooked()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Rollup(samples: 10, locked: 11));

    [Fact]
    public void HalfACarrierToNoiseFigureSaysNeither()
        => Assert.Throws<ArgumentException>(() => QualitySignalRollup.Rehydrate(
            QualityWindow.Minute,
            WindowStart,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            6,
            6,
            0,
            0,
            33304,
            null,
            null,
            null));

    [Fact]
    public void TheAverageOfAWindowSitsBetweenItsLowestReadingAndItsHighest()
        => Assert.Throws<ArgumentException>(() => QualitySignalRollup.Rehydrate(
            QualityWindow.Minute,
            WindowStart,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            6,
            6,
            0,
            0,
            40000,
            33000,
            34000,
            null));

    [Fact(DisplayName = "BR-QD-009: a window rolls each broadcast layer up on its own")]
    public void AWindowRollsEachBroadcastLayerUpOnItsOwn()
    {
        QualitySignalRollup rollup = QualitySignalRollup.Rehydrate(
            QualityWindow.Minute,
            WindowStart,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            6,
            6,
            0,
            0,
            null,
            null,
            null,
            [new LayerErrorRate(1, 0.0004, 0.0009), new LayerErrorRate(0, 0.0001, 0.0002)]);

        Assert.Equal([0, 1], rollup.BitErrors.Select(rate => rate.Layer));
    }

    [Fact(DisplayName = "BR-QD-009: two rolled up rates for one layer would lose which layer failed")]
    public void TwoRolledUpRatesForOneLayerWouldLoseWhichLayerFailed()
        => Assert.Throws<ArgumentException>(() => QualitySignalRollup.Rehydrate(
            QualityWindow.Minute,
            WindowStart,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            6,
            6,
            0,
            0,
            null,
            null,
            null,
            [new LayerErrorRate(0, 0.0004, 0.0009), new LayerErrorRate(0, 0.0001, 0.0002)]));

    private static QualitySignalRollup Rollup(long samples, long locked)
        => QualitySignalRollup.Rehydrate(
            QualityWindow.Minute,
            WindowStart,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            samples,
            locked,
            0,
            0,
            null,
            null,
            null,
            null);
}
