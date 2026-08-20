using System.Net;
using System.Net.Http.Json;

using Carina.Api.Authentication;
using Carina.Api.Services;
using Carina.Domain.Auth;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class LocalLoginTests
{
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

        Assert.Equal(started.Id.Value, carried);
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
}
