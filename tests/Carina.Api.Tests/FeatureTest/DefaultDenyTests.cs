using System.Net;

namespace Carina.Api.Tests.FeatureTest;

public sealed class DefaultDenyTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("/api/driver/status")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/api/does-not-exist")]
    public async Task EverythingButHealthIsDeniedWithoutAuthentication(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }
}
