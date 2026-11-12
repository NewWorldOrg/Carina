using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Carina.Api.Authentication;
using Carina.Api.Services;
using Carina.Api.Tests.FeatureTest;
using Carina.Api.Tests.Unit;
using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Feature.Auth;

[Trait("Category", "Feature")]
public sealed class LogInActionTests
{
    private const string NotTheOneOnTheAccount = "not the one on the account";

    private const string Caller = "203.0.113.77";

    private static readonly Uri Me = new("/api/auth/me", UriKind.Relative);

    private static readonly Uri Password = new("/api/auth/password", UriKind.Relative);

    private static readonly Uri Logout = new("/api/auth/logout", UriKind.Relative);

    private static readonly Uri Login = new("/api/auth/login", UriKind.Relative);

    private static readonly Uri Tuners = new("/api/tuners", UriKind.Relative);

    private readonly RecordedStartup log = new();

    [Fact]
    public async Task TheRightCredentialsHandBackASessionCookieAScriptCannotRead()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpResponseMessage response = await probe.LogInAsync(FirstCredentials.Username, AuthProbe.Password);
        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith($"{SessionCookie.Name}=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OverHttpsTheCookieKeepsTheOneNameAndTakesTheSecureFlag()
    {
        await using AuthProbe probe = AuthProbe.OverHttps().WithAnAccount();

        using HttpResponseMessage response = await probe.LogInAsync(FirstCredentials.Username, AuthProbe.Password);
        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith($"{SessionCookie.Name}=", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheCookieCarriesNothingButAnOpaqueIdentifier()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpResponseMessage response = await probe.LogInAsync(FirstCredentials.Username, AuthProbe.Password);
        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        AuthSession started = probe.Sessions.Sessions[^1];

        string carried = cookie[$"{SessionCookie.Name}=".Length..cookie.IndexOf(';', StringComparison.Ordinal)];

        Assert.Equal(started.Handle, SessionHandle.Of(new SessionId(carried)));
        Assert.DoesNotContain(FirstCredentials.Username, carried, StringComparison.Ordinal);
        Assert.DoesNotContain(AuthProbe.Password, carried, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownUsernameAndAWrongPasswordAreAnsweredWithTheVerySameThing()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpResponseMessage wrongPassword = await probe.LogInAsync(
            FirstCredentials.Username,
            "not the password");
        string wrongPasswordBody = await wrongPassword.Content.ReadAsStringAsync();

        using HttpResponseMessage unknownName = await probe.LogInAsync("nobody-by-that-name", "not the password");
        string unknownNameBody = await unknownName.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(wrongPassword.StatusCode, unknownName.StatusCode);
        Assert.Equal(wrongPasswordBody, unknownNameBody);
        Assert.Contains(LocalAccountService.TheSameRefusalForEveryBadLogin, wrongPasswordBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedLoginHandsBackNoCookieAtAll()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpResponseMessage response = await probe.LogInAsync(FirstCredentials.Username, "not the password");

        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Empty(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task LoggingInBeforeAnyAccountExistsIsRefusedTheSameWayAsAWrongPassword()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();

        using HttpResponseMessage response = await probe.LogInAsync(FirstCredentials.Username, AuthProbe.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(
            LocalAccountService.TheSameRefusalForEveryBadLogin,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongTriesBeyondThePolicyAreHeldOffAndSayWhenToComeBack()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        for (int attempt = 0; attempt < LoginRatePolicy.Default.FailuresBeforeRefusing; attempt++)
        {
            using HttpResponseMessage refused = await probe.LogInAsync(
                FirstCredentials.Username,
                "not the password");

            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        using HttpResponseMessage response = await probe.LogInAsync(FirstCredentials.Username, AuthProbe.Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter?.Delta);
        Assert.Empty(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task ALoginWithoutAJsonBodyIsRefusedBeforeItCountsAsATry()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using var form = new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("username", FirstCredentials.Username)]);
        using HttpResponseMessage response = await probe.Client.PostAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task ALoginFromAnotherSiteIsRefusedBeforeItReachesTheAccount()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        probe.Client.DefaultRequestHeaders.Remove("Origin");
        probe.Client.DefaultRequestHeaders.Add("Origin", "http://somewhere-else.example");

        using HttpResponseMessage response = await probe.Client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username = FirstCredentials.Username, password = AuthProbe.Password });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(probe.Sessions.Sessions);
    }

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
    public async Task OnlyTheCookieHandedOutOverHttpsIsMarkedSecure()
    {
        await using AuthProbe overHttp = AuthProbe.OverHttp().WithAnAccount();
        await using AuthProbe overHttps = AuthProbe.OverHttps().WithAnAccount();

        using HttpResponseMessage plain = await overHttp.LogInAsync(FirstCredentials.Username, AuthProbe.Password);
        using HttpResponseMessage secure = await overHttps.LogInAsync(FirstCredentials.Username, AuthProbe.Password);

        Assert.DoesNotContain("secure", AttributesOf(Handed(plain)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", AttributesOf(Handed(secure)), StringComparison.OrdinalIgnoreCase);
    }

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

    [Fact]
    public async Task ANamedProxySayingHttpsMakesTheSessionCookieSecureUnderTheOneName()
    {
        await using ForwardedProxyProbe probe = ForwardedProxyProbe.TrustingItsProxy();

        using HttpResponseMessage response = await probe.LogInAsync();
        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith($"{SessionCookie.Name}=", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheOneRefusalStillAnsweringWith401AndABodyIsTheOneOutsideTheGate()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpResponseMessage refused = await probe.LogInAsync(
            FirstCredentials.Username,
            NotTheOneOnTheAccount);

        JsonNode document = await ServedOpenApi.FetchAsync(probe.Wired);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.NotEmpty(await Said(refused));
        Assert.Empty(ContractAgreement.Disagreements(
            document,
            "/api/auth/login",
            HttpMethods.Post,
            refused.StatusCode,
            await refused.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task TheLocalAccountStillOpensASessionWhileTheProviderIsOutOfReach()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured().WithALocalAccount();
        probe.Idp.Reachable = false;

        using HttpResponseMessage signedIn = await probe.LogInAsync();
        using HttpResponseMessage named = await probe.Client.GetAsync(
            new Uri("/api/auth/me", UriKind.Relative));
        using var body = JsonDocument.Parse(await named.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, named.StatusCode);
        Assert.Equal(
            FirstCredentials.Username,
            body.RootElement.GetProperty("data").GetProperty("subject").GetString());
        Assert.Equal("local", body.RootElement.GetProperty("data").GetProperty("method").GetString());
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

    [Fact]
    public async Task AnAppStartingWithNoAccountMakesOneAndTheWordItWroteDownOpensIt()
    {
        var log = new RecordedStartup();
        var accounts = new HeldLocalAccount();

        await using var factory = new TestingWebApplicationFactory();
        using HttpClient client = Started(factory, log, accounts);

        await Eventually.Happens(
            () => log.By<LocalAccountBootstrap>(LogLevel.Warning).Count > 0,
            "the app writes down the credentials it made for itself");

        SaidLine written = Assert.Single(log.By<LocalAccountBootstrap>(LogLevel.Warning));

        using HttpResponseMessage signedIn = await client.PostAsJsonAsync(
            Login,
            new { username = written.Only("Username"), password = written.Only("Password") });

        Assert.Equal(FirstCredentials.Username, written.Only("Username"));
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal(1, accounts.Saves);
    }

    [Fact]
    public async Task AnAppThatCouldNotMakeThatAccountRefusesCallersRatherThanLettingThemPast()
    {
        var log = new RecordedStartup();

        await using var factory = new TestingWebApplicationFactory();
        using HttpClient client = Started(factory, log, new RefusingLocalAccounts());

        await Eventually.Happens(
            () => log.By<LocalAccountBootstrap>(LogLevel.Error).Count > 0,
            "the app says nobody can sign in");

        using HttpResponseMessage refused = await client.GetAsync(Tuners);
        using HttpResponseMessage attempted = await client.PostAsJsonAsync(
            Login,
            new { username = FirstCredentials.Username, password = "anything at all" });

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, attempted.StatusCode);
        Assert.DoesNotContain(
            attempted.Headers,
            header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase));
    }

    private static string Handed(HttpResponseMessage response)
        => Assert.Single(response.Headers.GetValues(HeaderNames.SetCookie));

    private static string NameOf(string cookie) => cookie[..cookie.IndexOf('=', StringComparison.Ordinal)];

    private static string AttributesOf(string cookie) => cookie[cookie.IndexOf(';', StringComparison.Ordinal)..];

    private static bool NeverStored(HttpResponseMessage response)
        => response.Headers.CacheControl?.NoStore is true;

    private static async Task<string> Said(HttpResponseMessage response)
        => JsonNode.Parse(await response.Content.ReadAsStringAsync())!["message"]!.GetValue<string>();

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

    private AuthProbe Probe() => AuthProbe.OverHttp(Heard);

    private void Heard(IServiceCollection services)
    {
        services.AddSingleton<ILoggerProvider>(log);
        services.AddSingleton<IStartupFilter>(new ArrivingFrom(IPAddress.Parse(Caller)));
    }

    private IReadOnlyList<SaidLine> Written() => log.By<SignInRecord>();

    private static HttpClient Started(
        TestingWebApplicationFactory factory,
        RecordedStartup log,
        ILocalAccountRepository accounts)
        => factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILoggerProvider>(log);
                services.AddSingleton(accounts);
                services.AddSingleton<IAuthSessionRepository>(new HeldAuthSessions());
                services.AddSingleton<IPasswordHasher>(new QuickPasswordHasher());
            }))
            .CreateClient();
}
