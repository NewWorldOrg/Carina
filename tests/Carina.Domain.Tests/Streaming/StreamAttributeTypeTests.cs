using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class StreamAttributeTypeTests
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

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1440, 0)]
    [InlineData(-1440, 1080)]
    public void APictureOfNoSizeIsRefused(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoSize(width, height));
    }

    [Fact]
    public void TwoPicturesOfTheSameSizeAreTheSameSize()
    {
        Assert.Equal(new VideoSize(1440, 1080), new VideoSize(1440, 1080));
        Assert.NotEqual(new VideoSize(1440, 1080), new VideoSize(1080, 1440));
        Assert.Equal("1440x1080", new VideoSize(1440, 1080).ToString());
    }

    [Fact]
    public void ASourceIsTheValueItWasGiven()
    {
        Assert.Equal("/srv/recordings/k-1.ts", new StreamSource("/srv/recordings/k-1.ts").Value);
        Assert.Equal(new StreamSource("/srv/recordings/k-1.ts"), new StreamSource("/srv/recordings/k-1.ts"));
    }

    [Fact]
    public void AScanTheStreamNeverSettledIsNotOneOfTheTwoWaysAPictureIsDrawn()
    {
        Assert.Equal(0, (int)ScanType.Undetermined);
        Assert.Equal(0, (int)AudioMode.Undetermined);
    }

    [Fact]
    public void AnAttributeSetIsRefusedAValueOutsideTheOnesNamed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StreamAttributes(new VideoSize(1, 1), (ScanType)99, FrameRate.Of(30, 1), AudioMode.Stereo));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StreamAttributes(new VideoSize(1, 1), ScanType.Progressive, FrameRate.Of(30, 1), (AudioMode)99));
    }
}
