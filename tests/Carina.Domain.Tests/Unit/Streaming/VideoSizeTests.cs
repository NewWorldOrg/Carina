using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class VideoSizeTests
{
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
}
