using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class ThresholdTests
{
    private static readonly DateTime Changed = new(2026, 8, 21, 3, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-QD-003: a threshold carries what it shipped as, what it is now, and how much stands behind it")]
    public void AThresholdCarriesWhatItShippedAsAndWhatItIsNow()
    {
        Threshold threshold = Threshold.Of(0.0002, 0.0005, provisional: false, observations: 412, Changed);

        Assert.Equal(0.0002, threshold.Default);
        Assert.Equal(0.0005, threshold.Current);
        Assert.False(threshold.Provisional);
        Assert.Equal(412, threshold.Observations);
        Assert.Equal(Changed, threshold.UpdatedAt);
        Assert.False(threshold.IsAsShipped);
    }

    [Fact(DisplayName = "BR-QD-003: a number nobody has measured against is provisional and says so")]
    public void ANumberNobodyHasMeasuredAgainstIsProvisionalAndSaysSo()
    {
        Threshold threshold = Threshold.Provisionally(0.0002, observations: 0, Changed);

        Assert.True(threshold.Provisional);
        Assert.Equal(0, threshold.Observations);
        Assert.True(threshold.IsAsShipped);
    }

    [Fact(DisplayName = "BR-QD-003: a threshold that no longer calls itself provisional stands on measurement")]
    public void AThresholdThatNoLongerCallsItselfProvisionalStandsOnMeasurement()
        => Assert.Throws<ArgumentException>(() => Threshold.Of(0.0002, 0.0002, provisional: false, observations: 0, Changed));

    [Fact]
    public void AThresholdCannotStandOnFewerThanNoObservations()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Threshold.Provisionally(0.0002, observations: -1, Changed));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AThresholdIsANumberAReadingCanBeComparedAgainst(double value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Threshold.Provisionally(value, observations: 1, Changed));

    [Fact]
    public void TheTimeAThresholdLastMovedIsKeptInUtc()
        => Assert.Throws<ArgumentException>(
            () => Threshold.Provisionally(0.0002, observations: 1, new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Local)));
}
