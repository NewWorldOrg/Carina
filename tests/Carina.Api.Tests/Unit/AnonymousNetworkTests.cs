using System.Net;

using Carina.Api.Authentication;
using Carina.Api.Extensions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Carina.Api.Tests.Unit;

public sealed class AnonymousNetworkTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,")]
    public void AnInstallationNamingNoNetworkNamesNothing(string? setting)
    {
        AnonymousNetworks named = AnonymousNetworks.Named(setting);

        Assert.True(named.NamesNothing);
        Assert.Empty(named.Networks);
    }

    [Fact]
    public void TheNetworksAreTakenAsAddressAndPrefixHoweverTheyWereSeparated()
    {
        AnonymousNetworks named = AnonymousNetworks.Named("10.0.0.0/8, 192.168.0.0/16 fd00::/8");

        Assert.Equal(
            [IPNetwork.Parse("10.0.0.0/8"), IPNetwork.Parse("192.168.0.0/16"), IPNetwork.Parse("fd00::/8")],
            named.Networks);
        Assert.False(named.NamesNothing);
    }

    [Theory]
    [InlineData("not-a-network")]
    [InlineData("10.0.0.0")]
    [InlineData("10.0.0.0/99")]
    public void ANetworkThatIsNotOneIsRefusedByTheNameOfItsSetting(string setting)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => AnonymousNetworks.Named(setting));

        Assert.Equal(AnonymousNetworks.Key, refusal.ParamName);
        Assert.Contains(setting, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneUnreadableEntryRefusesTheWholeSettingRatherThanTheRestOfIt()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => AnonymousNetworks.Named("10.0.0.0/8, not-a-network"));

        Assert.Equal(AnonymousNetworks.Key, refusal.ParamName);
        Assert.Contains("not-a-network", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANetworkWrittenFromAnAddressInsideItIsReadBackAsTheWholeNetworkItMeans()
    {
        Assert.Equal("10.0.0.0/8", AnonymousNetworks.Named("10.1.2.3/8").ToString());
    }

    [Fact]
    public void WhatIsNamedIsSayableSoTheStartupDiagnosisCanSayIt()
    {
        Assert.Equal("10.0.0.0/8, fd00::/8", AnonymousNetworks.Named("10.0.0.0/8 fd00::/8").ToString());
        Assert.Equal("nothing", AnonymousNetworks.Named(null).ToString());
    }

    [Fact]
    public void TheSettingIsReadThroughTheOptionsTheHostValidatesAtStartup()
    {
        var options = new AnonymousNetworkOptions { Networks = "10.0.0.0/8" };

        Assert.False(options.Read().NamesNothing);
        Assert.True(new AnonymousNetworkValidation().Validate(null, options).Succeeded);
    }

    [Fact]
    public void AnUnreadableSettingFailsValidationWithTheMessageNamingIt()
    {
        var options = new AnonymousNetworkOptions { Networks = "not-a-network" };

        ValidateOptionsResult result = new AnonymousNetworkValidation().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(AnonymousNetworks.Key, result.FailureMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void ANetworkThatIsNotOneStopsTheHostNamingTheSetting()
    {
        using IHost host = Hosting("not-a-network");

        OptionsValidationException refusal = Assert.Throws<OptionsValidationException>(host.Start);

        Assert.Contains(AnonymousNetworks.Key, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInstallationThatSetsNothingNamesNoNetwork()
    {
        using IHost host = Hosting(setting: null);

        await host.StartAsync();

        Assert.True(host.Services.GetRequiredService<AnonymousNetworks>().NamesNothing);

        await host.StopAsync();
    }

    [Fact]
    public async Task TheNetworksTheHostReadsAreTheOnesTheSettingWasGiven()
    {
        using IHost host = Hosting("10.0.0.0/8");

        await host.StartAsync();

        Assert.Equal("10.0.0.0/8", host.Services.GetRequiredService<AnonymousNetworks>().ToString());

        await host.StopAsync();
    }

    private static IHost Hosting(string? setting)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        if (setting is not null)
        {
            builder.Configuration.AddInMemoryCollection(
                [new KeyValuePair<string, string?>(AnonymousNetworks.Key, setting)]);
        }

        builder.Services.AddAnonymousNetworks(builder.Configuration);

        return builder.Build();
    }
}
