using System.Net;
using System.Text.Json;

using Carina.Api.Common;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class VersionEndpointTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private static readonly Uri TheSurface = new("/api/version", UriKind.Relative);

    [Fact]
    public async Task TheApplicationAnswersWithTheVersionItWasBuiltAs()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(TheSurface);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            DeclaredVersion.Of(typeof(DeclaredVersion).Assembly),
            document.RootElement.GetProperty("data").GetProperty("version").GetString());
    }
}
