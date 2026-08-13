using System.Net;
using System.Net.Http.Json;

using Carina.Api.Responder.Health;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class HealthEndpointTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Fact]
    public async Task HealthIsServedWithoutAuthentication()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HealthResponder>();
        Assert.Equal("ok", payload?.Status);
    }
}
