using System.Globalization;
using System.Net;
using System.Text.Json;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class CarriedShelfEndpointTests
{
    private const int Shelf = 53;

    private const int Stations = 6;

    private static readonly DateTime FirstAired = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string?[] Metrics = ["packetsLost", "packetsLeftScrambled", "overflows"];

    [Theory(DisplayName = "every page of a carried shelf reads unmeasured in either order, with no count standing in for one")]
    [InlineData("startedAt", false)]
    [InlineData("startedAt", true)]
    [InlineData("programmeStartsAt", false)]
    [InlineData("programmeStartsAt", true)]
    public async Task EveryPageOfACarriedShelfReadsUnmeasuredInEitherOrder(string sort, bool descending)
    {
        await using var feature = new RecordingFeature();
        Carry(feature);

        List<JsonElement> seen = [];

        foreach (int page in Enumerable.Range(1, 3))
        {
            (HttpStatusCode status, JsonElement body) = await feature.GetAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"/api/recordings?sort={sort}&descending={(descending ? "true" : "false")}&perPage=20&page={page}"));

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(Shelf, body.GetProperty("data").GetProperty("total").GetInt32());
            seen.AddRange(body.GetProperty("data").GetProperty("items").EnumerateArray());
        }

        Assert.Equal(Shelf, seen.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.All(seen, item => SaysNothingWasCounted(item.GetProperty("drops")));
    }

    [Fact(DisplayName = "the detail of every carried recording says nothing was counted, as the list did")]
    public async Task TheDetailOfEveryCarriedRecordingSaysNothingWasCounted()
    {
        await using var feature = new RecordingFeature();

        foreach (Recording carried in Carry(feature))
        {
            (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/recordings/{carried.Id.Wire}");
            JsonElement data = body.GetProperty("data");

            Assert.Equal(HttpStatusCode.OK, status);
            SaysNothingWasCounted(data.GetProperty("recording").GetProperty("drops"));
            Assert.False(data.GetProperty("positions").GetProperty("located").GetBoolean());
        }
    }

    [Fact(DisplayName = "the drop reading finds the whole carried shelf unmeasured and none of it clean or dropped")]
    public async Task TheDropReadingFindsTheWholeCarriedShelfUnmeasured()
    {
        await using var feature = new RecordingFeature();
        Carry(feature);

        Assert.Equal(Shelf, await TotalAsync(feature, "/api/recordings?drops=unmeasured&perPage=200"));
        Assert.Equal(0, await TotalAsync(feature, "/api/recordings?drops=clean&perPage=200"));
        Assert.Equal(0, await TotalAsync(feature, "/api/recordings?drops=dropped&perPage=200"));
    }

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
                startedAt: FirstAired.AddHours(60 * at));
        }

        const string Period = "from=2026-03-31T00:00:00Z&until=2026-09-07T12:00:00Z";

        JsonElement summary = await DataAsync(feature, $"/api/quality/summary?{Period}");
        JsonElement channels = await DataAsync(feature, $"/api/quality/channels?{Period}&sort=worst");
        JsonElement tuners = await DataAsync(feature, $"/api/quality/tuners?{Period}");
        JsonElement beyond = await DataAsync(feature, $"/api/quality/recordings?{Period}&sort=worst");

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

    private static List<Recording> Carry(RecordingFeature feature)
    {
        List<Recording> carried = [];

        foreach (int at in Enumerable.Range(0, Shelf))
        {
            Recording recording = Carried(at);
            feature.Recordings.Recordings.Add(recording);
            carried.Add(recording);
        }

        return carried;
    }

    private static Recording Carried(int at)
    {
        RecordingId id = RecordingId.New();
        DateTime start = FirstAired.AddHours(60 * at);
        DateTime end = start.AddMinutes(30);

        return Recording.Rehydrate(
            id,
            null,
            new ProgrammeRef(new NetworkId(7), new ServiceId(1_001 + (at % Stations)), new EventId(5_001 + at), start),
            new OutputRoot("carried"),
            RecordingFileName.For(id, ".m2ts"),
            188 * (at + 1),
            RecordingFeature.Noon,
            start,
            end,
            end,
            (long)(end - start).TotalMilliseconds,
            0,
            [],
            start,
            end,
            end,
            RecordingOutcome.Complete,
            [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            null,
            ThumbnailState.Pending,
            ProgrammeSnapshot.Of(
                string.Create(CultureInfo.InvariantCulture, $"a carried programme {at + 1}"),
                string.Empty,
                [],
                [],
                start,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone);
    }

    private static void SaysNothingWasCounted(JsonElement drops)
    {
        Assert.Equal("unmeasured", drops.GetProperty("quality").GetString());
        Assert.Equal("unmeasured", drops.GetProperty("scrambleQuality").GetString());
        Assert.False(drops.GetProperty("ccMeasured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, drops.GetProperty("ccDroppedPackets").ValueKind);
        Assert.Equal(JsonValueKind.Null, drops.GetProperty("ccTotalPackets").ValueKind);
        Assert.Equal(JsonValueKind.Null, drops.GetProperty("scrambledPackets").ValueKind);
        Assert.Equal(JsonValueKind.Null, drops.GetProperty("eovfCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, drops.GetProperty("measuredUpdatedAt").ValueKind);
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

    private static async Task<int> TotalAsync(RecordingFeature feature, string path)
    {
        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, status);

        return body.GetProperty("data").GetProperty("total").GetInt32();
    }

    private static async Task<JsonElement> DataAsync(QualityFeature feature, string path)
    {
        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, status);

        return body.GetProperty("data");
    }
}
