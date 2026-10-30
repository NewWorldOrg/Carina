using System.Net;

using Carina.Driver.CardLending;

namespace Carina.Driver.Tests;

public sealed class HomeNetworkTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.45.6.7")]
    [InlineData("10.200.3.4")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.77.8")]
    [InlineData("169.254.12.34")]
    [InlineData("::1")]
    [InlineData("fd12:3456:789a::1")]
    [InlineData("fc00::5")]
    [InlineData("fe80::1234")]
    [InlineData("::ffff:192.168.77.8")]
    public void AnAddressInsideTheHouseIsHome(string address)
    {
        Assert.True(HomeNetwork.Holds(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("192.0.2.10")]
    [InlineData("198.51.100.7")]
    [InlineData("203.0.113.200")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("224.0.0.251")]
    [InlineData("::")]
    [InlineData("2001:db8::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:203.0.113.200")]
    [InlineData("::ffff:100.64.0.1")]
    public void AnAddressThatCanBeOutsideTheHouseIsNot(string address)
    {
        Assert.False(HomeNetwork.Holds(IPAddress.Parse(address)));
    }
}
