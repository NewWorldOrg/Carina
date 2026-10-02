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
        Measured(feature, dropped: 0, total: 5_000_000, scrambled: 4_800_000);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");
        JsonElement drops = body.GetProperty("data").GetProperty("items")[0].GetProperty("drops");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("mayNotBeWatchable", drops.GetProperty("quality").GetString());
        Assert.Equal(0, drops.GetProperty("ccDroppedPackets").GetInt64());
        Assert.Equal(4_800_000, drops.GetProperty("scrambledPackets").GetInt64());
    }

    [Fact]
    public async Task ARecordingTheCardUnlockedAndNothingWasLostFromIsGood()
    {
        await using var feature = new RecordingFeature();
        Measured(feature, dropped: 0, total: 7_000_000, scrambled: 0);

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
        Recording recording = Measured(feature, dropped: 0, total: 5_000_000, scrambled: 4_800_000);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement drops = body.GetProperty("data").GetProperty("recording").GetProperty("drops");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("mayNotBeWatchable", drops.GetProperty("quality").GetString());
        Assert.Equal("mayNotBeWatchable", drops.GetProperty("scrambleQuality").GetString());
    }

    [Fact(DisplayName = "moving the level scrambling is held against moves what every recording says")]
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

    [Fact(DisplayName = "BR-KD-031: a recording descrambled since is read by what it lost alone, and still says how much was left scrambled when it was received")]
    public async Task ARecordingDescrambledSinceIsReadByWhatItLostAlone()
    {
        await using var feature = new RecordingFeature();
        Recording recording = Measured(feature, dropped: 0, total: 5_000_000, scrambled: 4_800_000);
        DateTime ended = RecordingFeature.Noon.AddHours(1);

        recording.Wrote(TimeSpan.FromHours(1));
        recording.Note(new OutcomeDetail(RecordingFault.ScramblingUnresolved, null, string.Empty, RecordingFeature.Noon));
        recording.Abort(ended);
        recording.Settle(RecordingOutcome.Complete, 1_234_567, ended);

        (_, JsonElement before) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement leftScrambled = before.GetProperty("data").GetProperty("recording").GetProperty("drops");

        recording.Descrambled(ended.AddDays(1));

        (_, JsonElement list) = await feature.GetAsync("/api/recordings");
        (_, JsonElement one) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        (_, JsonElement clean) = await feature.GetAsync("/api/recordings?drops=clean");

        JsonElement listed = list.GetProperty("data").GetProperty("items")[0].GetProperty("drops");
        JsonElement detailed = one.GetProperty("data").GetProperty("recording").GetProperty("drops");

        Assert.Equal("mayNotBeWatchable", leftScrambled.GetProperty("quality").GetString());
        Assert.Equal("mayNotBeWatchable", leftScrambled.GetProperty("scrambleQuality").GetString());
        Assert.Equal("good", listed.GetProperty("quality").GetString());
        Assert.Equal("good", listed.GetProperty("scrambleQuality").GetString());
        Assert.Equal("good", detailed.GetProperty("quality").GetString());
        Assert.Equal("good", detailed.GetProperty("scrambleQuality").GetString());
        Assert.Equal(4_800_000, detailed.GetProperty("scrambledPackets").GetInt64());
        Assert.Equal(
            recording.Id.Wire,
            Assert.Single(clean.GetProperty("data").GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
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
