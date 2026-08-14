using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class SignalMeasurementTests
{
    private static readonly DateTime At = new(2026, 8, 14, 1, 2, 3, DateTimeKind.Utc);

    [Fact]
    public void ALockedMeasurementKeepsTheFiguresItRead()
    {
        var measurement = SignalMeasurement.WithLock(At, 21_500, 3, 1_000_000);

        Assert.True(measurement.Locked);
        Assert.Equal(21_500, measurement.CnrMilliDecibels);
        Assert.Equal(3, measurement.PostViterbiErrorBits);
        Assert.Equal(1_000_000, measurement.PostViterbiTotalBits);
    }

    [Fact]
    public void AnUnlockedFrontendReportsNoQualityFigureAtAll()
    {
        var measurement = SignalMeasurement.WithoutLock(At);

        Assert.False(measurement.Locked);
        Assert.Null(measurement.CnrMilliDecibels);
        Assert.Null(measurement.PostViterbiErrorBits);
        Assert.Null(measurement.PostViterbiTotalBits);
    }

    [Fact]
    public void AMeasurementTimeThatIsNotUtcIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => SignalMeasurement.WithLock(new DateTime(2026, 8, 14, 1, 2, 3, DateTimeKind.Local)));
        Assert.Throws<ArgumentException>(
            () => SignalMeasurement.WithoutLock(new DateTime(2026, 8, 14, 1, 2, 3, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void TwoReadingsOfTheSameFiguresAreTheSameValue()
    {
        Assert.Equal(SignalMeasurement.WithLock(At, 21_500), SignalMeasurement.WithLock(At, 21_500));
        Assert.NotEqual(SignalMeasurement.WithLock(At, 21_500), SignalMeasurement.WithLock(At, 21_400));
    }
}
