using System.Net;

using Carina.Api.Authentication;

namespace Carina.Api.Tests.Unit;

public sealed class TrustedProxyNetworksTests
{
    [Fact]
    public void APeerInsideADeclaredNetworkIsAdmitted()
    {
        Assert.True(Parse("10.42.0.0/24").Admits(IPAddress.Parse("10.42.0.7")));
    }

    [Fact]
    public void APeerOutsideEveryDeclaredNetworkIsNot()
    {
        Assert.False(Parse("10.42.0.0/24").Admits(IPAddress.Parse("203.0.113.9")));
    }

    [Fact]
    public void SeveralNetworksMayBeDeclaredAtOnce()
    {
        var trusted = Parse("10.42.0.0/24, 192.168.7.0/24");

        Assert.True(trusted.Admits(IPAddress.Parse("192.168.7.3")));
        Assert.True(trusted.Admits(IPAddress.Parse("10.42.0.3")));
    }

    [Fact]
    public void ADualStackListenerReportingAMappedAddressIsJudgedByTheAddressItMaps()
    {
        Assert.True(Parse("10.42.0.0/24").Admits(IPAddress.Parse("::ffff:10.42.0.7")));
    }

    [Fact]
    public void APeerWithNoAddressAtAllIsNotAdmitted()
    {
        Assert.False(Parse("10.42.0.0/24").Admits(null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ADeploymentThatDeclaresNoProxyIsRefusedRatherThanLeftToGuess(string? setting)
    {
        Assert.False(TrustedProxyNetworks.TryParse(setting, out _));
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    [InlineData("10.42.0.0/24,0.0.0.0/0")]
    public void ANetworkCoveringEveryAddressIsRefusedBecauseItIsNotATrustBoundary(string setting)
    {
        Assert.False(TrustedProxyNetworks.TryParse(setting, out _));
    }

    [Theory]
    [InlineData("10.42.0.7")]
    [InlineData("not-a-network")]
    [InlineData("10.42.0.0/33")]
    public void AnEntryThatIsNotACidrNetworkIsRefused(string setting)
    {
        Assert.False(TrustedProxyNetworks.TryParse(setting, out _));
    }

    private static TrustedProxyNetworks Parse(string setting)
    {
        Assert.True(TrustedProxyNetworks.TryParse(setting, out var trusted));

        return trusted;
    }
}
