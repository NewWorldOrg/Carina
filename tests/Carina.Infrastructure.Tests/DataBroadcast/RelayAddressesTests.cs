using System.Net;

using Carina.Infrastructure.DataBroadcast;

namespace Carina.Infrastructure.Tests.DataBroadcast;

public sealed class RelayAddressesTests
{
    [Theory(DisplayName = "BR-BD-011: an address on this machine, on the networks around it or kept for something else is not public")]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("100.64.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("127.8.9.10")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.8")]
    [InlineData("192.168.10.1")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:192.168.0.1")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2002:a00:1::1")]
    [InlineData("2001::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("ff02::1")]
    public void AnAddressInsideIsNotPublic(string address)
        => Assert.False(RelayAddresses.IsPublic(IPAddress.Parse(address)));

    [Theory(DisplayName = "BR-BD-011: an address reachable across the internet is public")]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("203.0.113.7")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]
    [InlineData("2002:808:808::1")]
    [InlineData("2400:cb00::1")]
    public void AnAddressAcrossTheInternetIsPublic(string address)
        => Assert.True(RelayAddresses.IsPublic(IPAddress.Parse(address)));

    [Theory(DisplayName = "BR-BD-011: only http and https on port 80 or 443 with no user named is admitted from the address alone")]
    [InlineData("http://bc.example.jp/bmltop/bc.txt", true)]
    [InlineData("https://bridge.example.jp/v3/dtv/create", true)]
    [InlineData("http://bc.example.jp:443/", true)]
    [InlineData("http://bc.example.jp:80/", true)]
    [InlineData("http://bc.example.jp:81/", false)]
    [InlineData("gopher://bc.example.jp/", false)]
    [InlineData("http://me@bc.example.jp/", false)]
    [InlineData("http://8.8.8.8/", true)]
    [InlineData("http://127.0.0.1/", false)]
    [InlineData("http://[::1]/", false)]
    [InlineData("http://0x7f000001/", false)]
    public void OnlyHttpAndHttpsOnTheTwoPortsAreAdmitted(string address, bool admitted)
        => Assert.Equal(admitted, RelayDestination.Admit(address) is not null);

    [Fact(DisplayName = "BR-BD-011: an address longer than the relay takes is not admitted")]
    public void AnAddressTooLongIsNotAdmitted()
        => Assert.Null(RelayDestination.Admit("http://bc.example.jp/" + new string('a', RelayDestination.LongestAddress)));
}
