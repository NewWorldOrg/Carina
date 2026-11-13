using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.Unit.DataBroadcast;

[Trait("Category", "Unit")]
public sealed class CarouselResourceTests
{
    [Theory]
    [InlineData("", "text/css")]
    [InlineData("a.css", "")]
    public void AResourceWithNoPathOrNoMediaTypeIsRefused(string path, string mediaType)
    {
        Assert.ThrowsAny<ArgumentException>(() => new CarouselResource(path, mediaType, ResourceForm.Text, ReadOnlyMemory<byte>.Empty));
        Assert.ThrowsAny<ArgumentException>(() => new CatalogResource(path, mediaType));
    }

    [Fact]
    public void APathLongerThanTwoBytesOfLengthCanTellIsRefused()
    {
        string longest = new('a', CarouselNumbers.MostPathBytes);
        string tooLong = new('a', CarouselNumbers.MostPathBytes + 1);

        Assert.Equal(longest, new CatalogResource(longest, "text/css").Path);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CarouselResource(tooLong, "text/css", ResourceForm.Text, ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CatalogResource(tooLong, "text/css"));
    }

    [Fact]
    public void AResourceInAFormNotNamedIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CarouselResource("a", "image/png", (ResourceForm)9, ReadOnlyMemory<byte>.Empty));
    }
}
