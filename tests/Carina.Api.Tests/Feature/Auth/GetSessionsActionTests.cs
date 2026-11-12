using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Authentication;
using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Auth;

namespace Carina.Api.Tests.Feature.Auth;

[Trait("Category", "Feature")]
public sealed class GetSessionsActionTests
{
    private static readonly Uri Sessions = new("/api/auth/sessions", UriKind.Relative);

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
