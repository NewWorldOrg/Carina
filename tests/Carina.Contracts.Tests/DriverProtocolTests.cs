namespace Carina.Contracts.Tests;

public sealed class DriverProtocolTests
{
    [Fact]
    public void VersionIsPinned()
    {
        Assert.Equal(1, DriverProtocol.Version);
    }
}
