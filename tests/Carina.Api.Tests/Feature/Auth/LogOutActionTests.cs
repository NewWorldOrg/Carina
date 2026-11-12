using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Authentication;
using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Feature.Auth;

[Trait("Category", "Feature")]
public sealed class LogOutActionTests
{
    private const string Caller = "203.0.113.77";

    private const string Desk = "a desk that is asking";

    private static readonly Uri Me = new("/api/auth/me", UriKind.Relative);

    private static readonly Uri Sessions = new("/api/auth/sessions", UriKind.Relative);

    private static readonly Uri Logout = new("/api/auth/logout", UriKind.Relative);

    private readonly RecordedStartup log = new();

    [Fact]
    public async Task SigningOutTakesTheRowAwayAndTellsTheBrowserToDropTheCookie()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();

        using HttpResponseMessage response = await probe.Client.PostAsJsonAsync(Logout, new { });
        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, probe.Sessions.Deletions);
        Assert.Empty(probe.Sessions.Sessions);
        Assert.StartsWith($"{SessionCookie.Name}=;", cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SigningOutSendsTheCallerNowhereRatherThanOnToAnyoneElse()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();

        using HttpResponseMessage response = await probe.Client.PostAsJsonAsync(Logout, new { });

        Assert.Null(response.Headers.Location);
        Assert.False(response.Headers.Contains("Location"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task TheCookieIsWorthNothingOnceTheCallerHasSignedOut()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();

        using HttpResponseMessage signedOut = await probe.Client.PostAsJsonAsync(Logout, new { });
        using HttpResponseMessage after = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
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

    [Fact(DisplayName = "BR-AU-021: the answer that takes the session cookie back at sign-out is not to be stored")]
    public async Task BrAu021TheAnswerThatTakesTheCookieBackAtSignOutIsNotToBeStored()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();

        using HttpResponseMessage response = await probe.Client.PostAsJsonAsync(Logout, new { });

        Assert.True(response.Headers.Contains("Set-Cookie"));
        Assert.True(NeverStored(response));
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

    [Fact(DisplayName = "BR-AU-020: a sign-out is written down with the device that signed out")]
    public async Task BrAu020ASignOutIsWrittenDownWithTheDeviceThatSignedOut()
    {
        await using AuthProbe probe = Probe().WithAnAccount();
        using HttpClient desk = Carrying(probe, probe.Sitting(Desk));

        using HttpResponseMessage left = await desk.PostAsJsonAsync(Logout, new { });

        Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);

        SaidLine said = Assert.Single(Written());

        Assert.Equal(nameof(SignInMoment.SignedOut), said.Only("Moment"));
        Assert.Equal(Desk, said.Only("Device"));
    }

    private static string Handed(HttpResponseMessage response)
        => Assert.Single(response.Headers.GetValues(HeaderNames.SetCookie));

    private static string NameOf(string cookie) => cookie[..cookie.IndexOf('=', StringComparison.Ordinal)];

    private static string AttributesOf(string cookie) => cookie[cookie.IndexOf(';', StringComparison.Ordinal)..];

    private static bool NeverStored(HttpResponseMessage response)
        => response.Headers.CacheControl?.NoStore is true;

    private static HttpClient Carrying(AuthProbe probe, AuthSession session)
        => probe.Relaying($"{SessionCookie.Name}={probe.Sessions.CookieOf(session).Value}");

    private AuthProbe Probe() => AuthProbe.OverHttp(Heard);

    private void Heard(IServiceCollection services)
    {
        services.AddSingleton<ILoggerProvider>(log);
        services.AddSingleton<IStartupFilter>(new ArrivingFrom(IPAddress.Parse(Caller)));
    }

    private IReadOnlyList<SaidLine> Written() => log.By<SignInRecord>();
}
