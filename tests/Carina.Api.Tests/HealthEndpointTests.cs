using System.Net;
using System.Net.Http.Json;

namespace Carina.Api.Tests;

public sealed class HealthEndpointTests(CarinaApiFactory factory) : IClassFixture<CarinaApiFactory>
{
    [Fact]
    public async Task HealthIsServedWithoutAuthentication()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("ok", payload?.Status);
    }
}
