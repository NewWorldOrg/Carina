namespace Carina.Driver.Tests;

public sealed class DeliberatelyRedProbeTests
{
    [Fact]
    public void TheHostGoesDownWhileThisTestIsStillRunning()
        => Environment.FailFast("a deliberately red run, to see what the results name");
}
