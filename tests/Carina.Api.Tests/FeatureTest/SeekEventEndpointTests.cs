using System.Net;
using System.Text.Json;

using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Api.Tests.FeatureTest;

public sealed class SeekEventEndpointTests
{
    private const string Forward = """{"event":"forward","means":"step","fromSec":600.004,"toSec":702.3}""";

    [Fact(DisplayName = "BR-CV-004: a seek is taken, answered as it was kept, and keeps nothing of who sent it")]
    public async Task ASeekIsTakenAsItWasKept()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(RecordingSegmentsFeature.SeeksPath(recording.Id), Forward);
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["event", "fromSec", "happenedAt", "means", "toSec"],
            data.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(feature.Clock.Now, data.GetProperty("happenedAt").GetDateTime().ToUniversalTime());
        Assert.Equal(("forward", "step", 600d, 702.3d), (
            data.GetProperty("event").GetString(),
            data.GetProperty("means").GetString(),
            data.GetProperty("fromSec").GetDouble(),
            data.GetProperty("toSec").GetDouble()));
        SeekEvent kept = Assert.Single(feature.Seeks.Taken);
        Assert.Equal(
            (recording.Id, SeekAction.Forward, SeekMeans.Step, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(702.3)),
            (kept.RecordingId, kept.Event, kept.Means, kept.From, kept.To));
        Assert.Empty(feature.Skips.Taken);
    }

    [Theory(DisplayName = "BR-CV-004: a seek by each way of seeking is taken, and coming back names an earlier moment than where the viewer was")]
    [InlineData("""{"event":"forward","means":"bar","fromSec":0,"toSec":1800}""", "forward", "bar")]
    [InlineData("""{"event":"forward","means":"chapter","fromSec":30,"toSec":120}""", "forward", "chapter")]
    [InlineData("""{"event":"forward","means":"key","fromSec":30,"toSec":40}""", "forward", "key")]
    [InlineData("""{"event":"returned","means":"bar","fromSec":700,"toSec":612.5}""", "returned", "bar")]
    public async Task EverySeekIsTaken(string json, string action, string means)
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(RecordingSegmentsFeature.SeeksPath(recording.Id), json);
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((action, means), (data.GetProperty("event").GetString(), data.GetProperty("means").GetString()));
        Assert.Single(feature.Seeks.Taken);
    }

    [Theory(DisplayName = "BR-CV-004: a seek it does not know, a way of seeking it does not know, a moment outside the recording, a seek going the other way from what it says and anything about who sent it are refused, and nothing is taken")]
    [InlineData("""{"event":"skipped","means":"bar","fromSec":600,"toSec":690}""", "event")]
    [InlineData("""{"means":"bar","fromSec":600,"toSec":690}""", "event")]
    [InlineData("""{"event":"forward","means":"mouse","fromSec":600,"toSec":690}""", "means")]
    [InlineData("""{"event":"forward","fromSec":600,"toSec":690}""", "means")]
    [InlineData("""{"event":"forward","means":"bar","fromSec":-1,"toSec":690}""", "fromSec")]
    [InlineData("""{"event":"forward","means":"bar","toSec":690}""", "fromSec")]
    [InlineData("""{"event":"forward","means":"bar","fromSec":600,"toSec":1800.01}""", "toSec")]
    [InlineData("""{"event":"forward","means":"bar","fromSec":600,"toSec":600}""", "toSec")]
    [InlineData("""{"event":"forward","means":"bar","fromSec":600,"toSec":590}""", "toSec")]
    [InlineData("""{"event":"returned","means":"bar","fromSec":600,"toSec":690}""", "toSec")]
    [InlineData("""{"event":"forward","means":"bar","fromSec":600,"toSec":690,"viewer":"someone"}""", "viewer")]
    [InlineData("""{"event":"forward","means":"bar","fromSec":600,"toSec":690,"kind":"break"}""", "kind")]
    public async Task WhatCannotBeReadIsRefused(string json, string named)
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(RecordingSegmentsFeature.SeeksPath(recording.Id), json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        string message = body.GetProperty("message").GetString()!;
        Assert.True(message.StartsWith($"{named}: ", StringComparison.Ordinal) || message.Contains($"here: {named} ", StringComparison.Ordinal), message);
        Assert.Empty(feature.Seeks.Taken);
    }

    [Fact(DisplayName = "BR-CV-004: past six hundred seeks of a recording within an hour the next is refused, while its skip events, another recording and the hour after take theirs")]
    public async Task PastSixHundredSeeksAnHourTheNextIsRefused()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        Recording other = feature.Ended();
        DateTime now = feature.Clock.Now;

        for (int at = 0; at < SeekEvent.MostAnHour; at++)
        {
            await feature.Seeks.TakeAsync(
                SeekEvent.Of(recording.Id, new ViewerSeek(now.AddMinutes(-59).AddSeconds(at), SeekAction.Forward, SeekMeans.Bar, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690))),
                CancellationToken.None);
        }

        (HttpStatusCode refused, JsonElement body) = await feature.PostAsync(RecordingSegmentsFeature.SeeksPath(recording.Id), Forward);
        (HttpStatusCode skipped, _) = await feature.PostAsync(RecordingSegmentsFeature.SkipsPath(recording.Id), """{"event":"skipped","kind":"break","fromSec":600,"toSec":690,"modelVersion":1}""");
        (HttpStatusCode elsewhere, _) = await feature.PostAsync(RecordingSegmentsFeature.SeeksPath(other.Id), Forward);
        feature.Clock.Now = now.AddHours(1);
        (HttpStatusCode later, _) = await feature.PostAsync(RecordingSegmentsFeature.SeeksPath(recording.Id), Forward);

        Assert.Equal((HttpStatusCode.TooManyRequests, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK), (refused, skipped, elsewhere, later));
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal(SeekEvent.MostAnHour + 2, feature.Seeks.Taken.Count);
    }

    [Fact(DisplayName = "BR-CV-004: a seek on a recording still being written is taken within the length written so far")]
    public async Task ASeekOnARecordingStillBeingWrittenIsTaken()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.StillWriting();

        (HttpStatusCode within, _) = await feature.PostAsync(RecordingSegmentsFeature.SeeksPath(recording.Id), """{"event":"forward","means":"bar","fromSec":600,"toSec":690}""");
        (HttpStatusCode past, _) = await feature.PostAsync(RecordingSegmentsFeature.SeeksPath(recording.Id), """{"event":"forward","means":"bar","fromSec":700,"toSec":750.01}""");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.BadRequest), (within, past));
    }

    [Fact(DisplayName = "a seek on a recording whose row is gone is not found, and an id that is not one is refused")]
    public async Task ASeekOnARecordingWhoseRowIsGoneIsNotFound()
    {
        await using var feature = new RecordingSegmentsFeature();

        (HttpStatusCode missing, _) = await feature.PostAsync(RecordingSegmentsFeature.SeeksPath(RecordingId.New()), Forward);
        (HttpStatusCode malformed, _) = await feature.PostAsync("/api/recordings/not-an-id/seeks", Forward);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.BadRequest), (missing, malformed));
        Assert.Empty(feature.Seeks.Taken);
    }

    [Fact(DisplayName = "BR-CA-001: a seek sent from another origin, or by a caller who has not signed in, is refused and nothing is taken")]
    public async Task ASeekFromAnotherOriginOrNoSessionIsRefused()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        using HttpClient elsewhere = feature.FromElsewhere();
        using HttpClient anonymous = feature.Anonymous();

        (HttpStatusCode fromElsewhere, _) = await feature.PostAsync(RecordingSegmentsFeature.SeeksPath(recording.Id), Forward, elsewhere);
        (HttpStatusCode signedOut, _) = await feature.PostAsync(RecordingSegmentsFeature.SeeksPath(recording.Id), Forward, anonymous);

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized), (fromElsewhere, signedOut));
        Assert.Empty(feature.Seeks.Taken);
    }
}
