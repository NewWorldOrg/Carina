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
public sealed class SessionAuthenticationHandlerTests
{
    private const string Caller = "203.0.113.77";

    private const string Tablet = "a tablet that signed in a while ago";

    private static readonly Uri Me = new("/api/auth/me", UriKind.Relative);

    private static readonly Uri Sessions = new("/api/auth/sessions", UriKind.Relative);

    private readonly RecordedStartup log = new();

    [Fact]
    public async Task ACookieNamingASessionThatWasEndedNoLongerOpensAnything()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        AuthSession session = await probe.SignedInAsync();

        session.Revoke(DateTime.UtcNow);

        using HttpResponseMessage response = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ACookieCarryingSomethingThatIsNotASessionIdIsSimplyNotACaller()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        probe.Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}=not-a-session-id");

        using HttpResponseMessage response = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ACookieNamingASessionTheLedgerNeverHeldIsTakenBackFromTheBrowser()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();
        using HttpClient client = probe.Relaying($"{SessionCookie.Name}={SessionId.Issue().Value}");

        using HttpResponseMessage response = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith($"{SessionCookie.Name}=;", Discarded(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACookieCarryingSomethingThatIsNotASessionIdIsTakenBackTooRatherThanLeftToAskAgain()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();
        using HttpClient client = probe.Relaying($"{SessionCookie.Name}=not-a-session-id");

        using HttpResponseMessage response = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith($"{SessionCookie.Name}=;", Discarded(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACookieNamingASessionThatWasEndedIsTakenBackSoTheBrowserStopsSendingIt()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();
        AuthSession ended = probe.Sitting("a device that was signed out from elsewhere");

        ended.Revoke(DateTime.UtcNow);

        using HttpClient client = probe.Relaying($"{SessionCookie.Name}={probe.Sessions.CookieOf(ended).Value}");
        using HttpResponseMessage response = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith($"{SessionCookie.Name}=;", Discarded(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACookieNamingASessionThatHasLapsedIsTakenBackSoTheBrowserStopsSendingIt()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpClient client = probe.Relaying($"{SessionCookie.Name}={probe.Sessions.CookieOf(Lapsed(probe)).Value}");
        using HttpResponseMessage response = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith($"{SessionCookie.Name}=;", Discarded(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACallerCarryingNoCookieAtAllIsHandedNoneBack()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpResponseMessage response = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task ACookieThatStillOpensSomethingIsLeftWhereItIs()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();

        using HttpResponseMessage response = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task TheHandleTheListShowsSignsNobodyInWhenItIsCarriedAsTheCookie()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();
        AuthSession there = probe.Sitting("another device");

        using HttpClient client = probe.Relaying($"{SessionCookie.Name}={there.Handle.Value}");
        using HttpResponseMessage response = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SessionStatus.Active, there.StatusAt(DateTime.UtcNow, SessionPolicy.Default));
    }

    [Fact]
    public async Task TheCookieAnHttpsBrowserWasGivenStillNamesTheCallerWhenARelayCarriesItOverPlainHttp()
    {
        await using AuthProbe probe = AuthProbe.OverHttps();

        using HttpClient relayed = await probe.RelayingAsync();
        using HttpResponseMessage response = await relayed.GetAsync(Me);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    [Fact(DisplayName = "BR-AU-020: a cookie naming no session is written down once, by whoever took it back")]
    public async Task BrAu020ACookieNamingNoSessionIsWrittenDownOnce()
    {
        await using AuthProbe probe = Probe().WithAnAccount();
        using HttpClient client = probe.Relaying($"{SessionCookie.Name}={SessionId.Issue().Value}");

        using HttpResponseMessage response = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        SaidLine said = Assert.Single(Written());

        Assert.Equal(nameof(SignInMoment.TheSessionCookieNamedNoSession), said.Only("Moment"));
        Assert.Equal(SignInRecord.Carried, said.Only("SessionCookie"));
    }

    [Fact(DisplayName = "BR-AU-020: a cookie naming a session that was ended is written down as revoked, with how it had signed in and where")]
    public async Task BrAu020ACookieNamingASessionThatWasEndedIsWrittenDownAsRevoked()
    {
        await using AuthProbe probe = Probe().WithAnAccount();
        AuthSession ended = probe.Sitting(Tablet);

        ended.Revoke(DateTime.UtcNow);

        using HttpClient client = probe.Relaying($"{SessionCookie.Name}={probe.Sessions.CookieOf(ended).Value}");
        using HttpResponseMessage response = await client.GetAsync(Me);

        SaidLine said = Assert.Single(Written());

        Assert.Equal(nameof(SignInMoment.TheSessionHadBeenRevoked), said.Only("Moment"));
        Assert.Equal(nameof(AuthMethod.Local), said.Only("SignInMethod"));
        Assert.Equal(Tablet, said.Only("Device"));
    }

    [Fact(DisplayName = "BR-AU-020: a cookie naming a session that lapsed is written down as expired")]
    public async Task BrAu020ACookieNamingASessionThatLapsedIsWrittenDownAsExpired()
    {
        await using AuthProbe probe = Probe().WithAnAccount();
        AuthSession lapsed = Seated(probe, TimeSpan.FromDays(40), TimeSpan.FromDays(39));

        using HttpClient client = probe.Relaying($"{SessionCookie.Name}={probe.Sessions.CookieOf(lapsed).Value}");
        using HttpResponseMessage response = await client.GetAsync(Me);

        SaidLine said = Assert.Single(Written());

        Assert.Equal(nameof(SignInMoment.TheSessionHadExpired), said.Only("Moment"));
    }

    [Fact(DisplayName = "BR-AU-020: a session that is admitted is written down when its last use is, and not on the requests in between")]
    public async Task BrAu020ASessionThatIsAdmittedIsWrittenDownWhenItsLastUseIs()
    {
        await using AuthProbe probe = Probe().WithAnAccount();
        AuthSession resting = Seated(
            probe,
            TimeSpan.FromHours(2),
            SessionPolicy.Default.BetweenLastUsedWrites + TimeSpan.FromMinutes(1));

        using HttpClient client = probe.Relaying($"{SessionCookie.Name}={probe.Sessions.CookieOf(resting).Value}");

        for (int asked = 0; asked < 3; asked++)
        {
            using HttpResponseMessage response = await client.GetAsync(Me);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        SaidLine said = Assert.Single(Written());

        Assert.Equal(nameof(SignInMoment.TheSessionWasUsed), said.Only("Moment"));
        Assert.Equal(Tablet, said.Only("Device"));
    }

    private static string Discarded(HttpResponseMessage response)
        => Assert.Single(response.Headers.GetValues("Set-Cookie"));

    private static AuthSession Lapsed(AuthProbe probe)
    {
        DateTime now = DateTime.UtcNow;
        SessionId cookie = SessionId.Issue();
        AuthSession lapsed = AuthSession.Rehydrate(
            SessionHandle.Of(cookie),
            new Subject(FirstCredentials.Username),
            FirstCredentials.Username,
            AuthMethod.Local,
            now - TimeSpan.FromDays(40),
            now - TimeSpan.FromDays(39),
            "a device that stopped asking",
            null);

        return probe.Sessions.Seat(cookie, lapsed);
    }

    private static bool NeverStored(HttpResponseMessage response)
        => response.Headers.CacheControl?.NoStore is true;

    private static AuthSession Seated(AuthProbe probe, TimeSpan openedAgo, TimeSpan lastUsedAgo)
    {
        DateTime now = DateTime.UtcNow;
        SessionId cookie = SessionId.Issue();

        return probe.Sessions.Seat(
            cookie,
            AuthSession.Rehydrate(
                SessionHandle.Of(cookie),
                new Subject(FirstCredentials.Username),
                FirstCredentials.Username,
                AuthMethod.Local,
                now - openedAgo,
                now - lastUsedAgo,
                Tablet,
                null));
    }

    private AuthProbe Probe() => AuthProbe.OverHttp(Heard);

    private void Heard(IServiceCollection services)
    {
        services.AddSingleton<ILoggerProvider>(log);
        services.AddSingleton<IStartupFilter>(new ArrivingFrom(IPAddress.Parse(Caller)));
    }

    private IReadOnlyList<SaidLine> Written() => log.By<SignInRecord>();
}
