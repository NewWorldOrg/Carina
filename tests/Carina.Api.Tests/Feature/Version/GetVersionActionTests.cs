using System.Net;
using System.Text.Json;

using Carina.Api.Common;
using Carina.Api.Tests.Support;

namespace Carina.Api.Tests.Feature.Version;

[Trait("Category", "Feature")]
public sealed class GetVersionActionTests(TestingWebApplicationFactory factory)
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
