using System.Net;
using System.Text.Json;

using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

public sealed class LeftScrambledEndpointTests
{
    private static readonly DateTime Ended = RecordingFeature.Noon.AddHours(1);

    [Fact]
    public async Task ARecordingThatCameOutWholeButScrambledSaysSoInTheDetailAndTheList()
    {
        await using var feature = new RecordingFeature();
        Recording recording = Scrambled(feature);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement detail = body.GetProperty("data").GetProperty("recording");
        (_, JsonElement list) = await feature.GetAsync("/api/recordings");
        JsonElement listed = Assert.Single(list.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("complete", detail.GetProperty("outcome").GetString());
        Assert.True(detail.GetProperty("leftScrambled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("descrambledAt").ValueKind);
        Assert.True(listed.GetProperty("leftScrambled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("descrambledAt").ValueKind);
    }

    [Fact]
    public async Task ARecordingDescrambledSinceSaysWhenAndIsNoLongerLeftScrambled()
    {
        await using var feature = new RecordingFeature();
        Recording recording = Scrambled(feature);
        recording.Descrambled(Ended.AddDays(1));

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement detail = body.GetProperty("data").GetProperty("recording");

        Assert.False(detail.GetProperty("leftScrambled").GetBoolean());
        Assert.Equal(Ended.AddDays(1), detail.GetProperty("descrambledAt").GetDateTime().ToUniversalTime());
        Assert.Equal("complete", detail.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task ARecordingThatNamesNoScramblingIsNotLeftScrambled()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        recording.Wrote(TimeSpan.FromHours(1));
        recording.Abort(Ended);
        recording.Settle(RecordingOutcome.Complete, 1_234_567, Ended);

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement detail = body.GetProperty("data").GetProperty("recording");

        Assert.False(detail.GetProperty("leftScrambled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("descrambledAt").ValueKind);
    }

    private static Recording Scrambled(RecordingFeature feature)
    {
        Recording recording = feature.Held();
        recording.Wrote(TimeSpan.FromHours(1));
        recording.Note(new OutcomeDetail(
            RecordingFault.ScramblingUnresolved,
            null,
            string.Empty,
            RecordingFeature.Noon));
        recording.Abort(Ended);
        recording.Settle(RecordingOutcome.Complete, 1_234_567, Ended);

        return recording;
    }
}
