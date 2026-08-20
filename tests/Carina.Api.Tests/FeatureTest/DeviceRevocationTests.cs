using System.Net;
using System.Net.Http.Json;

using Carina.Domain.Auth;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class DeviceRevocationTests
{
    private static readonly Uri Me = new("/api/auth/me", UriKind.Relative);

    private static readonly Uri Password = new("/api/auth/password", UriKind.Relative);

    [Fact]
    public async Task EndingAnotherDeviceRefusesThatDeviceAndLeavesThisOneSignedIn()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();

        using HttpClient there = await probe.RelayingAsync();
        using HttpClient here = await probe.RelayingAsync();
        SessionId ended = probe.Sessions.Sessions[0].Id;

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
}
