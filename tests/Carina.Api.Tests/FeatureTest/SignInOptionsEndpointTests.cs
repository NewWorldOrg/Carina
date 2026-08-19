using System.Net;
using System.Text.Json;

using Carina.Domain.Auth;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

public sealed class SignInOptionsEndpointTests
{
    [Fact]
    public async Task TheSignInScreenIsToldThereIsOnlyTheLocalAccountBeforeAnyProviderIsSet()
    {
        await using OidcProbe probe = OidcProbe.OverHttp();

        JsonElement options = await AskAsync(probe);

        Assert.False(options.GetProperty("identityProvider").GetBoolean());
        Assert.Equal(JsonValueKind.Null, options.GetProperty("providerName").ValueKind);
        Assert.Equal(OnTheWire(OidcReach.NotConfigured), options.GetProperty("reach").GetString());
    }

    [Fact]
    public async Task ASetProviderIsNamedSoTheScreenCanLabelItsButton()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage started = await probe.StartAsync();
        JsonElement options = await AskAsync(probe);

        Assert.True(options.GetProperty("identityProvider").GetBoolean());
        Assert.Equal(new Uri(MockIdentityProvider.Issuer).Host, options.GetProperty("providerName").GetString());
        Assert.Equal(OnTheWire(OidcReach.Reachable), options.GetProperty("reach").GetString());
    }

    [Fact]
    public async Task AProviderThatStoppedAnsweringIsSaidToBeOutOfReachSoTheScreenOffersTheLocalAccount()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();
        probe.Idp.Reachable = false;

        using HttpResponseMessage started = await probe.StartAsync();
        JsonElement options = await AskAsync(probe);

        Assert.True(options.GetProperty("identityProvider").GetBoolean());
        Assert.Equal(OnTheWire(OidcReach.OutOfReach), options.GetProperty("reach").GetString());
    }

    [Fact]
    public async Task WhatTheScreenIsToldCarriesNothingOfTheSettingsBehindIt()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage started = await probe.StartAsync();
        using HttpResponseMessage asked = await probe.SignInOptionsAsync();
        string body = await asked.Content.ReadAsStringAsync();

        Assert.DoesNotContain(MockIdentityProvider.DiscoveryUrl, body, StringComparison.Ordinal);
        Assert.DoesNotContain(probe.Idp.ClientId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(OidcProbe.Secret, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheScreenAsksBeforeItHasAnythingToAskWith()
    {
        await using OidcProbe probe = OidcProbe.OverHttp();

        using HttpResponseMessage asked = await probe.SignInOptionsAsync();

        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.DoesNotContain(
            asked.Headers,
            header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase));
    }

    private static string OnTheWire(OidcReach reach)
        => JsonNamingPolicy.CamelCase.ConvertName(reach.ToString());

    private static async Task<JsonElement> AskAsync(OidcProbe probe)
    {
        using HttpResponseMessage asked = await probe.SignInOptionsAsync();

        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);

        using var document = JsonDocument.Parse(await asked.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("data").Clone();
    }
}
