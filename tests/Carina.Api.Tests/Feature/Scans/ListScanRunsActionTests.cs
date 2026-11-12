using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;

namespace Carina.Api.Tests.Feature.Scans;

[Trait("Category", "Feature")]
public sealed class ListScanRunsActionTests
{
    [Fact]
    public async Task PastRunsAreListedNewestFirstWithHowTheyEnded()
    {
        await using var feature = new ScanFeature();

        Guid first = await feature.StartAsync();
        await feature.UntilSettled(first);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/tuners/scan-runs");
        JsonElement runs = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, runs.GetArrayLength());
        Assert.Equal(first, runs[0].GetProperty("scanId").GetGuid());
        Assert.Equal("completed", runs[0].GetProperty("state").GetString());
    }

    [Fact]
    public async Task EveryScanSurfaceIsBehindTheSameDenialAsTheRestOnceASchemeIsRegistered()
    {
        using var app = new TestingWebApplicationFactory();
        using HttpClient client = app.WithTestScheme().CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync(new Uri("/api/tuners/scan-runs", UriKind.Relative))).StatusCode);
    }
}
