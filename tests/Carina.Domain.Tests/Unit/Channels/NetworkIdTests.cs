using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Unit.Channels;

[Trait("Category", "Unit")]
public sealed class NetworkIdTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void ANetworkIdOutsideSixteenBitsIsRefused(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkId(value));
    }

    [Fact]
    public void TheSameNumberIsTheSameIdentifier()
    {
        Assert.Equal(new NetworkId(4), new NetworkId(4));
        Assert.Equal(new NetworkId(4).GetHashCode(), new NetworkId(4).GetHashCode());
    }
}
