using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class RebuildEpgEndpointTests
{
    [Fact]
    public async Task DiscardingTheWholeGuideNeedsTheWordThatMeansIt()
    {
        await using var feature = new EpgFeature();

        (HttpStatusCode status, _) = await feature.PostAsync("/api/epg/rebuild");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(0, feature.Programmes.Wiped);
    }

    [Fact]
    public async Task AWordThatIsNotTheOneAskedForIsNotEnough()
    {
        await using var feature = new EpgFeature();

        (HttpStatusCode status, _) = await feature.PostAsync(
            "/api/epg/rebuild",
            new { confirm = "yes" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(0, feature.Programmes.Wiped);
    }

    [Fact]
    public async Task ConfirmedRebuildDiscardsTheGuideAndMovesTheGenerationOn()
    {
        await using var feature = new EpgFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/epg/rebuild",
            new { confirm = "discard-everything" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, feature.Programmes.Wiped);
        Assert.Equal(2, body.GetProperty("data").GetProperty("generation").GetInt32());
    }

    [Fact]
    public async Task EachRebuildMovesTheGenerationOnAgain()
    {
        await using var feature = new EpgFeature();

        await feature.PostAsync("/api/epg/rebuild", new { confirm = "discard-everything" });

        (_, JsonElement body) = await feature.PostAsync(
            "/api/epg/rebuild",
            new { confirm = "discard-everything" });

        Assert.Equal(3, body.GetProperty("data").GetProperty("generation").GetInt32());
    }

    [Fact]
    public async Task AFormPostCannotDiscardTheGuide()
    {
        await using var feature = new EpgFeature();
        using var form = new StringContent("confirm=discard-everything", Encoding.UTF8);

        form.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using HttpResponseMessage response = await feature.Client.PostAsync(
            new Uri("/api/epg/rebuild", UriKind.Relative),
            form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(0, feature.Programmes.Wiped);
    }
}
