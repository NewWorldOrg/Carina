using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Authentication;
using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Feature.Auth;

[Trait("Category", "Feature")]
public sealed class StartOidcLoginActionTests
{
    private const string Caller = "203.0.113.77";

    private readonly RecordedStartup log = new();

    [Fact]
    public async Task TheHandshakeMarkIsNamedTheSameThingWhicheverWayTheRequestArrived()
    {
        await using OidcProbe overHttp = OidcProbe.OverHttp().Configured();
        await using OidcProbe overHttps = OidcProbe.OverHttps().Configured();

        using HttpResponseMessage plain = await overHttp.StartAsync();
        using HttpResponseMessage secure = await overHttps.StartAsync();

        Assert.Equal(OidcHandshake.MarkName, NameOf(Handed(plain)));
        Assert.Equal(OidcHandshake.MarkName, NameOf(Handed(secure)));
        Assert.DoesNotContain("secure", AttributesOf(Handed(plain)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", AttributesOf(Handed(secure)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheMarkAnHttpsBrowserWasGivenIsTheOneARelayedHandshakeIsMatchedAgainst()
    {
        await using OidcProbe probe = OidcProbe.OverHttps().Configured();

        using HttpResponseMessage started = await probe.StartAsync();
        string held = PairOf(Handed(started));

        using HttpClient relayed = probe.Relaying(held);
        using HttpResponseMessage again = await relayed.GetAsync(
            new Uri(OidcHandshake.StartPath, UriKind.Relative));

        Assert.Equal(held, PairOf(Handed(again)));
    }

    [Fact(DisplayName = "BR-AU-021: the redirect that sets out for the provider is not to be stored")]
    public async Task BrAu021TheRedirectThatSetsOutForTheProviderIsNotToBeStored()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage started = await probe.StartAsync();

        Assert.Equal(HttpStatusCode.Found, started.StatusCode);
        Assert.True(NeverStored(started));
    }

    [Fact(DisplayName = "BR-AU-021: the redirect back to the login screen when no provider is set up is not to be stored")]
    public async Task BrAu021TheRedirectBackWhenNoProviderIsSetUpIsNotToBeStored()
    {
        await using OidcProbe probe = OidcProbe.OverHttp();

        using HttpResponseMessage started = await probe.StartAsync();

        Assert.Equal(HttpStatusCode.Found, started.StatusCode);
        Assert.True(NeverStored(started));
    }

    [Fact]
    public async Task TheRedirectToTheProviderAsksForACodeBoundToAStateANonceAndADigestedVerifier()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        Uri authorize = await probe.AuthorizeUriAsync();
        Dictionary<string, StringValues> asked = QueryHelpers.ParseQuery(authorize.Query);

        Assert.Equal($"{MockIdentityProvider.Issuer}{MockIdentityProvider.AuthorizePath}", authorize.GetLeftPart(UriPartial.Path));
        Assert.Equal("code", asked["response_type"]);
        Assert.Equal(probe.Idp.ClientId, asked["client_id"]);
        Assert.Equal("http://localhost/api/auth/oidc/callback", asked["redirect_uri"]);
        Assert.Contains("openid", asked["scope"].ToString(), StringComparison.Ordinal);
        Assert.Equal(PkceChallenge.Method, asked["code_challenge_method"]);
        Assert.Equal(Unguessable.Length, asked["state"].ToString().Length);
        Assert.Equal(Unguessable.Length, asked["nonce"].ToString().Length);
        Assert.NotEmpty(asked["code_challenge"].ToString());
    }

    [Fact(DisplayName = "BR-AU-021: the address of the provider is handed over escaped, with no raw space in it")]
    public async Task BrAu021TheAddressOfTheProviderIsHandedOverEscaped()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage started = await probe.StartAsync();
        string handed = Assert.Single(started.Headers.NonValidated[HeaderNames.Location]);

        Assert.DoesNotContain(" ", handed, StringComparison.Ordinal);
        Assert.Contains("scope=openid%20profile%20email", handed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoStartsNeverShareAStateANonceOrAChallenge()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        Dictionary<string, StringValues> first = QueryHelpers.ParseQuery((await probe.AuthorizeUriAsync()).Query);
        Dictionary<string, StringValues> second = QueryHelpers.ParseQuery((await probe.AuthorizeUriAsync()).Query);

        Assert.NotEqual(first["state"], second["state"]);
        Assert.NotEqual(first["nonce"], second["nonce"]);
        Assert.NotEqual(first["code_challenge"], second["code_challenge"]);
    }

    [Fact]
    public async Task TheHandshakeIsMarkedByACookieTheScriptOnThePageCannotRead()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage started = await probe.StartAsync();
        string mark = started.Headers.GetValues(HeaderNames.SetCookie)
            .Single(cookie => cookie.StartsWith(OidcHandshake.MarkName, StringComparison.Ordinal));

        Assert.Contains("httponly", mark, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", mark, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", mark, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WithNoProviderConfiguredTheCallerIsSentBackToTheLocalFormRatherThanNowhere()
    {
        await using OidcProbe probe = OidcProbe.OverHttp();

        using HttpResponseMessage started = await probe.StartAsync("/settings");

        Assert.Equal(HttpStatusCode.Found, started.StatusCode);
        Assert.StartsWith(LoginRedirect.Path, started.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Contains(LoginRedirect.TheIdentityProviderFailed, started.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Contains("%2Fsettings", started.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithTheProviderOutOfReachTheCallerIsSentBackToTheLocalForm()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();
        probe.Idp.Reachable = false;

        using HttpResponseMessage started = await probe.StartAsync();

        Assert.Contains(LoginRedirect.TheIdentityProviderFailed, started.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheProviderIsSentTheRedirectUriTheScreenAsksForItToBeRegisteredWith()
    {
        await using PublicOriginProbe probe =
            PublicOriginProbe.Named(PublicOriginProbe.PublicAddress).Configured();

        Uri authorize = await probe.AuthorizeUriAsync();
        JsonElement config = await probe.ReadConfigAsync();

        Assert.Equal(
            config.GetProperty("redirectUri").GetString(),
            PublicOriginProbe.RedirectUriIn(authorize));
    }

    [Fact(DisplayName = "BR-AU-020: a way to the provider that cannot be opened is written down with why")]
    public async Task BrAu020AWayToTheProviderThatCannotBeOpenedIsWrittenDownWithWhy()
    {
        await using OidcProbe probe = OidcProbe.OverHttp(Heard);

        using HttpResponseMessage started = await probe.StartAsync();

        SaidLine said = Assert.Single(Written());

        Assert.Equal(nameof(SignInMoment.TheWayToTheProviderCouldNotBeOpened), said.Only("Moment"));
        Assert.Equal(nameof(OidcRefusal.NoIdentityProviderIsConfigured), said.Only("Reason"));
    }

    private static string Handed(HttpResponseMessage response)
        => Assert.Single(response.Headers.GetValues(HeaderNames.SetCookie));

    private static string NameOf(string cookie) => cookie[..cookie.IndexOf('=', StringComparison.Ordinal)];

    private static string PairOf(string cookie) => cookie[..cookie.IndexOf(';', StringComparison.Ordinal)];

    private static string AttributesOf(string cookie) => cookie[cookie.IndexOf(';', StringComparison.Ordinal)..];

    private static bool NeverStored(HttpResponseMessage response)
        => response.Headers.CacheControl?.NoStore is true;

    private void Heard(IServiceCollection services)
    {
        services.AddSingleton<ILoggerProvider>(log);
        services.AddSingleton<IStartupFilter>(new ArrivingFrom(IPAddress.Parse(Caller)));
    }

    private IReadOnlyList<SaidLine> Written() => log.By<SignInRecord>();
}
