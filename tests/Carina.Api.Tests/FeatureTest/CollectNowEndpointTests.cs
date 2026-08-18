using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Carina.Domain.Channels;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class CollectNowEndpointTests
{
    [Fact]
    public async Task ABoostOverEverythingWalksEveryStreamOnOffer()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049]), Stream(4, 32_737, [1050])]);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/epg/collect-now");

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(2, body.GetProperty("data").GetProperty("streams").GetInt32());
        Assert.NotEqual(Guid.Empty, body.GetProperty("data").GetProperty("boostId").GetGuid());
    }

    [Fact]
    public async Task ABoostOverOneStreamWalksOnlyThatOne()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049]), Stream(4, 32_737, [1050])]);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/epg/collect-now",
            new { networkId = 4, transportStreamId = 32_737 });

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("streams").GetInt32());
    }

    [Fact]
    public async Task ABoostNamingAServiceWalksTheStreamCarryingIt()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049]), Stream(4, 32_737, [1050])]);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/epg/collect-now",
            new { serviceId = 1050 });

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("streams").GetInt32());
    }

    [Fact]
    public async Task ABoostForSomethingNobodyCarriesIsNotFound()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049])]);

        (HttpStatusCode status, _) = await feature.PostAsync(
            "/api/epg/collect-now",
            new { serviceId = 9999 });

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task ASecondBoostAskedForAtOnceIsRefusedAndSaysWhy()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049])]);

        (HttpStatusCode first, _) = await feature.PostAsync("/api/epg/collect-now");
        (HttpStatusCode second, JsonElement refused) = await feature.PostAsync("/api/epg/collect-now");

        Assert.Equal(HttpStatusCode.Accepted, first);
        Assert.Equal(HttpStatusCode.Conflict, second);
        Assert.Contains(
            refused.GetProperty("data").GetProperty("refusal").GetString(),
            (string[])["oneIsAlreadyRunning", "tooSoonAfterTheLastOne"]);
    }

    [Fact]
    public async Task AFormPostCannotSetABoostGoing()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049])]);
        using var form = new StringContent("networkId=4", Encoding.UTF8);

        form.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using HttpResponseMessage response = await feature.Client.PostAsync(
            new Uri("/api/epg/collect-now", UriKind.Relative),
            form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    private static BroadcastStream Stream(int network, int stream, IReadOnlyList<int> services)
        => new(
            new NetworkId(network),
            new TransportStreamId(stream),
            TuningParameters.Terrestrial(22),
            [.. services.Select(service => new ServiceId(service))]);
}
