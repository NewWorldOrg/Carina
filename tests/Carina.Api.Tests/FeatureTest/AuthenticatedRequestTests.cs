using System.Net;
using System.Text.Json;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class AuthenticatedRequestTests
{
    [Fact]
    public async Task AnAuthenticatedRequestReachesTheWorkedExample()
    {
        using HttpClient client = new TestingWebApplicationFactory().CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/driver/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.True(body.RootElement.GetProperty("status").GetBoolean());
        Assert.Equal(
            "notConnected",
            body.RootElement.GetProperty("data").GetProperty("connection").GetString()
        );
    }

    [Fact]
    public async Task AnUnauthenticatedRequestIsStillDeniedWhenASchemeExists()
    {
        using HttpClient client = new TestingWebApplicationFactory().WithTestScheme().CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/driver/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
