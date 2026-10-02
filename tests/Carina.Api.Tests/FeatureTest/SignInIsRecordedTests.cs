using System.Net;
using System.Net.Http.Json;

using Carina.Api.Authentication;
using Carina.Domain.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

public sealed class SignInIsRecordedTests
{
    private const string Caller = "203.0.113.77";

    private const string Tablet = "a tablet that signed in a while ago";

    private static readonly Uri Me = new("/api/auth/me", UriKind.Relative);

    private static readonly Uri Logout = new("/api/auth/logout", UriKind.Relative);

    private readonly RecordedStartup log = new();

    [Fact(DisplayName = "BR-AU-020: an API request with no session cookie is written down as refused for carrying none")]
    public async Task BrAu020AnApiRequestWithNoSessionCookieIsWrittenDownAsRefusedForCarryingNone()
    {
        await using AuthProbe probe = Probe().WithAnAccount();

        using HttpResponseMessage response = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        SaidLine said = Assert.Single(Written());

        Assert.Equal(nameof(SignInMoment.RefusedWithoutASessionCookie), said.Only("Moment"));
        Assert.Equal("/api/auth/me", said.Only("Route"));
        Assert.Equal(SignInRecord.Absent, said.Only("SessionCookie"));
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

    [Fact(DisplayName = "BR-AU-020: a local sign-in, a refused one and the sign-out are each written down")]
    public async Task BrAu020ALocalSignInARefusedOneAndTheSignOutAreEachWrittenDown()
    {
        await using AuthProbe probe = Probe().WithAnAccount();

        using HttpResponseMessage refused = await probe.LogInAsync(FirstCredentials.Username, "not the password at all");
        using HttpResponseMessage opened = await probe.LogInAsync(FirstCredentials.Username, AuthProbe.Password);
        using HttpResponseMessage left = await probe.Client.PostAsJsonAsync(Logout, new { });

        Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);
        Assert.Equal(
            [
                nameof(SignInMoment.ALocalSignInWasRefused),
                nameof(SignInMoment.ALocalSignInOpenedASession),
                nameof(SignInMoment.SignedOut),
            ],
            Written().Select(said => said.Only("Moment")));
        Assert.Equal(nameof(AuthMethod.Local), Written()[^1].Only("SignInMethod"));
    }

    [Fact(DisplayName = "BR-AU-020: a local sign-in held off for too many tries is written down as held off")]
    public async Task BrAu020ALocalSignInHeldOffIsWrittenDownAsHeldOff()
    {
        await using AuthProbe probe = Probe().WithAnAccount();

        for (int attempt = 0; attempt <= LoginRatePolicy.Default.FailuresBeforeRefusing; attempt++)
        {
            using HttpResponseMessage tried = await probe.LogInAsync(FirstCredentials.Username, "not the password at all");
        }

        Assert.Equal(nameof(SignInMoment.ALocalSignInWasHeldOff), Written()[^1].Only("Moment"));
    }

