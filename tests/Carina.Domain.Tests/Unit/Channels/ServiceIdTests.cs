using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Unit.Channels;

[Trait("Category", "Unit")]
public sealed class ServiceIdTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void AServiceIdOutsideSixteenBitsIsRefused(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceId(value));
    }

    [Fact]
    public void IdentifiersOfDifferentKindsWithTheSameNumberAreNotEqual()
    {
        Assert.NotEqual<object>(new NetworkId(4), new ServiceId(4));
        Assert.NotEqual<object>(new ServiceId(4), new TransportStreamId(4));
    }
}
