using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Carina.Api.Tests.Feature.Quality;

/// <summary>
/// A shelf of recordings carried over from another recorder, with nothing counted on any of them, and what every
/// quality surface has to say of it.
/// </summary>
internal static class CarriedShelf
{
    public const int Shelf = 53;

    public const int Stations = 6;

    private static readonly TimeSpan BetweenAirings = TimeSpan.FromHours(60);

    private static readonly DateTime FirstAired = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime LastAired = FirstAired + (BetweenAirings * (Shelf - 1));

    private static readonly string?[] Metrics = ["packetsLost", "packetsLeftScrambled", "overflows"];

    /// <summary>Records the shelf and answers the period that holds all of it, as a query string.</summary>
    public static string Shelved(QualityFeature feature)
    {
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

        return string.Create(
            CultureInfo.InvariantCulture,
            $"from={FirstAired.AddDays(-1):yyyy-MM-ddTHH:mm:ssZ}&until={LastAired.AddDays(1):yyyy-MM-ddTHH:mm:ssZ}");
    }

    public static void ReadsUnmeasuredThroughout(JsonElement measures, int? subjects)
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

    public static async Task<JsonElement> DataAsync(QualityFeature feature, string path)
    {
        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, status);

        return body.GetProperty("data");
    }
}
