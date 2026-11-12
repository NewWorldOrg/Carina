using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Carina.Api.Authentication;
using Carina.Api.Tests.FeatureTest;
using Carina.Api.Tests.Unit;
using Carina.Domain.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Feature.Auth;

[Trait("Category", "Feature")]
public sealed class ChangePasswordActionTests
{
    private const string PasswordPath = "/api/auth/password";

    private const string NotTheOneOnTheAccount = "not the one on the account";

    private const string AReplacementLongEnough = "a replacement long enough";

    private const string TooShortToBeWorthHaving = "short";

    private const string Caller = "203.0.113.77";

    private const string Tablet = "a tablet that signed in a while ago";

    private const string Desk = "a desk that is asking";

    private const string Replacement = "a replacement password";

    private static readonly Uri Me = new("/api/auth/me", UriKind.Relative);

    private static readonly Uri Password = new("/api/auth/password", UriKind.Relative);

    private static readonly Uri Endpoint = new(PasswordPath, UriKind.Relative);

    private readonly RecordedStartup log = new();

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
    public async Task ChangingThePasswordRefusesEveryOtherDeviceAndLeavesThisOneSignedIn()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();

        using HttpClient there = await probe.RelayingAsync();
        using HttpClient here = await probe.RelayingAsync();

        using HttpResponseMessage changed = await here.PostAsJsonAsync(
            Password,
            new { currentPassword = AuthProbe.Password, newPassword = "a replacement password" });
        using HttpResponseMessage refused = await there.GetAsync(Me);
        using HttpResponseMessage answered = await here.GetAsync(Me);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
    }

    [Fact]
    public async Task EveryWayThePasswordChangeCanEndIsAnAnswerTheDocumentDescribesAndShapesTheSameWay()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();

        using HttpResponseMessage withoutASession = await Refused(probe);

        await probe.SignedInAsync();

        using HttpResponseMessage wrongCurrentOne = await Asking(probe, NotTheOneOnTheAccount, AReplacementLongEnough);
        using HttpResponseMessage tooWeakAReplacement = await Asking(probe, AuthProbe.Password, TooShortToBeWorthHaving);
        using HttpResponseMessage changed = await Asking(probe, AuthProbe.Password, AReplacementLongEnough);

        JsonNode document = await ServedOpenApi.FetchAsync(probe.Wired);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutASession.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrentOne.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooWeakAReplacement.StatusCode);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotEmpty(await Said(wrongCurrentOne));
        Assert.Equal("wrongPassword", await RefusalAsync(wrongCurrentOne));
        Assert.Equal("outOfLength", await RefusalAsync(tooWeakAReplacement));
        Assert.Equal(12, await LengthAsync(tooWeakAReplacement, "shortestLength"));
        Assert.Equal(256, await LengthAsync(tooWeakAReplacement, "longestLength"));
        Assert.Empty(
            await DisagreementsAsync(
                document,
                withoutASession,
                wrongCurrentOne,
                tooWeakAReplacement,
                changed));
    }

    [Fact(DisplayName = "BR-AU-020: a changed password is written down with the device that changed it and how many others it ended")]
    public async Task BrAu020AChangedPasswordIsWrittenDownWithTheDeviceThatChangedItAndHowManyOthersItEnded()
    {
        await using AuthProbe probe = Probe().WithAnAccount();
        using HttpClient desk = Carrying(probe, probe.Sitting(Desk));

        probe.Sitting(Tablet);
        probe.Sitting(Tablet);

        using HttpResponseMessage changed = await desk.PostAsJsonAsync(
            Password,
            new { currentPassword = AuthProbe.Password, newPassword = Replacement });

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        SaidLine said = Assert.Single(Written());

        Assert.Equal(nameof(SignInMoment.AChangedPasswordRevokedTheOthers), said.Only("Moment"));
        Assert.Equal("/api/auth/password", said.Only("Route"));
        Assert.Equal(nameof(AuthMethod.Local), said.Only("SignInMethod"));
        Assert.Equal(Desk, said.Only("Device"));
        Assert.Equal("2", said.Only("Ended"));
        Assert.Equal(SignInRecord.Unsaid, said.Only("EndedSignInMethod"));
        Assert.Equal(SignInRecord.Unsaid, said.Only("EndedDevice"));
    }

    [Fact(DisplayName = "BR-AU-020: a password change that is refused writes nothing down")]
    public async Task BrAu020APasswordChangeThatIsRefusedWritesNothingDown()
    {
        await using AuthProbe probe = Probe().WithAnAccount();
        using HttpClient desk = Carrying(probe, probe.Sitting(Desk));

        using HttpResponseMessage refused = await desk.PostAsJsonAsync(
            Password,
            new { currentPassword = "not the password at all", newPassword = Replacement });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Empty(Written());
    }

    private static async Task<HttpResponseMessage> Refused(AuthProbe probe)
    {
        probe.WithAnAccount();

        return await Asking(probe, AuthProbe.Password, AReplacementLongEnough);
    }

    private static Task<HttpResponseMessage> Asking(AuthProbe probe, string current, string replacement)
        => probe.Client.PostAsJsonAsync(
            Endpoint,
            new { currentPassword = current, newPassword = replacement });

    private static async Task<string> Said(HttpResponseMessage response)
        => JsonNode.Parse(await response.Content.ReadAsStringAsync())!["message"]!.GetValue<string>();

    private static async Task<string> RefusalAsync(HttpResponseMessage response)
        => JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]!["refusal"]!.GetValue<string>();

    private static async Task<int> LengthAsync(HttpResponseMessage response, string bound)
        => JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]![bound]!.GetValue<int>();

    private static async Task<IReadOnlyList<string>> DisagreementsAsync(
        JsonNode document,
        params HttpResponseMessage[] answers)
    {
        var found = new List<string>();

        foreach (HttpResponseMessage answer in answers)
        {
            found.AddRange(ContractAgreement.Disagreements(
                document,
                PasswordPath,
                HttpMethods.Post,
                answer.StatusCode,
                await answer.Content.ReadAsStringAsync()));
        }

        return found;
    }

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
