using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Authentication;
using Carina.Api.Services;
using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Feature.Auth;

[Trait("Category", "Feature")]
public sealed class GetOidcConfigActionTests
{
    [Fact]
    public async Task ANamedProxySayingHttpsMakesTheRedirectUriTheProviderWasRegisteredWith()
    {
        await using ForwardedProxyProbe probe = ForwardedProxyProbe.TrustingItsProxy();

        using HttpResponseMessage response = await probe.ReadConfigAsync();
        JsonElement config = await DataAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $"https://localhost{OidcHandshake.CallbackPath}",
            config.GetProperty("redirectUri").GetString());
    }

    [Fact]
    public async Task AnInstallationTrustingNothingReadsTheSameRequestAsPlainHttp()
    {
        await using ForwardedProxyProbe probe = ForwardedProxyProbe.TrustingNothing();

        using HttpResponseMessage response = await probe.ReadConfigAsync();
        JsonElement config = await DataAsync(response);

        Assert.Equal(
            $"http://localhost{OidcHandshake.CallbackPath}",
            config.GetProperty("redirectUri").GetString());
    }

    [Fact]
    public async Task AProxyThatWasNotNamedIsIgnoredHoweverItLabelledTheRequest()
    {
        await using ForwardedProxyProbe probe = ForwardedProxyProbe.TrustingSomeoneElse();

        using HttpResponseMessage response = await probe.ReadConfigAsync();
        JsonElement config = await DataAsync(response);

        Assert.Equal(
            $"http://localhost{OidcHandshake.CallbackPath}",
            config.GetProperty("redirectUri").GetString());
    }

    [Fact]
    public async Task AnInstallationWithNoProviderSaysWhichRedirectUriToRegisterFirst()
    {
        await using OidcProbe probe = OidcProbe.OverHttp();

        JsonElement config = await ReadAsync(probe);

        Assert.False(config.GetProperty("configured").GetBoolean());
        Assert.Equal("http://localhost/api/auth/oidc/callback", config.GetProperty("redirectUri").GetString());
        Assert.True(config.GetProperty("admitsEveryone").GetBoolean());
        Assert.False(config.GetProperty("secretHeld").GetBoolean());
        Assert.False(config.GetProperty("secretLost").GetBoolean());
    }

    [Fact]
    public async Task TheSecretGoesInAndNeverComesBackOut()
    {
        await using OidcProbe probe = OidcProbe.OverHttp();

        await SaveAsync(probe, new
        {
            discoveryUrl = MockIdentityProvider.DiscoveryUrl,
            clientId = "carina",
            clientSecret = OidcProbe.Secret,
        });

        using HttpResponseMessage read = await probe.ReadConfigAsync();
        string body = await read.Content.ReadAsStringAsync();

        Assert.DoesNotContain(OidcProbe.Secret, body, StringComparison.Ordinal);
        Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("secretHeld").GetBoolean());
    }

    [Fact]
    public async Task ASecretThatCanNoLongerBeOpenedIsSaidToBeLostWhileTheRestOfTheFormStays()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().WithTheSecretLost();

        JsonElement config = await ReadAsync(probe);

        Assert.True(config.GetProperty("secretLost").GetBoolean());
        Assert.False(config.GetProperty("secretHeld").GetBoolean());
        Assert.False(config.GetProperty("configured").GetBoolean());
        Assert.Equal(MockIdentityProvider.DiscoveryUrl, config.GetProperty("discoveryUrl").GetString());
    }

    [Fact]
    public async Task ReadingTheConfigurationNeedsTheCallerToHaveSignedIn()
    {
        await using OidcProbe probe = OidcProbe.OverHttp();

        using HttpResponseMessage read = await probe.Client.GetAsync(
            new Uri("/api/auth/oidc-config", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
    }

    [Fact]
    public async Task TheScreenIsToldThePublicRedirectUriEvenWhenItAskedFromInside()
    {
        await using PublicOriginProbe probe = PublicOriginProbe.Named(PublicOriginProbe.PublicAddress);

        JsonElement config = await probe.ReadConfigAsync();

        Assert.Equal(
            $"{PublicOriginProbe.PublicAddress}{OidcHandshake.CallbackPath}",
            config.GetProperty("redirectUri").GetString());
        Assert.False(config.GetProperty("redirectUriGuessed").GetBoolean());
    }

    [Fact]
    public async Task AnInstallationNamingNoPublicOriginFallsBackToTheRequestAndSaysTheUriIsAGuess()
    {
        await using PublicOriginProbe probe = PublicOriginProbe.NamingNothing();

        JsonElement config = await probe.ReadConfigAsync();

        Assert.Equal(
            $"{PublicOriginProbe.InsideAddress}{OidcHandshake.CallbackPath}",
            config.GetProperty("redirectUri").GetString());
        Assert.True(config.GetProperty("redirectUriGuessed").GetBoolean());
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("data").Clone();
    }

    private static async Task<JsonElement> ReadAsync(OidcProbe probe)
    {
        using HttpResponseMessage read = await probe.ReadConfigAsync();

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        return JsonDocument.Parse(await read.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Clone();
    }

    private static async Task<JsonElement> SaveAsync(OidcProbe probe, object body)
    {
        using HttpResponseMessage saved = await probe.SaveConfigAsync(body);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        return JsonDocument.Parse(await saved.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Clone();
    }
}
