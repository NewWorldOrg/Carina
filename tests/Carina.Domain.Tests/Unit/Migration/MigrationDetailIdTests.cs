using Carina.Domain.Migration;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class MigrationDetailIdTests
{
    [Fact]
    public void ADetailIdIsNeverEmpty()
    {
        Assert.Throws<ArgumentException>(() => new MigrationDetailId(Guid.Empty));
    }
}
