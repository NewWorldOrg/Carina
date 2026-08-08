namespace Carina.Contracts.Tests;

public sealed class DriverProtocolTests
{
    // The driver and the app are released independently, so the protocol version is
    // pinned here: changing it has to be a deliberate edit of this expectation
    // together with a compatibility window, not a side effect of another change.
    [Fact]
    public void VersionIsPinned()
    {
        Assert.Equal(1, DriverProtocol.Version);
    }
}
