using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class FrameRateTests
{
    [Theory]
    [InlineData("30000/1001", 30000, 1001)]
    [InlineData("30/1", 30, 1)]
    [InlineData("60000/1001", 60000, 1001)]
    public void ARateIsReadAsTheRatioItIs(string text, int numerator, int denominator)
    {
        FrameRate? rate = FrameRate.Read(text);

        Assert.NotNull(rate);
        Assert.Equal(numerator, rate.Numerator);
        Assert.Equal(denominator, rate.Denominator);
    }

    [Theory]
    [InlineData("0/0")]
    [InlineData("30")]
    [InlineData("/1001")]
    [InlineData("30000/0")]
    [InlineData("N/A")]
    [InlineData("")]
    [InlineData(null)]
    public void SomethingThatIsNotARatioIsNotARate(string? text)
    {
        Assert.Null(FrameRate.Read(text));
    }

    [Fact]
    public void ARateOfAlmostThirtyIsNotThirty()
    {
        Assert.Equal(29.97, FrameRate.Of(30000, 1001).PerSecond, 2);
        Assert.NotEqual(30d, FrameRate.Of(30000, 1001).PerSecond);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(30, 0)]
    [InlineData(-30, 1)]
    public void ARateOutsideTheShapeIsRefused(int numerator, int denominator)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameRate.Of(numerator, denominator));
    }
}
