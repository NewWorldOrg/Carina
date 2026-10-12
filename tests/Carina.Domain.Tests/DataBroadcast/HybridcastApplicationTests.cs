using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.DataBroadcast;

public sealed class HybridcastApplicationTests
{
    private static readonly HybridcastApplication Autostart = new(
        16,
        1,
        HybridcastControlCode.Autostart,
        new Uri("http://hc.station.example.jp/p/index.html"),
        1,
        [new HybridcastPermission(0x1fff, ["http://station.example.jp"])]);

    [Fact(DisplayName = "BR-BV-007: an application is started when both identifiers match and it is to start by itself")]
    public void AnApplicationIsStartedWhenBothIdentifiersMatch()
    {
        Assert.True(Autostart.IsStartedBy(16, 1));
        Assert.False(Autostart.IsStartedBy(16, 2));
        Assert.False(Autostart.IsStartedBy(17, 1));
        Assert.False((Autostart with { ControlCode = HybridcastControlCode.Present }).IsStartedBy(16, 1));
    }

    [Fact(DisplayName = "BR-BA-004: an application's reach is the boundary its pairs draw around its entry")]
    public void AnApplicationsReachIsItsBoundary()
    {
        HybridcastBoundary reach = Autostart.Reach();

        Assert.True(reach.Admits(Autostart.EntryUrl));
        Assert.False(reach.Admits(new Uri("http://elsewhere.example.com/")));
    }
}
