using System.Net;
using System.Text.Json;

using Carina.Api.Authentication;
using Carina.Domain.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

public sealed class OidcSignInTests
{
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
    public async Task ACallerWhoComesBackWithTheCodeGetsTheSameSessionRowALocalSignInWouldHaveGiven()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage arrived = await probe.SignInAsync(
            new MockIdentityUser("owner-from-the-provider") { Email = "owner@example.test", Name = "The Owner" });

        Assert.Equal(HttpStatusCode.Found, arrived.StatusCode);
        Assert.Equal(LoginRedirect.Home, arrived.Headers.Location!.ToString());

        AuthSession started = Assert.Single(probe.Sessions.Sessions);

        Assert.Equal(AuthMethod.Oidc, started.Method);
        Assert.Equal("owner-from-the-provider", started.Subject.Value);
        Assert.Equal("owner@example.test", started.DisplayName);
        Assert.Contains(
            arrived.Headers.GetValues(HeaderNames.SetCookie),
            cookie => cookie.StartsWith($"{SessionCookie.Name}={started.Id.Value}", StringComparison.Ordinal)
                      && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TheCallerIsCarriedBackToWhereTheySetOutFrom()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"), "/settings/authentication");

        Assert.Equal("/settings/authentication", arrived.Headers.Location!.ToString());
    }

    [Fact]
    public async Task ACallerCarriedTowardsAnotherHostIsPutBackOnTheFrontPage()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"), "https://elsewhere.example/steal");

        Assert.Equal(LoginRedirect.Home, arrived.Headers.Location!.ToString());
    }

    [Fact]
    public async Task TheSessionOpenedThroughTheProviderAnswersTheRestOfTheApi()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));
        using HttpResponseMessage me = await probe.Client.GetAsync(new Uri("/api/auth/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Contains(
            $"\"method\":\"{JsonNamingPolicy.CamelCase.ConvertName(AuthMethod.Oidc.ToString())}\"",
            await me.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACodePlayedBackASecondTimeOpensNoSecondSession()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        Uri authorize = await probe.AuthorizeUriAsync();
        string state = MockIdentityProvider.StateOf(authorize);
        string code = probe.Idp.Authorize(authorize, new MockIdentityUser("owner"));

        using HttpResponseMessage first = await probe.CallbackAsync(state, code);
        using HttpResponseMessage again = await probe.CallbackAsync(state, code);

        Assert.Equal(LoginRedirect.Home, first.Headers.Location!.ToString());
        Assert.Contains(LoginRedirect.TheIdentityProviderFailed, again.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Single(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task AStateNobodyIssuedIsNotAHandshakeToFinish()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        Uri authorize = await probe.AuthorizeUriAsync();
        string code = probe.Idp.Authorize(authorize, new MockIdentityUser("owner"));

        using HttpResponseMessage arrived = await probe.CallbackAsync(Unguessable.Issue(), code);

        Assert.Contains(LoginRedirect.TheIdentityProviderFailed, arrived.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Empty(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task ACodeThatLapsedAtTheProviderBuysNoSession()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        Uri authorize = await probe.AuthorizeUriAsync();
        string code = probe.Idp.Authorize(authorize, new MockIdentityUser("owner"));

        probe.Idp.LetEveryCodeLapse();

        using HttpResponseMessage arrived = await probe.CallbackAsync(MockIdentityProvider.StateOf(authorize), code);

        Assert.Empty(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task AHandshakeLeftOpenPastItsWindowIsNoLongerOneToFinish()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        Uri authorize = await probe.AuthorizeUriAsync();
        string code = probe.Idp.Authorize(authorize, new MockIdentityUser("owner"));

        probe.Clock.Wind(OidcLoginPolicy.Default.HandshakeLifetime);

        using HttpResponseMessage arrived = await probe.CallbackAsync(MockIdentityProvider.StateOf(authorize), code);

        Assert.Empty(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task AHandshakeFinishedByAnotherBrowserBuysNoSession()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        Uri authorize = await probe.AuthorizeUriAsync();
        string code = probe.Idp.Authorize(authorize, new MockIdentityUser("owner"));

        using HttpResponseMessage arrived = await probe.CallbackAsync(
            MockIdentityProvider.StateOf(authorize),
            code,
            probe.Signed);

        Assert.Empty(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task ATokenSignedWithAKeyTheProviderNeverPublishedBuysNoSession()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();
        probe.Idp.SignsWithAKeyItNeverPublished = true;

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));

        Assert.Empty(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task ATokenCarryingSomebodyElsesNonceBuysNoSession()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();
        probe.Idp.NonceOverride = Unguessable.Issue();

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));

        Assert.Empty(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task ATokenFromAnotherIssuerBuysNoSession()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();
        probe.Idp.IssuerOverride = "https://login.elsewhere.test";

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));

        Assert.Empty(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task ATokenIssuedForAnotherClientBuysNoSession()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();
        probe.Idp.AudienceOverride = "somebody-else";

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));

        Assert.Empty(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task ATokenAlreadyPastItsExpiryWhenItArrivesBuysNoSession()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();
        probe.Idp.TokenLifetime = -OidcLoginPolicy.Default.ClockSkew - TimeSpan.FromMinutes(1);

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));

        Assert.Empty(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task TheProviderIsAskedForTheSecretItIssuedAndForNothingElse()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));

        Assert.Equal([OidcProbe.Secret], probe.Idp.SecretsOffered);
    }

    [Fact]
    public async Task SigningInReachesOnlyTheFourPlacesADiscoveryDocumentNames()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));

        Assert.Equal(
            [
                "GET /.well-known/openid-configuration",
                "GET /jwks",
                "POST /token",
            ],
            probe.Idp.Visits.Order(StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task SigningOutNeverReachesTheProviderBecauseItWouldSignTheOperatorOutOfEverythingElse()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));
        using HttpResponseMessage away = await probe.Client.PostAsync(
            new Uri("/api/auth/logout", UriKind.Relative),
            AuthProbe.Json());

        Assert.Equal(HttpStatusCode.NoContent, away.StatusCode);
        Assert.False(probe.Idp.WasVisited(MockIdentityProvider.SignOutPath));
        Assert.Empty(probe.Sessions.Sessions);
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
}
