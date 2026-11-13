using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Unit.Recordings;

[Trait("Category", "Unit")]
public sealed class OutputRootTests
{
    [Theory]
    [InlineData("bulk")]
    [InlineData("archive-2")]
    [InlineData("cold_store.1")]
    public void AnOutputRootIsANameTheDriverDeclared(string name)
        => Assert.Equal(name, new OutputRoot(name).Value);

    [Theory]
    [InlineData("/mnt/recordings")]
    [InlineData("bulk/nested")]
    [InlineData("bulk\\nested")]
    [InlineData("..")]
    [InlineData("bulk..cold")]
    [InlineData("recordings on disk")]
    [InlineData("")]
    public void AnOutputRootIsNeverAPath(string name)
        => Assert.ThrowsAny<ArgumentException>(() => new OutputRoot(name));

    [Fact]
    public void AnOutputRootIsShortEnoughToCrossTheWire()
        => Assert.Throws<ArgumentException>(() => new OutputRoot(new string('a', OutputRoot.MaxLength + 1)));
}
