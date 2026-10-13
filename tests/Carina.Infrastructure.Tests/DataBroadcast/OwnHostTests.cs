using System.Net;

using Carina.Infrastructure.DataBroadcast;

namespace Carina.Infrastructure.Tests.DataBroadcast;

public sealed class OwnHostTests
{
    private static readonly OwnHost Own = OwnHost.Of([new Uri("https://carina.example:8443"), new Uri("https://日本.example")]);

    [Theory(DisplayName = "BR-BD-011: an address naming this installation's host is its own, whatever the case, trailing dot, script, scheme or port")]
    [InlineData("http://carina.example/")]
    [InlineData("https://carina.example/api/health")]
    [InlineData("http://CARINA.Example/")]
    [InlineData("http://carina.example./")]
    [InlineData("https://Carina.Example.:443/")]
    [InlineData("http://carina.example:443/")]
    [InlineData("https://carina.example:80/")]
    [InlineData("https://carina.example:8443/")]
    [InlineData("ws://carina.example/live")]
    [InlineData("wss://carina.example./live")]
    [InlineData("http://ＣＡＲＩＮＡ.example/")]
    [InlineData("http://日本.example/")]
    [InlineData("http://日本.EXAMPLE./")]
    [InlineData("http://日本。example/")]
    [InlineData("http://xn--wgv71a.example/")]
    [InlineData("http://XN--WGV71A.example./")]
    public void AnAddressNamingTheHostIsItsOwn(string address)
        => Assert.True(Own.IsNamedBy(new Uri(address)));

    [Theory(DisplayName = "BR-BD-011: an address naming another host is not this installation's own, however close the name")]
    [InlineData("http://api.carina.example/")]
    [InlineData("http://carina.example.jp/")]
    [InlineData("http://carina.exampl/")]
    [InlineData("http://carinaexample/")]
    [InlineData("http://日本語.example/")]
    [InlineData("http://203.0.113.7/")]
    public void AnAddressNamingAnotherHostIsNotItsOwn(string address)
        => Assert.False(Own.IsNamedBy(new Uri(address)));

    [Theory(DisplayName = "BR-BD-011: a host a connection is opened to is matched as the address names it")]
    [InlineData("carina.example", true)]
    [InlineData("Carina.Example.", true)]
    [InlineData("xn--wgv71a.example", true)]
    [InlineData("station.example", false)]
    public void AHostAConnectionIsOpenedToIsMatched(string host, bool own)
        => Assert.Equal(own, Own.IsNamedBy(host));

    [Theory(DisplayName = "BR-BD-011: a host written as an address is its own however the address is written, and so is that address wherever a name resolves to it")]
    [InlineData("http://[2001:db8::7]:8080", "http://[2001:0DB8:0:0::7]/", "2001:db8::7")]
    [InlineData("http://203.0.113.50:8080", "http://3405803826/", "::ffff:203.0.113.50")]
    public void AHostWrittenAsAnAddressIsItsOwn(string origin, string address, string resolved)
    {
        OwnHost written = OwnHost.Of([new Uri(origin)]);

        Assert.True(written.IsNamedBy(new Uri(address)));
        Assert.True(written.IsAt(IPAddress.Parse(resolved)));
        Assert.False(written.IsAt(IPAddress.Parse("198.51.100.20")));
    }

    [Fact(DisplayName = "BR-BD-011: a host written as a name is no address of its own, since the name may share its addresses with other servers")]
    public void AHostWrittenAsANameIsNoAddress()
        => Assert.False(Own.IsAt(IPAddress.Parse("203.0.113.7")));

    [Fact(DisplayName = "BR-BD-011: an installation that names no host has nothing of its own to refuse by name")]
    public void AnUnnamedInstallationRefusesNothingByName()
    {
        Assert.False(OwnHost.Unnamed.IsNamedBy(new Uri("http://carina.example/")));
        Assert.False(OwnHost.Unnamed.IsNamedBy("carina.example"));
        Assert.False(OwnHost.Unnamed.IsAt(IPAddress.Parse("203.0.113.7")));
    }

    [Fact(DisplayName = "BR-BD-011: an address that is not absolute names no host")]
    public void ARelativeAddressNamesNoHost()
        => Assert.False(Own.IsNamedBy(new Uri("/carina.example", UriKind.Relative)));
}
