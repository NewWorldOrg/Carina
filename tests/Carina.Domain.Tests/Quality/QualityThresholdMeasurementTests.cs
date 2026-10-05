using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityThresholdMeasurementTests
{
    private static readonly DateTime At = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-QD-023: a measurement keeps its value, how many sessions it stood on and the span they were taken over")]
    public void AMeasurementKeepsWhatItStoodOn()
    {
        QualityThresholdMeasurement measurement = QualityThresholdMeasurement.Of(18_600, 328, 24, At.AddDays(-7), At, At);

        Assert.Equal(18_600, measurement.Value);
        Assert.Equal(328, measurement.Sessions);
        Assert.Equal(24, measurement.SessionsDropped);
        Assert.Equal(At.AddDays(-7), measurement.From);
        Assert.Equal(At, measurement.Until);
        Assert.Equal(At, measurement.MeasuredAt);
    }

    [Fact(DisplayName = "BR-QD-023: a measurement stands on at least one session, no more dropped ones than sessions, and a span that runs forward")]
    public void AMeasurementStandsOnSomethingThatAddsUp()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QualityThresholdMeasurement.Of(1, 0, 0, At, At, At));
        Assert.Throws<ArgumentOutOfRangeException>(() => QualityThresholdMeasurement.Of(1, 5, 6, At, At, At));
        Assert.Throws<ArgumentOutOfRangeException>(() => QualityThresholdMeasurement.Of(1, 5, -1, At, At, At));
        Assert.Throws<ArgumentException>(() => QualityThresholdMeasurement.Of(1, 5, 1, At, At.AddDays(-1), At));
        Assert.Throws<ArgumentOutOfRangeException>(() => QualityThresholdMeasurement.Of(double.NaN, 5, 1, At, At, At));
        Assert.Throws<ArgumentException>(() => QualityThresholdMeasurement.Of(
            1,
            5,
            1,
            DateTime.SpecifyKind(At, DateTimeKind.Local),
            At,
            At));
    }
}
