using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Authentication;
using Carina.Domain.Auth;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class SessionEndpointTests
{
    private static readonly Uri Me = new("/api/auth/me", UriKind.Relative);

    private static readonly Uri Sessions = new("/api/auth/sessions", UriKind.Relative);

    private static readonly Uri Logout = new("/api/auth/logout", UriKind.Relative);

    private static readonly Uri Password = new("/api/auth/password", UriKind.Relative);

    [Fact]
    public async Task TheCookieHandedOutAtLoginNamesTheAccountBackAtTheCallerAfterwards()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();

        using HttpResponseMessage response = await probe.Client.GetAsync(Me);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            FirstCredentials.Username,
            body.RootElement.GetProperty("data").GetProperty("subject").GetString());
        Assert.Equal("local", body.RootElement.GetProperty("data").GetProperty("method").GetString());
    }

    [Fact]
    public async Task WithoutTheCookieTheAccountIsNotNamedAtAll()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpResponseMessage response = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

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

        probe.Client.DefaultRequestHeaders.Add("Cookie", $"{probe.CookieName}=not-a-session-id");

        using HttpResponseMessage response = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ACookieSpeltWithTheHostPrefixNamesTheAccountOnARequestThatArrivedOverPlainHttp()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();
        AuthSession session = probe.Sitting("a device that signed in elsewhere");

        probe.Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.HostName}={session.Id.Value}");

        using HttpResponseMessage response = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ACookieSpeltWithoutThePrefixNamesTheAccountOnARequestThatArrivedOverHttps()
    {
        await using AuthProbe probe = AuthProbe.OverHttps().WithAnAccount();
        AuthSession session = probe.Sitting("a device that signed in elsewhere");

        probe.Client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.PlainName}={session.Id.Value}");

        using HttpResponseMessage response = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WithBothSpellingsInFlightThePrefixedOneNamesTheSessionThatIsAsking()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();
        AuthSession vouched = probe.Sitting("the device the prefixed cookie was issued to");
        AuthSession weaker = probe.Sitting("the device the unprefixed cookie was issued to");

        probe.Client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{SessionCookie.PlainName}={weaker.Id.Value}; {SessionCookie.HostName}={vouched.Id.Value}");

        using HttpResponseMessage response = await probe.Client.GetAsync(Sessions);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement listed = body.RootElement.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(Only(listed, vouched.Id).GetProperty("current").GetBoolean());
        Assert.False(Only(listed, weaker.Id).GetProperty("current").GetBoolean());
    }

    [Fact]
    public async Task TheSessionListMarksTheDeviceThatIsAskingAndShowsTheOthersBesideIt()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        AuthSession here = await probe.SignedInAsync();
        AuthSession there = probe.Sitting("another device");

        using HttpResponseMessage response = await probe.Client.GetAsync(Sessions);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement listed = body.RootElement.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, listed.GetArrayLength());
        Assert.True(Only(listed, here.Id).GetProperty("current").GetBoolean());
        Assert.False(Only(listed, there.Id).GetProperty("current").GetBoolean());
        Assert.Equal("another device", Only(listed, there.Id).GetProperty("deviceLabel").GetString());
    }

    [Fact]
    public async Task EndingAnotherDeviceLeavesThisOneSignedIn()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();
        AuthSession there = probe.Sitting("another device");

        using HttpResponseMessage ended = await EndingAsync(probe, there.Id);
        using HttpResponseMessage after = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.Equal(SessionStatus.Revoked, there.StatusAt(DateTime.UtcNow, SessionPolicy.Default));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task ASessionIdThatIsNotOnThisAccountIsNotFound()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();

        AuthSession theirs = AuthSession.Start(
            SessionId.Issue(),
            new Subject("somebody-else"),
            AuthMethod.Local,
            "a stranger's device",
            DateTime.UtcNow);

        probe.Sessions.Sessions.Add(theirs);

        using HttpResponseMessage response = await EndingAsync(probe, theirs.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SessionStatus.Active, theirs.StatusAt(DateTime.UtcNow, SessionPolicy.Default));
    }

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
        Assert.StartsWith($"{SessionCookie.PlainName}=;", cookie, StringComparison.Ordinal);
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
    public async Task ChangingThePasswordEndsEveryOtherDeviceAndLeavesThisOneSignedIn()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();
        AuthSession there = probe.Sitting("another device");
        AuthSession elsewhere = probe.Sitting("a third device");

        using HttpResponseMessage response = await probe.Client.PostAsJsonAsync(
            Password,
            new { currentPassword = AuthProbe.Password, newPassword = "a replacement password" });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        using HttpResponseMessage after = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, body.RootElement.GetProperty("data").GetProperty("sessionsEnded").GetInt32());
        Assert.Equal(SessionStatus.Revoked, there.StatusAt(DateTime.UtcNow, SessionPolicy.Default));
        Assert.Equal(SessionStatus.Revoked, elsewhere.StatusAt(DateTime.UtcNow, SessionPolicy.Default));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task ChangingThePasswordWithTheWrongCurrentOneEndsNothing()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();
        AuthSession there = probe.Sitting("another device");

        using HttpResponseMessage response = await probe.Client.PostAsJsonAsync(
            Password,
            new { currentPassword = "not the password", newPassword = "a replacement password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SessionStatus.Active, there.StatusAt(DateTime.UtcNow, SessionPolicy.Default));
    }

    [Fact]
    public async Task AReplacementPasswordTooShortToBeWorthHavingIsRefused()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();

        using HttpResponseMessage response = await probe.Client.PostAsJsonAsync(
            Password,
            new { currentPassword = AuthProbe.Password, newPassword = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NoneOfTheSessionSurfacesAnswerACallerWithoutACookie()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpResponseMessage listed = await probe.Client.GetAsync(Sessions);
        using HttpResponseMessage signedOut = await probe.Client.PostAsJsonAsync(Logout, new { });
        using HttpResponseMessage changed = await probe.Client.PostAsJsonAsync(
            Password,
            new { currentPassword = AuthProbe.Password, newPassword = "a replacement password" });

        Assert.Equal(HttpStatusCode.Unauthorized, listed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, changed.StatusCode);
    }

    private static async Task<HttpResponseMessage> EndingAsync(AuthProbe probe, SessionId id)
    {
        using var asking = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri($"/api/auth/sessions/{id.Value}", UriKind.Relative))
        {
            Content = AuthProbe.Json(),
        };

        return await probe.Client.SendAsync(asking);
    }

    private static JsonElement Only(JsonElement listed, SessionId id)
        => listed.EnumerateArray().Single(session => session.GetProperty("id").GetString() == id.Value);
}
