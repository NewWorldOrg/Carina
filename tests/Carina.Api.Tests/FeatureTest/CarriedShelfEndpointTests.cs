using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Carina.Api.Tests.FeatureTest;

public sealed class CarriedShelfEndpointTests
{
    private const int Shelf = 53;

    private const int Stations = 6;

    private static readonly TimeSpan BetweenAirings = TimeSpan.FromHours(60);

    private static readonly DateTime FirstAired = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime LastAired = FirstAired + (BetweenAirings * (Shelf - 1));

    private static readonly string?[] Metrics = ["packetsLost", "packetsLeftScrambled", "overflows"];

    [Fact(DisplayName = "a carried shelf reads unmeasured on the summary, the channels, the tuners and the recordings list")]
    public async Task ACarriedShelfReadsUnmeasuredOnEveryQualitySurface()
    {
        await using var feature = new QualityFeature();

        foreach (int at in Enumerable.Range(0, Shelf))
        {
            feature.Recorded(
                dropped: null,
                total: null,
                scrambled: null,
                service: 1_001 + (at % Stations),
                tuner: null,
                kind: null,
                startedAt: FirstAired + (BetweenAirings * at));
        }

        string period = string.Create(
            CultureInfo.InvariantCulture,
            $"from={FirstAired.AddDays(-1):yyyy-MM-ddTHH:mm:ssZ}&until={LastAired.AddDays(1):yyyy-MM-ddTHH:mm:ssZ}");

        JsonElement summary = await DataAsync(feature, $"/api/quality/summary?{period}");
        JsonElement channels = await DataAsync(feature, $"/api/quality/channels?{period}&sort=worst");
        JsonElement tuners = await DataAsync(feature, $"/api/quality/tuners?{period}");
        JsonElement beyond = await DataAsync(feature, $"/api/quality/recordings?{period}&sort=worst");

        Assert.Equal(Shelf, summary.GetProperty("recordings").GetInt32());
        ReadsUnmeasuredThroughout(summary.GetProperty("measures"), Shelf);

        Assert.Equal(Stations, channels.GetProperty("items").GetArrayLength());
        Assert.All(
            channels.GetProperty("items").EnumerateArray(),
            channel => ReadsUnmeasuredThroughout(channel.GetProperty("measures"), null));

        JsonElement tuner = Assert.Single(tuners.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, tuner.GetProperty("deviceId").ValueKind);
        ReadsUnmeasuredThroughout(tuner.GetProperty("measures"), Shelf);

        Assert.Equal(0, beyond.GetProperty("total").GetInt32());
        Assert.Equal(0, beyond.GetProperty("items").GetArrayLength());
        ReadsUnmeasuredThroughout(beyond.GetProperty("whole"), Shelf);
    }

    private static void ReadsUnmeasuredThroughout(JsonElement measures, int? subjects)
    {
        Assert.Equal(Metrics, measures.EnumerateArray().Select(measure => measure.GetProperty("metric").GetString()));
        Assert.All(measures.EnumerateArray(), measure =>
        {
            JsonElement reading = measure.GetProperty("reading");

            Assert.Equal("unmeasured", reading.GetProperty("state").GetString());
            Assert.Equal(0, reading.GetProperty("measured").GetInt32());
            Assert.Equal(0, reading.GetProperty("good").GetInt32());
            Assert.Equal(reading.GetProperty("subjects").GetInt32(), reading.GetProperty("unmeasured").GetInt32());
            Assert.Equal(JsonValueKind.Null, reading.GetProperty("average").ValueKind);
            Assert.Equal(JsonValueKind.Null, reading.GetProperty("lowest").ValueKind);
            Assert.Equal(JsonValueKind.Null, reading.GetProperty("highest").ValueKind);

            if (subjects is { } expected)
            {
                Assert.Equal(expected, reading.GetProperty("subjects").GetInt32());
            }
        });
    }

    private static async Task<JsonElement> DataAsync(QualityFeature feature, string path)
    {
        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, status);

        return body.GetProperty("data");
    }
}
