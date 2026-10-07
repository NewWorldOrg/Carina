using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class FrameRatesTests
{
    private static readonly TimeSpan First = TimeSpan.FromMilliseconds(33);

    [Theory(DisplayName = "a frame length close to a standard rate's is read as the nearest of them")]
    [InlineData(333_667, 30000, 1001)]
    [InlineData(333_333, 30, 1)]
    [InlineData(417_083, 24000, 1001)]
    [InlineData(400_000, 25, 1)]
    [InlineData(166_833, 60000, 1001)]
    [InlineData(200_000, 50, 1)]
    [InlineData(416_667, 24, 1)]
    [InlineData(331_000, 30, 1)]
    public void AFrameLengthCloseToAStandardRateIsReadAsThatRate(long ticks, int numerator, int denominator)
    {
        FrameClock clock = FrameRates.Clock(TimeSpan.FromTicks(ticks), First);

        Assert.Equal((numerator, denominator, First), (clock.Numerator, clock.Denominator, clock.FirstFrameAt));
    }

    [Fact(DisplayName = "a frame length near no standard rate is kept as it is")]
    public void AFrameLengthNearNoStandardRateIsKeptAsItIs()
    {
        FrameClock clock = FrameRates.Clock(TimeSpan.FromMilliseconds(37), TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromMilliseconds(37 * 27), clock.At(27));
        Assert.Equal((1000, 37), (clock.Numerator, clock.Denominator));
    }

    [Fact(DisplayName = "a frame that lasts nothing, or longer than a second, gives no rate")]
    public void AFrameThatLastsNothingOrTooLongGivesNoRate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameRates.Clock(TimeSpan.Zero, First));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameRates.Clock(TimeSpan.FromSeconds(1.5), First));
    }
}
