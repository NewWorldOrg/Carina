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
public sealed class CompleteOidcLoginActionTests
{
    private const string Caller = "203.0.113.77";

    private readonly RecordedStartup log = new();

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

    [Fact]
    public async Task WithNobodyNamedEveryoneTheTenantCanSignInThroughIsLetIn()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("a-stranger"));

        Assert.Single(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task AMemberOfAnAllowedGroupIsLetIn()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedGroups: ["operators"]);

        using HttpResponseMessage arrived = await probe.SignInAsync(
            new MockIdentityUser("owner") { Groups = ["strangers", "operators"] });

        Assert.Single(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task SomebodyInNoneOfTheAllowedGroupsIsKeptOut()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedGroups: ["operators"]);

        using HttpResponseMessage arrived = await probe.SignInAsync(
            new MockIdentityUser("a-stranger") { Groups = ["strangers"] });

        Assert.Empty(probe.Sessions.Sessions);
        Assert.Contains(
            LoginRedirect.TheIdentityProviderFailed,
            arrived.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SomebodyWhoseGroupsOverflowedOutOfTheTokenIsKeptOutRatherThanFetchedForElsewhere()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedGroups: ["operators"]);

        using HttpResponseMessage arrived = await probe.SignInAsync(
            new MockIdentityUser("owner") { GroupsOverflowed = true });

        Assert.Empty(probe.Sessions.Sessions);
        Assert.Contains(
            LoginRedirect.TheIdentityProviderFailed,
            arrived.Headers.Location!.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(probe.Idp.Visits, visit => visit.EndsWith("/groups", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOverflowIsNoObstacleWhereTheInstallationDecidesByHostedDomain()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedHostedDomains: ["example.test"]);

        using HttpResponseMessage arrived = await probe.SignInAsync(
            new MockIdentityUser("owner") { GroupsOverflowed = true, HostedDomain = "example.test" });

        Assert.Single(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task AnAccountFromTheAllowedHostedDomainIsLetInWhereNoGroupsAreEverIssued()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedHostedDomains: ["example.test"]);

        using HttpResponseMessage arrived = await probe.SignInAsync(
            new MockIdentityUser("owner") { HostedDomain = "example.test" });

        Assert.Single(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task AnAccountFromAnotherHostedDomainIsKeptOut()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedHostedDomains: ["example.test"]);

        using HttpResponseMessage arrived = await probe.SignInAsync(
            new MockIdentityUser("a-stranger") { HostedDomain = "elsewhere.test" });

        Assert.Empty(probe.Sessions.Sessions);
        Assert.Contains(
            LoginRedirect.TheIdentityProviderFailed,
            arrived.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AConsumerAccountCarryingNoHostedDomainIsKeptOut()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedHostedDomains: ["example.test"]);

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("a-stranger"));

        Assert.Empty(probe.Sessions.Sessions);
        Assert.Contains(
            LoginRedirect.TheIdentityProviderFailed,
            arrived.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SomebodyKeptOutIsToldNoMoreThanSomebodyWhoseTokenNeverVerified()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedGroups: ["operators"]);

        using HttpResponseMessage kept = await probe.SignInAsync(
            new MockIdentityUser("a-stranger") { Groups = ["strangers"] });

        Assert.Contains(
            LoginRedirect.TheIdentityProviderFailed,
            kept.Headers.Location!.ToString(),
            StringComparison.Ordinal);
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
        string handed = Assert.Single(
            arrived.Headers.GetValues(HeaderNames.SetCookie),
            cookie => cookie.StartsWith($"{SessionCookie.Name}=", StringComparison.Ordinal));
        string carried = handed[$"{SessionCookie.Name}=".Length..handed.IndexOf(';', StringComparison.Ordinal)];

        Assert.Equal(started.Handle, SessionHandle.Of(new SessionId(carried)));
        Assert.Contains("httponly", handed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheCallerIsCarriedBackToWhereTheySetOutFrom()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"), "/settings/authentication");

        Assert.Equal("/settings/authentication", arrived.Headers.Location!.ToString());
    }

    [Fact(DisplayName = "BR-AU-003: a return target carrying letters outside ASCII comes back escaped, so the way back is a redirect rather than a server error")]
    public async Task BrAu003AReturnTargetCarryingLettersOutsideAsciiComesBackEscaped()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"), "/search?q=ニュース 速報");

        Assert.Equal(HttpStatusCode.Redirect, arrived.StatusCode);
        Assert.Equal(
            "/search?q=%E3%83%8B%E3%83%A5%E3%83%BC%E3%82%B9%20%E9%80%9F%E5%A0%B1",
            Assert.Single(arrived.Headers.NonValidated[HeaderNames.Location]));
        Assert.Single(probe.Sessions.Sessions);
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
        Assert.Contains(
            LoginRedirect.TheIdentityProviderFailed,
            arrived.Headers.Location!.ToString(),
            StringComparison.Ordinal);
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
        Assert.Contains(
            LoginRedirect.TheIdentityProviderFailed,
            arrived.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHandshakeFinishedByAnotherBrowserBuysNoSession()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        Uri authorize = await probe.AuthorizeUriAsync();
        string code = probe.Idp.Authorize(authorize, new MockIdentityUser("owner"));

        using HttpClient another = probe.Relaying("seen=before");
        using HttpResponseMessage arrived = await probe.CallbackAsync(
            MockIdentityProvider.StateOf(authorize),
            code,
            another);

        Assert.Empty(probe.Sessions.Sessions);
        Assert.Contains(
            LoginRedirect.TheIdentityProviderFailed,
            arrived.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-AU-003: a caller refused on the way back is sent to the login screen still carrying where they set out for")]
    public async Task BrAu003ACallerRefusedOnTheWayBackStillCarriesWhereTheySetOutFor()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        Uri authorize = await probe.AuthorizeUriAsync("/settings/authentication");
        string code = probe.Idp.Authorize(authorize, new MockIdentityUser("owner"));

        probe.Idp.LetEveryCodeLapse();

        using HttpResponseMessage arrived = await probe.CallbackAsync(MockIdentityProvider.StateOf(authorize), code);

        Assert.Equal(
            LoginRedirect.AfterAFailedSignIn("/settings/authentication"),
            Assert.Single(arrived.Headers.NonValidated[HeaderNames.Location]));
    }

    [Fact(DisplayName = "BR-AU-003: a handshake left open past its window still sends its own browser back with where it set out for")]
    public async Task BrAu003AHandshakeLeftOpenPastItsWindowStillCarriesWhereItSetOutFor()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        Uri authorize = await probe.AuthorizeUriAsync("/library?sort=newest");
        string code = probe.Idp.Authorize(authorize, new MockIdentityUser("owner"));

        probe.Clock.Wind(OidcLoginPolicy.Default.HandshakeLifetime);

        using HttpResponseMessage arrived = await probe.CallbackAsync(MockIdentityProvider.StateOf(authorize), code);

        Assert.Equal(
            LoginRedirect.AfterAFailedSignIn("/library?sort=newest"),
            Assert.Single(arrived.Headers.NonValidated[HeaderNames.Location]));
    }

    [Fact(DisplayName = "BR-AU-003: a state nobody issued carries nowhere, so the caller is sent to the login screen bound for the front page")]
    public async Task BrAu003AStateNobodyIssuedCarriesNowhere()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        Uri authorize = await probe.AuthorizeUriAsync("/settings/authentication");
        string code = probe.Idp.Authorize(authorize, new MockIdentityUser("owner"));

        using HttpResponseMessage arrived = await probe.CallbackAsync(Unguessable.Issue(), code);

        Assert.Equal(
            LoginRedirect.AfterAFailedSignIn(null),
            Assert.Single(arrived.Headers.NonValidated[HeaderNames.Location]));
    }

    [Fact(DisplayName = "BR-AU-003: another browser finishing a handshake is not told where the one that began it was going")]
    public async Task BrAu003AnotherBrowserIsNotToldWhereTheOneThatBeganWasGoing()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        Uri authorize = await probe.AuthorizeUriAsync("/settings/authentication");
        string code = probe.Idp.Authorize(authorize, new MockIdentityUser("owner"));

        using HttpClient another = probe.Relaying("seen=before");
        using HttpResponseMessage arrived = await probe.CallbackAsync(
            MockIdentityProvider.StateOf(authorize),
            code,
            another);

        Assert.Equal(
            LoginRedirect.AfterAFailedSignIn(null),
            Assert.Single(arrived.Headers.NonValidated[HeaderNames.Location]));
    }

    [Fact]
    public async Task ATokenSignedWithAKeyTheProviderNeverPublishedBuysNoSession()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();
        probe.Idp.SignsWithAKeyItNeverPublished = true;

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));

        Assert.Empty(probe.Sessions.Sessions);
        Assert.Contains(
            LoginRedirect.TheIdentityProviderFailed,
            arrived.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATokenCarryingSomebodyElsesNonceBuysNoSession()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();
        probe.Idp.NonceOverride = Unguessable.Issue();

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));

        Assert.Empty(probe.Sessions.Sessions);
        Assert.Contains(
            LoginRedirect.TheIdentityProviderFailed,
            arrived.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATokenFromAnotherIssuerBuysNoSession()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();
        probe.Idp.IssuerOverride = "https://login.elsewhere.test";

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));

        Assert.Empty(probe.Sessions.Sessions);
        Assert.Contains(
            LoginRedirect.TheIdentityProviderFailed,
            arrived.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATokenIssuedForAnotherClientBuysNoSession()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();
        probe.Idp.AudienceOverride = "somebody-else";

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));

        Assert.Empty(probe.Sessions.Sessions);
        Assert.Contains(
            LoginRedirect.TheIdentityProviderFailed,
            arrived.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATokenAlreadyPastItsExpiryWhenItArrivesBuysNoSession()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();
        probe.Idp.TokenLifetime = -OidcLoginPolicy.Default.ClockSkew - TimeSpan.FromMinutes(1);

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("owner"));

        Assert.Empty(probe.Sessions.Sessions);
        Assert.Contains(
            LoginRedirect.TheIdentityProviderFailed,
            arrived.Headers.Location!.ToString(),
            StringComparison.Ordinal);
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
    public async Task TheProviderIsSentOneRedirectUriHoweverEachHalfOfTheHandshakeArrived()
    {
        await using PublicOriginProbe probe =
            PublicOriginProbe.Named(PublicOriginProbe.PublicAddress).Configured();

        using HttpResponseMessage started = await probe.StartAsync();
        var authorize = new Uri(started.Headers.Location!.ToString());
        string code = probe.Idp.Authorize(authorize, new MockIdentityUser("someone"));

        using HttpResponseMessage back = await probe.CallbackFromInsideAsync(
            MockIdentityProvider.StateOf(authorize),
            code,
            started.Headers.GetValues("Set-Cookie").First().Split(';')[0]);

        Assert.Single(probe.Sessions.Sessions);
        Assert.Equal(AuthMethod.Oidc, probe.Sessions.Sessions[0].Method);
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

    private static bool NeverStored(HttpResponseMessage response)
        => response.Headers.CacheControl?.NoStore is true;

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

    private void Heard(IServiceCollection services)
    {
        services.AddSingleton<ILoggerProvider>(log);
        services.AddSingleton<IStartupFilter>(new ArrivingFrom(IPAddress.Parse(Caller)));
    }

    private IReadOnlyList<SaidLine> Written() => log.By<SignInRecord>();
}
