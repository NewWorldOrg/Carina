using Carina.Domain.Migration;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class SourceFileTests
{
    [Fact]
    public void AFileIsNotSmallerThanEmpty()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceFile("one.m2ts", -1));
    }
}
