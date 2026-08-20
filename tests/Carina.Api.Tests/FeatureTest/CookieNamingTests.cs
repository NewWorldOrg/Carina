using System.Net;
using System.Net.Http.Json;

using Carina.Api.Authentication;
using Carina.Domain.Auth;

using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class CookieNamingTests
{
    private static readonly Uri Me = new("/api/auth/me", UriKind.Relative);

    private static readonly Uri Logout = new("/api/auth/logout", UriKind.Relative);

    [Fact]
    public async Task TheSessionCookieIsNamedTheSameThingWhicheverWayTheRequestArrived()
    {
        await using AuthProbe overHttp = AuthProbe.OverHttp().WithAnAccount();
        await using AuthProbe overHttps = AuthProbe.OverHttps().WithAnAccount();

        using HttpResponseMessage plain = await overHttp.LogInAsync(FirstCredentials.Username, AuthProbe.Password);
        using HttpResponseMessage secure = await overHttps.LogInAsync(FirstCredentials.Username, AuthProbe.Password);

        Assert.Equal(SessionCookie.Name, NameOf(Handed(plain)));
        Assert.Equal(SessionCookie.Name, NameOf(Handed(secure)));
    }

    [Fact]
    public async Task TheCookieAnHttpsBrowserWasGivenStillNamesTheCallerWhenARelayCarriesItOverPlainHttp()
    {
        await using AuthProbe probe = AuthProbe.OverHttps();

        using HttpClient relayed = await probe.RelayingAsync();
        using HttpResponseMessage response = await relayed.GetAsync(Me);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OnlyTheCookieHandedOutOverHttpsIsMarkedSecure()
    {
        await using AuthProbe overHttp = AuthProbe.OverHttp().WithAnAccount();
        await using AuthProbe overHttps = AuthProbe.OverHttps().WithAnAccount();

        using HttpResponseMessage plain = await overHttp.LogInAsync(FirstCredentials.Username, AuthProbe.Password);
        using HttpResponseMessage secure = await overHttps.LogInAsync(FirstCredentials.Username, AuthProbe.Password);

        Assert.DoesNotContain("secure", AttributesOf(Handed(plain)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", AttributesOf(Handed(secure)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SigningOutDropsTheCookieUnderTheOneNameHoweverTheRequestArrived()
    {
        await using AuthProbe overHttp = AuthProbe.OverHttp();
        await using AuthProbe overHttps = AuthProbe.OverHttps();
        await overHttp.SignedInAsync();
        await overHttps.SignedInAsync();

        using HttpResponseMessage plain = await overHttp.Client.PostAsJsonAsync(Logout, new { });
        using HttpResponseMessage secure = await overHttps.Client.PostAsJsonAsync(Logout, new { });

        Assert.Equal(SessionCookie.Name, NameOf(Handed(plain)));
        Assert.Equal(SessionCookie.Name, NameOf(Handed(secure)));
        Assert.StartsWith($"{SessionCookie.Name}=;", Handed(secure), StringComparison.Ordinal);
        Assert.DoesNotContain("secure", AttributesOf(Handed(plain)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", AttributesOf(Handed(secure)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ARelayedSignOutEndsTheSessionTheHttpsBrowserOpened()
    {
        await using AuthProbe probe = AuthProbe.OverHttps();

        using HttpClient relayed = await probe.RelayingAsync();
        using HttpResponseMessage signedOut = await relayed.PostAsync(Logout, AuthProbe.Json());
        using HttpResponseMessage after = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        Assert.StartsWith($"{SessionCookie.Name}=;", Handed(signedOut), StringComparison.Ordinal);
        Assert.Empty(probe.Sessions.Sessions);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

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

    private static string Handed(HttpResponseMessage response)
        => Assert.Single(response.Headers.GetValues(HeaderNames.SetCookie));

    private static string NameOf(string cookie) => cookie[..cookie.IndexOf('=', StringComparison.Ordinal)];

    private static string PairOf(string cookie) => cookie[..cookie.IndexOf(';', StringComparison.Ordinal)];

    private static string AttributesOf(string cookie) => cookie[cookie.IndexOf(';', StringComparison.Ordinal)..];
}