    [Fact(DisplayName = "BR-AU-020: setting out for the provider and coming back with a session are each written down")]
    public async Task BrAu020SettingOutForTheProviderAndComingBackAreEachWrittenDown()
    {
        await using OidcProbe probe = OidcProbe.OverHttp(Heard).Configured();

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));

        IReadOnlyList<SaidLine> written = Written();

        Assert.Equal(
            [
                nameof(SignInMoment.TheWayToTheProviderWasOpened),
                nameof(SignInMoment.TheWayBackFromTheProviderOpenedASession),
            ],
            written.Select(said => said.Only("Moment")));
        Assert.Equal(SignInRecord.Absent, written[0].Only("HandshakeCookie"));
        Assert.Equal(SignInRecord.Carried, written[1].Only("HandshakeCookie"));
        Assert.Equal(nameof(AuthMethod.Oidc), written[1].Only("SignInMethod"));
    }

    [Fact(DisplayName = "BR-AU-020: a way back that is refused is written down once, as a warning, with why")]
    public async Task BrAu020AWayBackThatIsRefusedIsWrittenDownOnceWithWhy()
    {
        await using OidcProbe probe = OidcProbe.OverHttp(Heard).Configured();

        Uri authorize = await probe.AuthorizeUriAsync();
        string code = probe.Idp.Authorize(authorize, new MockIdentityUser("owner"));

        using HttpClient another = probe.Relaying("seen=before");
        using HttpResponseMessage arrived = await probe.CallbackAsync(
            MockIdentityProvider.StateOf(authorize),
            code,
            another);

        SaidLine said = Assert.Single(log.By<SignInRecord>(LogLevel.Warning));

        Assert.Empty(log.By<Carina.Api.Services.OidcLoginService>());
        Assert.Equal(nameof(SignInMoment.TheWayBackFromTheProviderWasRefused), said.Only("Moment"));
        Assert.Equal(nameof(OidcRefusal.TheHandshakeBelongsToAnotherBrowser), said.Only("Reason"));
        Assert.Equal(SignInRecord.Absent, said.Only("HandshakeCookie"));
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

    [Fact(DisplayName = "BR-AU-020: nothing a local sign-in carries or hands out is in any line: not the password, the cookie, the handle, the name or the address")]
    public async Task BrAu020NothingALocalSignInCarriesOrHandsOutIsInAnyLine()
    {
        await using AuthProbe probe = Probe().WithAnAccount();

        using HttpResponseMessage refused = await probe.LogInAsync(FirstCredentials.Username, "not the password at all");
        using HttpResponseMessage opened = await probe.LogInAsync(FirstCredentials.Username, AuthProbe.Password);
        string cookie = CookieIn(opened, SessionCookie.Name);
        string handle = SessionHandle.Of(new SessionId(cookie)).Value;
        string unknown = SessionId.Issue().Value;

        using HttpClient stranger = probe.Relaying($"{SessionCookie.Name}={unknown}");
        using HttpResponseMessage turnedAway = await stranger.DeleteAsync(
            new Uri($"/api/auth/sessions/{handle}?shown={handle}", UriKind.Relative));
        using HttpResponseMessage me = await probe.Client.GetAsync(Me);
        using HttpResponseMessage left = await probe.Client.PostAsJsonAsync(Logout, new { });

        Assert.Equal(HttpStatusCode.Unauthorized, turnedAway.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);
        Assert.Equal(4, Written().Count);
        NothingSays(log.Everything, AuthProbe.Password, "not the password at all", cookie, handle, unknown);
        NothingSays(Written(), FirstCredentials.Username, Caller);
    }

    [Fact(DisplayName = "BR-AU-020: nothing a sign-in through the provider carries or hands out is in any line: not the state, the code, the mark, the cookie, the subject or the mail address")]
    public async Task BrAu020NothingASignInThroughTheProviderCarriesOrHandsOutIsInAnyLine()
    {
        await using OidcProbe probe = OidcProbe.OverHttp(Heard).Configured();
        var owner = new MockIdentityUser("the-subject-at-the-provider")
        {
            Email = "owner@example.test",
            Name = "The Owner Themself",
        };

        using HttpResponseMessage started = await probe.StartAsync("/library?shelf=a-shelf-in-the-query");
        var authorize = new Uri(started.Headers.Location!.ToString());
        Dictionary<string, StringValues> asked = QueryHelpers.ParseQuery(authorize.Query);
        string mark = CookieIn(started, OidcHandshake.MarkName);
        string code = probe.Idp.Authorize(authorize, owner);

        using HttpResponseMessage arrived = await probe.CallbackAsync(asked["state"], code);
        string cookie = CookieIn(arrived, SessionCookie.Name);

        using HttpResponseMessage again = await probe.CallbackAsync(asked["state"], code);

        Assert.Equal(3, Written().Count);
        NothingSays(
            log.Everything,
            asked["state"].ToString(),
            asked["nonce"].ToString(),
            asked["code_challenge"].ToString(),
            code,
            mark,
            cookie,
            SessionHandle.Of(new SessionId(cookie)).Value,
            OidcProbe.Secret,
            owner.Email,
            owner.Name,
            "a-shelf-in-the-query");
        NothingSays(Written(), owner.Subject, Caller);
    }

    private static void NothingSays(IReadOnlyList<SaidLine> lines, params string[] values)
    {
        Assert.NotEmpty(lines);

        foreach (SaidLine line in lines)
        {
            foreach (string value in values)
            {
                Assert.False(string.IsNullOrEmpty(value));
                Assert.DoesNotContain(value, line.Text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    line.Values,
                    held => held.Value?.ToString()?.Contains(value, StringComparison.OrdinalIgnoreCase) is true);
            }
        }
    }

    private static string CookieIn(HttpResponseMessage response, string name)
    {
        string handed = Assert.Single(
            response.Headers.GetValues(HeaderNames.SetCookie),
            cookie => cookie.StartsWith($"{name}=", StringComparison.Ordinal));

        return handed[$"{name}=".Length..handed.IndexOf(';', StringComparison.Ordinal)];
    }

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
