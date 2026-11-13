using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Unit.Reservations;

[Trait("Category", "Unit")]
public sealed class BroadcastGroupKeyTests
{
    [Fact]
    public void ABroadcastGroupKeyCarriesAName()
    {
        Assert.Equal("relay-4001", new BroadcastGroupKey("relay-4001").Value);
        Assert.Throws<ArgumentException>(() => new BroadcastGroupKey(" "));
        Assert.Throws<ArgumentException>(() => new BroadcastGroupKey(" relay-4001"));
        Assert.Throws<ArgumentException>(() => new BroadcastGroupKey(new string('x', BroadcastGroupKey.MaxLength + 1)));
    }
}
