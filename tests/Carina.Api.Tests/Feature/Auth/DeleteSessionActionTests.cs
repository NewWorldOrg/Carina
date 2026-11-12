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
public sealed class DeleteSessionActionTests
{
    private const string Caller = "203.0.113.77";

    private const string Tablet = "a tablet that signed in a while ago";

    private const string Desk = "a desk that is asking";

    private const string Phone = "a phone that came through the provider";

    private const string Replacement = "a replacement password";

    private const string ProviderSubject = "the-subject-at-the-provider";

    private const string ProviderName = "somebody@example.test";

    private static readonly Uri Me = new("/api/auth/me", UriKind.Relative);

    private static readonly Uri Sessions = new("/api/auth/sessions", UriKind.Relative);

    private static readonly Uri Password = new("/api/auth/password", UriKind.Relative);

    private readonly RecordedStartup log = new();

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
    public async Task EndingAnotherDeviceRefusesThatDeviceAndLeavesThisOneSignedIn()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();

        using HttpClient there = await probe.RelayingAsync();
        using HttpClient here = await probe.RelayingAsync();
        SessionHandle ended = probe.Sessions.Sessions[0].Handle;

        using var asking = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri($"/api/auth/sessions/{ended.Value}", UriKind.Relative))
        {
            Content = AuthProbe.Json(),
        };

        using HttpResponseMessage revoked = await here.SendAsync(asking);
        using HttpResponseMessage refused = await there.GetAsync(Me);
        using HttpResponseMessage answered = await here.GetAsync(Me);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
    }

    [Fact(DisplayName = "BR-AU-020: ending another device is written down with the device that asked and the one that was ended")]
    public async Task BrAu020EndingAnotherDeviceIsWrittenDownWithTheDeviceThatAskedAndTheOneThatWasEnded()
    {
        await using AuthProbe probe = Probe().WithAnAccount();
        using HttpClient desk = Carrying(probe, probe.Sitting(Desk));
        AuthSession phone = ThroughTheProvider(probe);

        using HttpResponseMessage ended = await EndAsync(desk, phone);

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);

        SaidLine said = Assert.Single(Written());

        Assert.Equal(nameof(SignInMoment.TheSessionRevokedAnother), said.Only("Moment"));
        Assert.Equal("/api/auth/sessions/{id}", said.Only("Route"));
        Assert.Equal(nameof(AuthMethod.Local), said.Only("SignInMethod"));
        Assert.Equal(Desk, said.Only("Device"));
        Assert.Equal("1", said.Only("Ended"));
        Assert.Equal(nameof(AuthMethod.Oidc), said.Only("EndedSignInMethod"));
        Assert.Equal(Phone, said.Only("EndedDevice"));
    }

    [Fact(DisplayName = "BR-AU-020: a device ending its own session is written down as ending itself, apart from ending another")]
    public async Task BrAu020ADeviceEndingItsOwnSessionIsWrittenDownAsEndingItself()
    {
        await using AuthProbe probe = Probe().WithAnAccount();
        AuthSession sitting = probe.Sitting(Desk);
        using HttpClient desk = Carrying(probe, sitting);

        using HttpResponseMessage ended = await EndAsync(desk, sitting);

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);

        SaidLine said = Assert.Single(Written());

        Assert.Equal(nameof(SignInMoment.TheSessionRevokedItself), said.Only("Moment"));
        Assert.Equal(Desk, said.Only("Device"));
        Assert.Equal("1", said.Only("Ended"));
        Assert.Equal(Desk, said.Only("EndedDevice"));
    }

    [Fact(DisplayName = "BR-AU-020: asking to end a session that does not exist writes nothing down")]
    public async Task BrAu020AskingToEndASessionThatDoesNotExistWritesNothingDown()
    {
        await using AuthProbe probe = Probe().WithAnAccount();
        using HttpClient desk = Carrying(probe, probe.Sitting(Desk));

        using var asking = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri($"/api/auth/sessions/{SessionHandle.Of(SessionId.Issue()).Value}", UriKind.Relative))
        {
            Content = AuthProbe.Json(),
        };
        using HttpResponseMessage ended = await desk.SendAsync(asking);

        Assert.Equal(HttpStatusCode.NotFound, ended.StatusCode);
        Assert.Empty(Written());
    }

    [Fact(DisplayName = "BR-AU-020: nothing that ending a session or changing the password carries is in any line: not either password, a cookie, a handle, a name or the address")]
    public async Task BrAu020NothingThatEndingASessionOrChangingThePasswordCarriesIsInAnyLine()
    {
        await using AuthProbe probe = Probe().WithAnAccount();
        AuthSession sitting = probe.Sitting(Desk);
        using HttpClient desk = Carrying(probe, sitting);
        AuthSession phone = ThroughTheProvider(probe);
        AuthSession tablet = probe.Sitting(Tablet);

        using HttpResponseMessage ended = await EndAsync(desk, phone);
        using HttpResponseMessage changed = await desk.PostAsJsonAsync(
            Password,
            new { currentPassword = AuthProbe.Password, newPassword = Replacement });
        using HttpResponseMessage endedItself = await EndAsync(desk, sitting);

        Assert.Equal(HttpStatusCode.NoContent, endedItself.StatusCode);
        Assert.Equal(
            [
                nameof(SignInMoment.TheSessionRevokedAnother),
                nameof(SignInMoment.AChangedPasswordRevokedTheOthers),
                nameof(SignInMoment.TheSessionRevokedItself),
            ],
            Written().Select(said => said.Only("Moment")));
        NothingSays(
            log.Everything,
            AuthProbe.Password,
            Replacement,
            probe.Sessions.CookieOf(sitting).Value,
            probe.Sessions.CookieOf(phone).Value,
            probe.Sessions.CookieOf(tablet).Value,
            sitting.Handle.Value,
            phone.Handle.Value,
            tablet.Handle.Value);
        NothingSays(Written(), FirstCredentials.Username, ProviderSubject, ProviderName, Caller);
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

    private static HttpClient Carrying(AuthProbe probe, AuthSession session)
        => probe.Relaying($"{SessionCookie.Name}={probe.Sessions.CookieOf(session).Value}");

    private static AuthSession ThroughTheProvider(AuthProbe probe)
    {
        SessionId cookie = SessionId.Issue();

        return probe.Sessions.Seat(
            cookie,
            AuthSession.Start(
                cookie,
                new Subject(ProviderSubject),
                ProviderName,
                AuthMethod.Oidc,
                Phone,
                DateTime.UtcNow));
    }

    private static async Task<HttpResponseMessage> EndAsync(HttpClient asking, AuthSession ended)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri($"/api/auth/sessions/{ended.Handle.Value}", UriKind.Relative))
        {
            Content = AuthProbe.Json(),
        };

        return await asking.SendAsync(request);
    }

    private AuthProbe Probe() => AuthProbe.OverHttp(Heard);

    private void Heard(IServiceCollection services)
    {
        services.AddSingleton<ILoggerProvider>(log);
        services.AddSingleton<IStartupFilter>(new ArrivingFrom(IPAddress.Parse(Caller)));
    }

    private IReadOnlyList<SaidLine> Written() => log.By<SignInRecord>();
}
