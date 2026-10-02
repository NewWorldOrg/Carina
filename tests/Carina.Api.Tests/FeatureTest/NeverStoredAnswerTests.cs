using System.Net;
using System.Net.Http.Json;

using Carina.Api.Authentication;
using Carina.Domain.Auth;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

public sealed class NeverStoredAnswerTests
{
    private static readonly Uri Me = new("/api/auth/me", UriKind.Relative);

    private static readonly Uri Logout = new("/api/auth/logout", UriKind.Relative);

    [Fact(DisplayName = "BR-AU-021: the answer that hands out the session cookie at a local sign-in is not to be stored")]
    public async Task BrAu021TheAnswerThatHandsOutTheSessionCookieIsNotToBeStored()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpResponseMessage response = await probe.LogInAsync(FirstCredentials.Username, AuthProbe.Password);

        Assert.True(response.Headers.Contains("Set-Cookie"));
        Assert.True(NeverStored(response));
    }

    [Fact(DisplayName = "BR-AU-021: a refused local sign-in is not to be stored either")]
    public async Task BrAu021ARefusedLocalSignInIsNotToBeStoredEither()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpResponseMessage response = await probe.LogInAsync(FirstCredentials.Username, "not the password at all");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(NeverStored(response));
    }

    [Fact(DisplayName = "BR-AU-021: the answer that takes the session cookie back at sign-out is not to be stored")]
    public async Task BrAu021TheAnswerThatTakesTheCookieBackAtSignOutIsNotToBeStored()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();

        using HttpResponseMessage response = await probe.Client.PostAsJsonAsync(Logout, new { });

        Assert.True(response.Headers.Contains("Set-Cookie"));
        Assert.True(NeverStored(response));
    }

    [Fact(DisplayName = "BR-AU-021: the answer that takes back a cookie that opened nothing is not to be stored")]
    public async Task BrAu021TheAnswerThatTakesBackACookieThatOpenedNothingIsNotToBeStored()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();
        using HttpClient client = probe.Relaying($"{SessionCookie.Name}={SessionId.Issue().Value}");

        using HttpResponseMessage response = await client.GetAsync(Me);

        Assert.True(response.Headers.Contains("Set-Cookie"));
        Assert.True(NeverStored(response));
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

    [Fact(DisplayName = "BR-AU-021: the redirect that hands out the session cookie on the way back from the provider is not to be stored")]
    public async Task BrAu021TheRedirectThatHandsOutTheSessionCookieOnTheWayBackIsNotToBeStored()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));

        Assert.True(arrived.Headers.Contains("Set-Cookie"));
        Assert.True(NeverStored(arrived));
    }

    [Fact(DisplayName = "BR-AU-021: the redirect that refuses the way back from the provider is not to be stored")]
    public async Task BrAu021TheRedirectThatRefusesTheWayBackIsNotToBeStored()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage arrived = await probe.CallbackAsync(Unguessable.Issue(), "a code nobody issued");

        Assert.Equal(HttpStatusCode.Found, arrived.StatusCode);
        Assert.True(NeverStored(arrived));
    }

    private static bool NeverStored(HttpResponseMessage response)
        => response.Headers.CacheControl?.NoStore is true;
}
