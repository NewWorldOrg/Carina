using Carina.Domain.Migration;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class ServiceKeyTests
{
    [Fact]
    public void ATwoPartServiceIdentityIsTheSameWhicheverObjectHoldsIt()
    {
        Assert.Equal(ServiceKey.Of(32736, 1024), ServiceKey.Of(32736, 1024));
        Assert.NotEqual(ServiceKey.Of(32736, 1024), ServiceKey.Of(32736, 1025));
    }
}
