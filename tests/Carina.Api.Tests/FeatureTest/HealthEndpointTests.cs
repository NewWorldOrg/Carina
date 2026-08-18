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
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        HealthResponder? payload = await response.Content.ReadFromJsonAsync<HealthResponder>();
        Assert.Equal("ok", payload?.Status);
    }
}
