using System.Net;

using Carina.Api.Authentication;

namespace Carina.Api.Tests.Unit;

public sealed class TrustedProxiesTests
{
    [Fact]
    public void AnInstallationThatNamesNoProxyTrustsNothing()
    {
        TrustedProxies trusted = TrustedProxies.Named(null, null);

        Assert.True(trusted.TrustsNothing);
        Assert.Empty(trusted.Proxies);
        Assert.Empty(trusted.Networks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,")]
    public void ASettingCarryingNoEntryIsTheSameAsNamingNoProxy(string setting)
    {
        Assert.True(TrustedProxies.Named(setting, setting).TrustsNothing);
    }

    [Fact]
    public void TheAddressesAreTakenOneByOneHoweverTheyWereSeparated()
    {
        TrustedProxies trusted = TrustedProxies.Named("10.0.0.1, 10.0.0.2 ::1", null);

        Assert.Equal(
            [IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2"), IPAddress.IPv6Loopback],
            trusted.Proxies);
        Assert.False(trusted.TrustsNothing);
    }

    [Fact]
    public void TheNetworksAreTakenAsAddressAndPrefix()
    {
        TrustedProxies trusted = TrustedProxies.Named(null, "172.16.0.0/12,fd00::/8");

        Assert.Equal(
            [IPNetwork.Parse("172.16.0.0/12"), IPNetwork.Parse("fd00::/8")],
            trusted.Networks);
    }

    [Fact]
    public void AnAddressThatIsNotOneIsRefusedByTheNameOfItsSetting()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => TrustedProxies.Named("10.0.0.1, not-an-address", null));

        Assert.Equal(TrustedProxies.ProxiesKey, refusal.ParamName);
        Assert.Contains("not-an-address", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARangeWrittenIntoTheAddressSettingIsSentToTheNetworkSetting()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => TrustedProxies.Named("10.0.0.0/8", null));

        Assert.Equal(TrustedProxies.ProxiesKey, refusal.ParamName);
        Assert.Contains(TrustedProxies.NetworksKey, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANetworkWithoutAPrefixIsRefusedByTheNameOfItsSetting()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => TrustedProxies.Named(null, "10.0.0.0"));

        Assert.Equal(TrustedProxies.NetworksKey, refusal.ParamName);
        Assert.Contains("10.0.0.0", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatIsTrustedIsSayableSoTheStartupDiagnosisCanSayIt()
    {
        TrustedProxies trusted = TrustedProxies.Named("10.0.0.1", "172.16.0.0/12");

        Assert.Equal("10.0.0.1, 172.16.0.0/12", trusted.ToString());
        Assert.Equal("nothing", TrustedProxies.Named(null, null).ToString());
    }

    [Fact]
    public void TheSettingsAreReadThroughTheOptionsTheHostValidatesAtStartup()
    {
        var options = new ProxyTrustOptions { KnownProxies = "10.0.0.1", KnownNetworks = "172.16.0.0/12" };
        var validation = new ProxyTrustValidation();

        Assert.False(options.Read().TrustsNothing);
        Assert.True(validation.Validate(null, options).Succeeded);
    }

    [Fact]
    public void AnUnreadableSettingFailsValidationWithTheMessageNamingIt()
    {
        var options = new ProxyTrustOptions { KnownProxies = "not-an-address" };

        Microsoft.Extensions.Options.ValidateOptionsResult result =
            new ProxyTrustValidation().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(TrustedProxies.ProxiesKey, result.FailureMessage!, StringComparison.Ordinal);
    }
}
