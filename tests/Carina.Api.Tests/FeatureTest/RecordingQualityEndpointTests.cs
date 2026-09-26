using System.Net;
using System.Text.Json;

using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

public sealed class RecordingQualityEndpointTests
{
    [Fact]
    public async Task ARecordingLeftEncryptedIsNotOfferedAsAGoodOneEvenThoughNothingWasLost()
    {
        await using var feature = new RecordingFeature();
        Measured(feature, dropped: 0, total: 5302549, scrambled: 5042768);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");
        JsonElement drops = body.GetProperty("data").GetProperty("items")[0].GetProperty("drops");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("mayNotBeWatchable", drops.GetProperty("quality").GetString());
        Assert.Equal(0, drops.GetProperty("ccDroppedPackets").GetInt64());
        Assert.Equal(5042768, drops.GetProperty("scrambledPackets").GetInt64());
    }

    [Fact]
    public async Task ARecordingTheCardUnlockedAndNothingWasLostFromIsGood()
    {
        await using var feature = new RecordingFeature();
        Measured(feature, dropped: 0, total: 6889195, scrambled: 0);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");
        JsonElement drops = body.GetProperty("data").GetProperty("items")[0].GetProperty("drops");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("good", drops.GetProperty("quality").GetString());
        Assert.Equal("good", drops.GetProperty("scrambleQuality").GetString());
        Assert.Equal(0, drops.GetProperty("scrambledPackets").GetInt64());
    }

    [Fact]
    public async Task ARecordingLeftScrambledSaysScramblingIsWhatMakesItUnwatchable()
    {
        await using var feature = new RecordingFeature();
        Measured(feature, dropped: 0, total: 1_000_000, scrambled: 20_000);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");
        JsonElement drops = body.GetProperty("data").GetProperty("items")[0].GetProperty("drops");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("mayNotBeWatchable", drops.GetProperty("quality").GetString());
        Assert.Equal("mayNotBeWatchable", drops.GetProperty("scrambleQuality").GetString());
    }

    [Fact]
    public async Task ARecordingThatOnlyLostPacketsSaysItsScramblingWasGood()
    {
        await using var feature = new RecordingFeature();
        Measured(feature, dropped: 50_000, total: 1_000_000, scrambled: 0);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");
        JsonElement drops = body.GetProperty("data").GetProperty("items")[0].GetProperty("drops");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("mayNotBeWatchable", drops.GetProperty("quality").GetString());
        Assert.Equal("good", drops.GetProperty("scrambleQuality").GetString());
    }

    [Fact]
    public async Task ARecordingNothingCountedSaysSoRatherThanReadingAsGood()
    {
        await using var feature = new RecordingFeature();
        feature.Held();

        (_, JsonElement body) = await feature.GetAsync("/api/recordings");
        JsonElement drops = body.GetProperty("data").GetProperty("items")[0].GetProperty("drops");

        Assert.Equal("unmeasured", drops.GetProperty("quality").GetString());
        Assert.Equal("unmeasured", drops.GetProperty("scrambleQuality").GetString());
        Assert.False(drops.GetProperty("ccMeasured").GetBoolean());
    }

    [Fact]
    public async Task TheOneRecordingSaysTheSameThingTheListSaidAboutIt()
    {
        await using var feature = new RecordingFeature();
        Recording recording = Measured(feature, dropped: 0, total: 5302549, scrambled: 5042768);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement drops = body.GetProperty("data").GetProperty("recording").GetProperty("drops");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("mayNotBeWatchable", drops.GetProperty("quality").GetString());
        Assert.Equal("mayNotBeWatchable", drops.GetProperty("scrambleQuality").GetString());
    }

    [Fact(DisplayName = "BR-QD-003: moving the level scrambling is held against moves what every recording says")]
    public async Task MovingTheLevelScramblingIsHeldAgainstMovesWhatEveryRecordingSays()
    {
        await using var feature = new RecordingFeature();
        Recording recording = Measured(feature, dropped: 0, total: 1_000_000, scrambled: 20_000);

        (HttpStatusCode moved, _) = await feature.PatchAsync(
            "/api/quality/thresholds/packetsLeftScrambledUnwatchable",
            new { value = 0.05 });
        (_, JsonElement list) = await feature.GetAsync("/api/recordings");
        (_, JsonElement one) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");

        JsonElement listed = list.GetProperty("data").GetProperty("items")[0].GetProperty("drops");
        JsonElement detailed = one.GetProperty("data").GetProperty("recording").GetProperty("drops");

        Assert.Equal(HttpStatusCode.OK, moved);
        Assert.Equal("warning", listed.GetProperty("scrambleQuality").GetString());
        Assert.Equal("warning", listed.GetProperty("quality").GetString());
        Assert.Equal("warning", detailed.GetProperty("scrambleQuality").GetString());
        Assert.Equal("warning", detailed.GetProperty("quality").GetString());
    }

    private static Recording Measured(RecordingFeature feature, long dropped, long total, long scrambled)
    {
        Recording recording = feature.Held();

        recording.Measure(
            DropCounters.Counted(dropped, total),
            DropTimeline.Unlocated,
            scrambled,
            0,
            RecordingFeature.Noon.AddMinutes(10));

        return recording;
    }
}
