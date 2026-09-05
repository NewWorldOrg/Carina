using Carina.Contracts;
using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class SignalSampleTests
{
    private static readonly DateTime LockRead = new(2026, 8, 8, 11, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime StatisticsRead = new(2026, 8, 8, 10, 59, 58, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-QD-004: a frontend that never locked hands over no carrier to noise figure at all")]
    public void AFrontendThatNeverLockedHandsOverNoCarrierToNoiseFigureAtAll()
    {
        SignalSample sample = SignalSample.WithoutLock(LockRead);

        Assert.False(sample.Locked);
        Assert.Null(sample.CarrierToNoiseMilliDecibels);
        Assert.Null(sample.CarrierToNoiseReadAt);
        Assert.Empty(sample.BitErrors);
        Assert.False(sample.CarriesAnyValue);
    }

    [Fact(DisplayName = "BR-QD-004: what the lock said and what the statistics said were read at different moments")]
    public void WhatTheLockSaidAndWhatTheStatisticsSaidWereReadAtDifferentMoments()
    {
        SignalSample sample = SignalSample.WithLock(LockRead, 33304, StatisticsRead);

        Assert.Equal(LockRead, sample.LockReadAt);
        Assert.Equal(StatisticsRead, sample.CarrierToNoiseReadAt);
        Assert.NotEqual(sample.LockReadAt, sample.CarrierToNoiseReadAt);
    }

    [Fact(DisplayName = "BR-QD-009: the two layers a terrestrial multiplex counts stay two")]
    public void TheTwoLayersATerrestrialMultiplexCountsStayTwo()
    {
        SignalSample sample = SignalSample.WithLock(
            LockRead,
            33304,
            StatisticsRead,
            [new LayerBitErrorCounts(1, 12, 67682304), new LayerBitErrorCounts(0, 3, 1671168)],
            StatisticsRead);

        Assert.Equal(2, sample.BitErrors.Count);
        Assert.Equal(1671168, sample.Layer(0)!.TotalBits);
        Assert.Equal(67682304, sample.Layer(1)!.TotalBits);
        Assert.Null(sample.Layer(2));
    }

    [Fact]
    public void TheLayersComeBackInTheOrderTheBroadcastNumbersThem()
    {
        SignalSample sample = SignalSample.WithLock(
            LockRead,
            bitErrors: [new LayerBitErrorCounts(1, 0, 8), new LayerBitErrorCounts(0, 0, 4)],
            bitErrorsReadAt: StatisticsRead);

        Assert.Equal([0, 1], sample.BitErrors.Select(counts => counts.Layer));
    }

    [Fact(DisplayName = "BR-QD-009: two counts for one layer would lose which layer failed")]
    public void TwoCountsForOneLayerWouldLoseWhichLayerFailed()
        => Assert.Throws<ArgumentException>(() => SignalSample.WithLock(
            LockRead,
            bitErrors: [new LayerBitErrorCounts(0, 1, 8), new LayerBitErrorCounts(0, 2, 8)],
            bitErrorsReadAt: StatisticsRead));

    [Fact(DisplayName = "BR-QV-003: a figure without the time it was read cannot be told from a frozen one")]
    public void AFigureWithoutTheTimeItWasReadCannotBeToldFromAFrozenOne()
    {
        Assert.Throws<ArgumentException>(() => SignalSample.WithLock(LockRead, 33304));
        Assert.Throws<ArgumentException>(() => SignalSample.WithLock(
            LockRead,
            bitErrors: [new LayerBitErrorCounts(0, 1, 8)]));
    }

    [Fact]
    public void ATimeWithNoFigureBesideItIsNoMoreOfAReadingThanAFigureWithNoTime()
        => Assert.Throws<ArgumentException>(() => SignalSample.WithLock(LockRead, null, StatisticsRead));

    [Fact(DisplayName = "BR-QD-009: a statistic that could not be read is named rather than left out")]
    public void AStatisticThatCouldNotBeReadIsNamedRatherThanLeftOut()
    {
        SignalSample sample = SignalSample.WithoutLock(LockRead, [SignalQualityMetrics.Cnr, SignalQualityMetrics.Cnr]);

        Assert.Equal([SignalQualityMetrics.Cnr], sample.MetricsNotRead);
    }

    [Fact]
    public void EveryTimeASampleCarriesIsKeptInUtc()
        => Assert.Throws<ArgumentException>(
            () => SignalSample.WithoutLock(new DateTime(2026, 8, 8, 20, 0, 0, DateTimeKind.Local)));

    [Fact]
    public void ALayerCannotHaveCountedFewerThanNoBits()
        => Assert.Throws<ArgumentOutOfRangeException>(() => SignalSample.WithLock(
            LockRead,
            bitErrors: [new LayerBitErrorCounts(0, -1, 8)],
            bitErrorsReadAt: StatisticsRead));
}
