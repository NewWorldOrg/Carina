using System.Net;
using System.Text.Json;

using Carina.Domain.Quality;

namespace Carina.Api.Tests.Feature.Quality;

[Trait("Category", "Feature")]
public sealed class ListQualityChannelsActionTests
{
    [Fact]
    public async Task TheChannelsComeBackWorstFirstAndCarryEveryMeasure()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(service: 1_024, dropped: 0, total: 1_000_000);
        feature.Recorded(service: 1_032, dropped: 2_000, total: 1_000_000);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/channels");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement items = body.GetProperty("data").GetProperty("items");

        Assert.Equal([1_032, 1_024], items.EnumerateArray().Select(item => item.GetProperty("serviceId").GetInt32()));
        Assert.Equal(32_736, items[0].GetProperty("networkId").GetInt32());
        Assert.Equal("isdbT", items[0].GetProperty("kind").GetString());
        Assert.Equal(3, items[0].GetProperty("measures").GetArrayLength());
        Assert.Equal(2, body.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact(DisplayName = "only the measures this domain names are answered")]
    public async Task OnlyTheMeasuresThisDomainNamesAreAnswered()
    {
        await using var feature = new QualityFeature();
        feature.Recorded();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/channels?metric=overflows");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["overflows"],
            body.GetProperty("data").GetProperty("metrics").EnumerateArray().Select(metric => metric.GetString()));

        Assert.Equal(HttpStatusCode.BadRequest, (await feature.GetAsync("/api/quality/channels?metric=99")).Status);
    }

    [Fact(DisplayName = "an ordering outside the list is refused")]
    public async Task AnOrderingOutsideTheListIsRefused()
    {
        await using var feature = new QualityFeature();

        Assert.Equal(HttpStatusCode.BadRequest, (await feature.GetAsync("/api/quality/channels?sort=99")).Status);
    }

    [Fact(DisplayName = "a page size above the ceiling comes back cut down to it")]
    public async Task APageSizeAboveTheCeilingComesBackCutDownToIt()
    {
        await using var feature = new QualityFeature();

        JsonElement data = (await feature.GetAsync($"/api/quality/channels?perPage={QualityGroupQuery.MostPerPage + 1}"))
            .Body.GetProperty("data");

        Assert.Equal(QualityGroupQuery.MostPerPage, data.GetProperty("perPage").GetInt32());
    }

    [Fact(DisplayName = "a period wider than the longest span is refused")]
    public async Task APeriodWiderThanTheLongestSpanIsRefused()
    {
        await using var feature = new QualityFeature();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await feature.GetAsync($"/api/quality/channels?from={QualityFeature.Noon:O}&until={QualityFeature.Noon:O}")).Status);
    }

    [Fact(DisplayName = "a carried shelf reads unmeasured on the channels")]
    public async Task ACarriedShelfReadsUnmeasuredOnTheChannels()
    {
        await using var feature = new QualityFeature();
        string period = CarriedShelf.Shelved(feature);

        JsonElement channels = await CarriedShelf.DataAsync(feature, $"/api/quality/channels?{period}&sort=worst");

        Assert.Equal(CarriedShelf.Stations, channels.GetProperty("items").GetArrayLength());
        Assert.All(
            channels.GetProperty("items").EnumerateArray(),
            channel => CarriedShelf.ReadsUnmeasuredThroughout(channel.GetProperty("measures"), null));
    }
}
