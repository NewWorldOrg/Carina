using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.DataBroadcast;

public sealed class HybridcastBoundaryTests
{
    private static readonly Uri Entry = new("http://hc.station.example.jp/p/index.html");

    private static readonly HybridcastBoundary Managed = HybridcastBoundary.Of(
        Entry,
        [new HybridcastPermission(0x1fff, ["http://station.example.jp", "https://station.example.jp", "https://station.example/hc-home/"])]);

    [Theory(DisplayName = "BR-BA-004: a managed address takes in its host and every host under it, on its own scheme")]
    [InlineData("http://hc.station.example.jp/p/index.html", true)]
    [InlineData("http://station.example.jp/", true)]
    [InlineData("https://a.b.station.example.jp/x", true)]
    [InlineData("ws://hc.station.example.jp/socket", true)]
    [InlineData("wss://hc.station.example.jp/socket", true)]
    [InlineData("http://elsewhere.example.com/", false)]
    [InlineData("http://notstation.example.jp/", false)]
    [InlineData("http://station.example.jp.elsewhere.example.com/", false)]
    [InlineData("ftp://station.example.jp/", false)]
    public void AManagedAddressTakesInItsHostAndEveryHostUnderIt(string address, bool admitted)
    {
        Assert.Equal(admitted, Managed.Admits(new Uri(address)));
    }

    [Theory(DisplayName = "BR-BV-008: a managed address with a path takes in that directory and what is under it, and nothing beside it")]
    [InlineData("https://station.example/hc-home/", true)]
    [InlineData("https://station.example/hc-home/home/index.html", true)]
    [InlineData("https://station.example/hc-homes/", false)]
    [InlineData("https://station.example/", false)]
    [InlineData("http://station.example/hc-home/", false)]
    public void AManagedAddressWithAPathTakesInItsDirectory(string address, bool admitted)
    {
        Assert.Equal(admitted, Managed.Admits(new Uri(address)));
    }

    [Fact(DisplayName = "BR-BV-008: a managed address written without a closing slash still stops at a whole path segment")]
    public void AManagedPathStopsAtAWholeSegment()
    {
        HybridcastBoundary boundary = HybridcastBoundary.Of(Entry, [new HybridcastPermission(0x1fff, ["http://station.example.jp/app"])]);

        Assert.True(boundary.Admits(new Uri("http://station.example.jp/app")));
        Assert.True(boundary.Admits(new Uri("http://station.example.jp/app/one.js")));
        Assert.False(boundary.Admits(new Uri("http://station.example.jp/apple")));
    }

    [Fact(DisplayName = "BR-BV-008: a managed address on a port of its own takes in that port only")]
    public void AManagedAddressOnAPortTakesInThatPortOnly()
    {
        HybridcastBoundary boundary = HybridcastBoundary.Of(Entry, [new HybridcastPermission(0x1fff, ["http://station.example.jp:8080"])]);

        Assert.False(boundary.Admits(new Uri("http://station.example.jp/")));
    }

    [Theory(DisplayName = "BR-BA-004: a boundary with no managed address takes in the registered domain of the entry only")]
    [InlineData("http://hybridcast.station.co.jp/", "http://hybridcast.station.co.jp/r/sports/", true)]
    [InlineData("http://hybridcast.station.co.jp/", "https://www.station.co.jp/", true)]
    [InlineData("http://hybridcast.station.co.jp/", "http://station.co.jp/", true)]
    [InlineData("http://hybridcast.station.co.jp/", "http://other.co.jp/", false)]
    [InlineData("http://hybridcast.station.co.jp/", "http://co.jp/", false)]
    [InlineData("http://hc.station.example/", "http://cdn.station.example/", true)]
    [InlineData("http://hc.station.example/", "http://elsewhere.example/", false)]
    [InlineData("http://hc.station.example/", "wss://live.station.example/", true)]
    [InlineData("http://hc.station.example/", "ftp://hc.station.example/", false)]
    public void ABoundaryWithNoManagedAddressTakesInTheRegisteredDomain(string entry, string address, bool admitted)
    {
        HybridcastBoundary pairWithoutAddress = HybridcastBoundary.Of(new Uri(entry), [new HybridcastPermission(0x1fff, [])]);
        HybridcastBoundary noDescriptor = HybridcastBoundary.Of(new Uri(entry), []);

        Assert.Equal(admitted, pairWithoutAddress.Admits(new Uri(address)));
        Assert.Equal(admitted, noDescriptor.Admits(new Uri(address)));
    }

    [Fact(DisplayName = "BR-BA-004: a pair without managed addresses beside one with them opens the registered domain of the entry as well")]
    public void APairWithoutAddressesBesideOneWithThem()
    {
        HybridcastBoundary boundary = HybridcastBoundary.Of(
            new Uri("http://hc.station.co.jp/"),
            [new HybridcastPermission(0x1fff, ["http://partner.example.com"]), new HybridcastPermission(0x0001, [])]);

        Assert.True(boundary.Admits(new Uri("http://partner.example.com/x")));
        Assert.True(boundary.Admits(new Uri("http://www.station.co.jp/x")));
        Assert.False(boundary.Admits(new Uri("http://elsewhere.example.com/x")));
    }

    [Fact(DisplayName = "BR-BA-004: a managed address that cannot be read takes in nothing, and does not open the boundary")]
    public void AnUnreadableManagedAddressTakesInNothing()
    {
        HybridcastBoundary boundary = HybridcastBoundary.Of(Entry, [new HybridcastPermission(0x1fff, ["not an address", "ftp://station.example.jp"])]);

        Assert.False(boundary.Admits(Entry));
        Assert.False(boundary.Admits(new Uri("http://elsewhere.example.com/")));
    }

    [Fact(DisplayName = "BR-BA-004: an entry written as an address opens nothing but that address")]
    public void AnEntryWrittenAsAnAddressOpensOnlyThatAddress()
    {
        HybridcastBoundary boundary = HybridcastBoundary.Of(new Uri("http://203.0.113.7/app/"), []);

        Assert.True(boundary.Admits(new Uri("http://203.0.113.7/other")));
        Assert.False(boundary.Admits(new Uri("http://203.0.113.8/")));
    }
}
