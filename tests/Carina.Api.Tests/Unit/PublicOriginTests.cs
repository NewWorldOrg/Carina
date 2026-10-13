using Carina.Api.Authentication;
using Carina.Api.Extensions;
using Carina.Infrastructure.DataBroadcast;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Carina.Api.Tests.Unit;

public sealed class PublicOriginTests
{
    private const string Inside = "http://carina-app.inside:8080";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnInstallationNamingNoOriginFallsBackToTheAddressTheRequestArrivedOn(string? setting)
    {
        PublicRedirectUri redirect = PublicOrigin.Named(setting).RedirectUriFor(Inside);

        Assert.Equal($"{Inside}{OidcHandshake.CallbackPath}", redirect.Value);
        Assert.True(redirect.Guessed);
    }

    [Fact(DisplayName = "BR-BA-004: the origin a page is told it is served from is the named one, and the one the request arrived on where none is named")]
    public void TheOriginAPageIsServedFromIsTheNamedOne()
    {
        Assert.Equal("https://carina.example", PublicOrigin.Named("https://carina.example").OriginFor(Inside));
        Assert.Equal(Inside, PublicOrigin.Named(null).OriginFor(Inside));
    }

    [Fact]
    public void ANamedOriginIsTheRedirectUriHoweverTheRequestArrived()
    {
        PublicRedirectUri redirect = PublicOrigin.Named("https://carina.example").RedirectUriFor(Inside);

        Assert.Equal($"https://carina.example{OidcHandshake.CallbackPath}", redirect.Value);
        Assert.False(redirect.Guessed);
    }

    [Theory]
    [InlineData("https://carina.example/")]
    [InlineData("  https://carina.example  ")]
    [InlineData("https://carina.example:443")]
    public void TheOriginIsReadAsAnOriginHoweverItWasWritten(string setting)
    {
        Assert.Equal(
            $"https://carina.example{OidcHandshake.CallbackPath}",
            PublicOrigin.Named(setting).RedirectUriFor(Inside).Value);
    }

    [Fact]
    public void APortThatIsNotTheSchemeDefaultStaysInTheRedirectUri()
    {
        Assert.Equal(
            $"https://carina.example:8443{OidcHandshake.CallbackPath}",
            PublicOrigin.Named("https://carina.example:8443").RedirectUriFor(Inside).Value);
    }

    [Theory]
    [InlineData("carina.example")]
    [InlineData("ftp://carina.example")]
    [InlineData("https://")]
    public void AnAddressThatIsNotOneIsRefusedByTheNameOfItsSetting(string setting)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => PublicOrigin.Named(setting));

        Assert.Equal(PublicOrigin.Key, refusal.ParamName);
        Assert.Contains(setting.Trim(), refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://carina.example/carina")]
    [InlineData("https://carina.example/?asked=1")]
    [InlineData("https://carina.example/#here")]
    public void AnOriginCarryingAnythingAfterItIsRefusedByTheNameOfItsSetting(string setting)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => PublicOrigin.Named(setting));

        Assert.Equal(PublicOrigin.Key, refusal.ParamName);
    }

    [Fact]
    public void TheNamedOriginIsReadableAsAnAddressAndNothingIsWhereNoneIsNamed()
    {
        Assert.Equal(new Uri("https://carina.example:8443"), PublicOrigin.Named("https://carina.example:8443/").Address);
        Assert.Null(PublicOrigin.Named(null).Address);
    }

    [Fact]
    public void WhatIsPublicIsSayableSoTheStartupDiagnosisCanSayIt()
    {
        Assert.Equal("https://carina.example", PublicOrigin.Named("https://carina.example").ToString());
        Assert.Equal("nothing", PublicOrigin.Named(null).ToString());
    }

    [Fact]
    public void TheSettingIsReadThroughTheOptionsTheHostValidatesAtStartup()
    {
        var options = new PublicOriginOptions { Origin = "https://carina.example" };

        Assert.False(options.Read().IsGuessed);
        Assert.True(new PublicOriginValidation().Validate(null, options).Succeeded);
    }

    [Fact]
    public void AnOriginThatIsNotOneStopsTheHostNamingTheSetting()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection([new KeyValuePair<string, string?>(PublicOrigin.Key, "carina.example")]);
        builder.Services.AddPublicOrigin(builder.Configuration);

        using IHost host = builder.Build();
        OptionsValidationException refusal = Assert.Throws<OptionsValidationException>(host.Start);

        Assert.Contains(PublicOrigin.Key, refusal.Message, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "BR-BD-011: the host of the named origin is the one the data broadcast relay refuses as this installation's own")]
    [InlineData("https://Carina.Example:8443", "http://carina.example/", true)]
    [InlineData("https://carina.example", "https://carina.example./api/health", true)]
    [InlineData("https://carina.example", "http://station.example/", false)]
    public void TheNamedOriginIsTheHostTheRelayRefuses(string setting, string address, bool refused)
    {
        using IHost host = Hosted(setting);

        Assert.Equal(refused, host.Services.GetRequiredService<OwnHost>().IsNamedBy(new Uri(address)));
    }

    [Fact(DisplayName = "BR-BD-011: where no origin is named, the relay knows no host of its own by name")]
    public void WhereNoOriginIsNamedTheRelayKnowsNoHostByName()
    {
        using IHost host = Hosted(null);

        Assert.Same(OwnHost.Unnamed, host.Services.GetRequiredService<OwnHost>());
    }

    [Fact]
    public void AnUnreadableSettingFailsValidationWithTheMessageNamingIt()
    {
        var options = new PublicOriginOptions { Origin = "carina.example" };

        ValidateOptionsResult result = new PublicOriginValidation().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(PublicOrigin.Key, result.FailureMessage!, StringComparison.Ordinal);
    }

    private static IHost Hosted(string? origin)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection([new KeyValuePair<string, string?>(PublicOrigin.Key, origin)]);
        builder.Services.AddPublicOrigin(builder.Configuration);

        return builder.Build();
    }
}
