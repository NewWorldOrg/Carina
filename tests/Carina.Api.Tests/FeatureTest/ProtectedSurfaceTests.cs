using System.Net;
using System.Text.Json;

using Carina.Api.Events;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class ProtectedSurfaceTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("GET", "/api/tuners")]
    [InlineData("PUT", "/api/tuners")]
    [InlineData("GET", "/api/tuners/detected")]
    [InlineData("PATCH", "/api/tuners/adapter0")]
    [InlineData("GET", "/api/tuners/scan-runs")]
    [InlineData("POST", "/api/tuners/scan")]
    [InlineData("GET", "/api/services")]
    [InlineData("GET", "/api/programs")]
    [InlineData("GET", "/api/programs/search")]
    [InlineData("GET", "/api/programs/1")]
    [InlineData("GET", "/api/epg/collection-status")]
    [InlineData("POST", "/api/epg/collect-now")]
    [InlineData("POST", "/api/epg/rebuild")]
    [InlineData("POST", "/api/epg/archive/forget-service")]
    [InlineData("GET", "/api/driver/status")]
    [InlineData("POST", "/api/driver/restart")]
    public async Task ASurfaceThatPredatesTheDenialRefusesACallerCarryingNoCredentials(
        string method,
        string path)
    {
        using HttpResponseMessage response = await AskAsync(method, path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData(AppEventStream.Path)]
    [InlineData(ProgrammeFeedStream.Path)]
    public async Task AConnectionMeantToStayOpenIsJudgedBeforeItIsOpened(string path)
    {
        using HttpResponseMessage response = await AskAsync("GET", path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("/api/programs/1/thumbnail")]
    [InlineData("/api/recordings/1/thumbnail.jpg")]
    [InlineData("/api/recordings/1/stream.ts")]
    [InlineData("/api/services/1-101/logo")]
    [InlineData("/_next/image")]
    [InlineData("/_next/data/build/programs.json")]
    public async Task ContentIsRefusedRatherThanTreatedAsSomethingTheBuildProduced(string path)
    {
        using HttpResponseMessage response = await AskAsync("GET", path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheOneSurfaceHandingBackDataWithoutCredentialsSaysOnlyWhetherItIsAlive()
    {
        using HttpResponseMessage response = await AskAsync("GET", "/api/health");
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["degraded", "status"],
            body.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    private async Task<HttpResponseMessage> AskAsync(string method, string path)
    {
        WebApplicationFactory<Program> guarded = factory.WithTestScheme();
        using HttpClient client = guarded.CreateClient();
        using var asking = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));

        return await client.SendAsync(asking, HttpCompletionOption.ResponseHeadersRead);
    }
}
