using Carina.Domain.Migration;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class MigrationRunIdTests
{
    [Fact]
    public void ARunIdIsNeverEmpty()
    {
        Assert.Throws<ArgumentException>(() => new MigrationRunId(Guid.Empty));
    }
}
