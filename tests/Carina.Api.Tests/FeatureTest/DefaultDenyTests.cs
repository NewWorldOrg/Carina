using System.Net;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class DefaultDenyTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("/api/driver/status")]
    [InlineData("/api/does-not-exist")]
    public async Task EverythingButHealthIsDeniedWithoutAuthentication(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheDocumentIsHandedOutWithoutCredentialsInDevelopmentBecauseTheClientIsGeneratedFromIt()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(ServedOpenApi.Route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheDocumentIsBehindTheSeamOutsideDevelopment()
    {
        using var deployed = new TestingWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.UseEnvironment(Environments.Production)
        );
        using var client = deployed.CreateClient();

        using var response = await client.GetAsync(new Uri(ServedOpenApi.Route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }
}
