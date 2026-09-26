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
        Assert.True(Only(listed, here.Handle).GetProperty("current").GetBoolean());
        Assert.False(Only(listed, there.Handle).GetProperty("current").GetBoolean());
        Assert.Equal("another device", Only(listed, there.Handle).GetProperty("deviceLabel").GetString());
        Assert.Equal(FirstCredentials.Username, Only(listed, there.Handle).GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task BrAu018TheSessionListShowsEveryIdentitysSessionsAndSaysWhoseEachOneIs()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        AuthSession here = await probe.SignedInAsync();
        AuthSession theirs = SomebodyElseSitting(probe);

        using HttpResponseMessage response = await probe.Client.GetAsync(Sessions);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement listed = body.RootElement.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, listed.GetArrayLength());
        Assert.Equal("somebody@example.test", Only(listed, theirs.Handle).GetProperty("displayName").GetString());
        Assert.Equal("oidc", Only(listed, theirs.Handle).GetProperty("method").GetString());
        Assert.False(Only(listed, theirs.Handle).GetProperty("current").GetBoolean());
        Assert.Equal(FirstCredentials.Username, Only(listed, here.Handle).GetProperty("displayName").GetString());
        Assert.Equal("local", Only(listed, here.Handle).GetProperty("method").GetString());
        Assert.True(Only(listed, here.Handle).GetProperty("current").GetBoolean());
    }

    [Fact]
    public async Task TheSessionListNeverHandsOutTheCookieOfAnySessionItShows()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        AuthSession here = await probe.SignedInAsync();
        AuthSession there = probe.Sitting("another device");
        AuthSession theirs = SomebodyElseSitting(probe);

        using HttpResponseMessage response = await probe.Client.GetAsync(Sessions);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(probe.Sessions.CookieOf(here).Value, body, StringComparison.Ordinal);
        Assert.DoesNotContain(probe.Sessions.CookieOf(there).Value, body, StringComparison.Ordinal);
        Assert.DoesNotContain(probe.Sessions.CookieOf(theirs).Value, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHandleTheListShowsIsWhatEndsThatSession()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();
        AuthSession theirs = SomebodyElseSitting(probe);

        using HttpResponseMessage listing = await probe.Client.GetAsync(Sessions);
        using var body = JsonDocument.Parse(await listing.Content.ReadAsStringAsync());
        string handle = body.RootElement.GetProperty("data").EnumerateArray()
            .Single(session => !session.GetProperty("current").GetBoolean())
            .GetProperty("id").GetString()!;

        using HttpResponseMessage ended = await EndingAsync(probe, handle);

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.Equal(SessionStatus.Revoked, theirs.StatusAt(DateTime.UtcNow, SessionPolicy.Default));
    }

    [Fact]
    public async Task TheCookieOfASessionDoesNotEndIt()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();
        AuthSession theirs = SomebodyElseSitting(probe);

        using HttpResponseMessage response = await EndingAsync(probe, probe.Sessions.CookieOf(theirs).Value);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(SessionStatus.Active, theirs.StatusAt(DateTime.UtcNow, SessionPolicy.Default));
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
    public async Task EndingAnotherDeviceLeavesThisOneSignedIn()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();
        AuthSession there = probe.Sitting("another device");

        using HttpResponseMessage ended = await EndingAsync(probe, there.Handle);
        using HttpResponseMessage after = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.Equal(SessionStatus.Revoked, there.StatusAt(DateTime.UtcNow, SessionPolicy.Default));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task BrAu018SomebodyElsesSessionCanBeEndedFromHere()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();
        AuthSession theirs = SomebodyElseSitting(probe);

        using HttpResponseMessage response = await EndingAsync(probe, theirs.Handle);
        using HttpResponseMessage after = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(SessionStatus.Revoked, theirs.StatusAt(DateTime.UtcNow, SessionPolicy.Default));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task ASessionIdThatWasNeverIssuedIsNotFound()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();

        using HttpResponseMessage response = await EndingAsync(probe, SessionHandle.Of(SessionId.Issue()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EndingTheSessionThatIsAskingIsAnsweredOnceAndTheNextRequestIsRefusedAtTheGate()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        AuthSession here = await probe.SignedInAsync();

        using HttpResponseMessage ended = await EndingAsync(probe, here.Handle);
        using HttpResponseMessage after = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.False(ended.Headers.Contains("Set-Cookie"));
        Assert.Equal(SessionStatus.Revoked, here.StatusAt(DateTime.UtcNow, SessionPolicy.Default));
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
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
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(body.RootElement.GetProperty("status").GetBoolean());
        Assert.NotEmpty(body.RootElement.GetProperty("message").GetString()!);
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

    private static Task<HttpResponseMessage> EndingAsync(AuthProbe probe, SessionHandle handle)
        => EndingAsync(probe, handle.Value);

    private static async Task<HttpResponseMessage> EndingAsync(AuthProbe probe, string named)
    {
        using var asking = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri($"/api/auth/sessions/{named}", UriKind.Relative))
        {
            Content = AuthProbe.Json(),
        };

        return await probe.Client.SendAsync(asking);
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

    private static AuthSession SomebodyElseSitting(AuthProbe probe)
    {
        SessionId cookie = SessionId.Issue();

        return probe.Sessions.Seat(
            cookie,
            AuthSession.Start(
                cookie,
                new Subject("108204329581372"),
                "somebody@example.test",
                AuthMethod.Oidc,
                "a stranger's device",
                DateTime.UtcNow));
    }

    private static JsonElement Only(JsonElement listed, SessionHandle handle)
        => listed.EnumerateArray()
            .Single(session => session.GetProperty("id").GetString() == handle.Value);
}
