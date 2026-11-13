using System.Net;
using System.Text.Json;

using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

public sealed class RecordingQualityEndpointTests
{
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
