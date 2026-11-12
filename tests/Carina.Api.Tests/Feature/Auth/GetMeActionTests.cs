using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Authentication;
using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Auth;

namespace Carina.Api.Tests.Feature.Auth;

[Trait("Category", "Feature")]
public sealed class GetMeActionTests
{
    private static readonly Uri Me = new("/api/auth/me", UriKind.Relative);

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
}
