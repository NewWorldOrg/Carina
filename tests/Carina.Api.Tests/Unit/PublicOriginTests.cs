using Carina.Api.Authentication;
using Carina.Api.Extensions;

using Microsoft.Extensions.Configuration;
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

    [Fact]
    public void AnUnreadableSettingFailsValidationWithTheMessageNamingIt()
    {
        var options = new PublicOriginOptions { Origin = "carina.example" };

        ValidateOptionsResult result = new PublicOriginValidation().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(PublicOrigin.Key, result.FailureMessage!, StringComparison.Ordinal);
    }
}
