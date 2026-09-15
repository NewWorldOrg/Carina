using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

public sealed class QualityTrendEndpointTests
{
    [Fact(DisplayName = "BR-QD-001: packets lost come back a point a day, the day holding an unmeasured recording counting it beside the share")]
    public async Task PacketsLostComeBackAPointADay()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(dropped: 0, total: 1_000_000, startedAt: QualityFeature.Noon.AddHours(-3));
        feature.Recorded(dropped: null, total: null, scrambled: null, startedAt: QualityFeature.Noon.AddHours(-3));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/trends?days=3");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement data = body.GetProperty("data");
        JsonElement series = Assert.Single(data.GetProperty("series").EnumerateArray());
        JsonElement points = series.GetProperty("points");

        Assert.Equal("packetsLost", data.GetProperty("subject").GetString());
        Assert.Equal("day", data.GetProperty("step").GetString());
        Assert.Equal(QualityTrendFrame.MostPoints, data.GetProperty("mostPoints").GetInt32());
        Assert.Equal(JsonValueKind.Null, series.GetProperty("channel").ValueKind);
        Assert.InRange(points.GetArrayLength(), 3, 5);

        JsonElement held = points.EnumerateArray()
            .Single(point => point.GetProperty("reading").GetProperty("subjects").GetInt32() > 0)
            .GetProperty("reading");

        Assert.Equal("good", held.GetProperty("state").GetString());
        Assert.Equal(1, held.GetProperty("measured").GetInt32());
        Assert.Equal(1, held.GetProperty("unmeasured").GetInt32());
        Assert.Equal("nothingToMeasure", points[0].GetProperty("reading").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, points[0].GetProperty("worst").ValueKind);
    }

    [Fact]
    public async Task TheCarrierToNoiseOfAMultiplexComesBackByTheHourNamedByTheStreamThatCarriesIt()
    {
        await using var feature = new QualityFeature();
        feature.Streams.Carried.Add(new BroadcastStream(
            new NetworkId(1),
            new TransportStreamId(1_000),
            TuningParameters.Terrestrial(20),
            [new ServiceId(101), new ServiceId(102)]));
        feature.Signals.Windows.Add(new QualitySignalWindow(
            QualityFeature.Noon.AddHours(-2),
            new TunerDeviceId("adapter0"),
            new NetworkId(1),
            new ServiceId(101),
            360,
            360,
            0,
            0,
            9_000,
            [],
            [],
            QualityFeature.Noon.AddHours(-2)));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/trends?subject=carrierToNoise");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement data = body.GetProperty("data");
        JsonElement series = data.GetProperty("series");

        Assert.Equal("hour", data.GetProperty("step").GetString());
        Assert.Equal(2, series.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, series[0].GetProperty("channel").ValueKind);

        JsonElement channel = series[1].GetProperty("channel");

        Assert.Equal(1_000, channel.GetProperty("transportStreamId").GetInt32());
        Assert.Equal([101, 102], channel.GetProperty("serviceIds").EnumerateArray().Select(id => id.GetInt32()));

        JsonElement past = series[1].GetProperty("points").EnumerateArray()
            .Single(point => point.GetProperty("reading").GetProperty("measured").GetInt32() > 0);

        Assert.Equal("atOrAboveWarning", past.GetProperty("reading").GetProperty("state").GetString());
        Assert.Equal(9_000, past.GetProperty("worst").GetDouble());
        Assert.Equal(15_000, past.GetProperty("level").GetDouble());
        Assert.Equal(0, past.GetProperty("layers").GetArrayLength());
    }

    [Fact(DisplayName = "BR-QD-001: a signal nothing sampled still answers every point, as nothing measured")]
    public async Task ASignalNothingSampledStillAnswersEveryPointAsNothingMeasured()
    {
        await using var feature = new QualityFeature();

        JsonElement series = Assert.Single(
            (await feature.GetAsync("/api/quality/trends?days=2&subject=lockRate")).Body
                .GetProperty("data")
                .GetProperty("series")
                .EnumerateArray());

        JsonElement points = series.GetProperty("points");

        Assert.InRange(points.GetArrayLength(), 48, QualityTrendFrame.MostPoints);
        Assert.All(
            points.EnumerateArray(),
            point =>
            {
                Assert.Equal("nothingToMeasure", point.GetProperty("reading").GetProperty("state").GetString());
                Assert.Equal(0, point.GetProperty("reading").GetProperty("measured").GetInt32());
            });
    }

    [Theory(DisplayName = "BR-QV-001: a day count outside the range or a subject nobody names is refused")]
    [InlineData("/api/quality/trends?days=0")]
    [InlineData("/api/quality/trends?days=366")]
    [InlineData("/api/quality/trends?subject=99")]
    public async Task ADayCountOutsideTheRangeOrASubjectNobodyNamesIsRefused(string path)
    {
        await using var feature = new QualityFeature();

        Assert.Equal(HttpStatusCode.BadRequest, (await feature.GetAsync(path)).Status);
    }
}
