using System.Net;
using System.Text.Json;

using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

public sealed class RecordingGapEndpointTests
{
    private static readonly DateTime Ended = RecordingFeature.Noon.AddHours(1);

    [Fact]
    public async Task ARecordingThatCarriedOnAfterAGapSaysWhereAndHowLongInTheDetailAndTheList()
    {
        await using RecordingFeature feature = new();
        Recording recording = feature.Held();
        recording.Missed(new RecordingGap(
            recording.StartedAtActual.AddSeconds(312),
            recording.StartedAtActual.AddSeconds(314.5)));
        recording.Wrote(TimeSpan.FromHours(1));
        recording.Abort(Ended);
        recording.Settle(RecordingOutcome.Complete, 1_234_567, Ended);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement detail = body.GetProperty("data").GetProperty("recording");
        (_, JsonElement list) = await feature.GetAsync("/api/recordings");
        JsonElement listed = Assert.Single(list.GetProperty("data").GetProperty("items").EnumerateArray());
        JsonElement gap = Assert.Single(detail.GetProperty("gaps").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("complete", detail.GetProperty("outcome").GetString());
        Assert.Equal(2.5, gap.GetProperty("seconds").GetDouble(), 3);
        Assert.Equal(312, gap.GetProperty("atSecond").GetDouble(), 3);
        Assert.Equal(2_500, detail.GetProperty("missedMs").GetInt64());
        Assert.Equal(2_500, listed.GetProperty("missedMs").GetInt64());
        Assert.Single(listed.GetProperty("gaps").EnumerateArray());
    }

    [Fact]
    public async Task ARecordingThatMissedNothingAnswersNoGap()
    {
        await using RecordingFeature feature = new();
        Recording recording = feature.Held();

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement detail = body.GetProperty("data").GetProperty("recording");

        Assert.Empty(detail.GetProperty("gaps").EnumerateArray());
        Assert.Equal(0, detail.GetProperty("missedMs").GetInt64());
    }
}
