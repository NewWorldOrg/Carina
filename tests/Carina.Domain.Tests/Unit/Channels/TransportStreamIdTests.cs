using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Unit.Channels;

[Trait("Category", "Unit")]
public sealed class TransportStreamIdTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void ATransportStreamIdOutsideSixteenBitsIsRefused(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransportStreamId(value));
    }
}
