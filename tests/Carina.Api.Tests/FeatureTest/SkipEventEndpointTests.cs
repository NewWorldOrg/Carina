using System.Net;
using System.Text.Json;

using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Api.Tests.FeatureTest;

public sealed class SkipEventEndpointTests
{
    private const string Skipped = """{"event":"skipped","kind":"break","fromSec":915.004,"toSec":1002.3,"modelVersion":1}""";

    [Fact(DisplayName = "BR-CV-003: a skip event is taken, answered as it was kept, and keeps nothing of who sent it")]
    public async Task ASkipEventIsTakenAsItWasKept()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(RecordingSegmentsFeature.SkipsPath(recording.Id), Skipped);
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["event", "fromSec", "happenedAt", "kind", "modelVersion", "toSec"],
            data.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(feature.Clock.Now, data.GetProperty("happenedAt").GetDateTime().ToUniversalTime());
        Assert.Equal(("skipped", "break", 915d, 1002.3d, 1), (
            data.GetProperty("event").GetString(),
            data.GetProperty("kind").GetString(),
            data.GetProperty("fromSec").GetDouble(),
            data.GetProperty("toSec").GetDouble(),
            data.GetProperty("modelVersion").GetInt32()));
        SkipEvent kept = Assert.Single(feature.Skips.Taken);
        Assert.Equal(
            (recording.Id, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(915), (int?)1),
            (kept.RecordingId, kept.Event, kept.Kind, kept.From, kept.ModelVersion));
        Assert.Equal(0, feature.Settings.Saves);
    }

    [Theory(DisplayName = "BR-CV-003: closing the button and coming back are taken, an opening and an ending too, and a chapter an encode marked names no version")]
    [InlineData("""{"event":"dismissed","kind":"opening","fromSec":30,"toSec":120,"modelVersion":1}""", "dismissed", "opening")]
    [InlineData("""{"event":"returned","kind":"ending","fromSec":1790,"toSec":1700,"modelVersion":null}""", "returned", "ending")]
    [InlineData("""{"event":"skipped","kind":"break","fromSec":0,"toSec":1800}""", "skipped", "break")]
    public async Task EveryActionIsTaken(string json, string action, string kind)
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(RecordingSegmentsFeature.SkipsPath(recording.Id), json);
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((action, kind), (data.GetProperty("event").GetString(), data.GetProperty("kind").GetString()));
        Assert.Single(feature.Skips.Taken);
    }

    [Theory(DisplayName = "BR-CV-003: an action or a kind it does not know, a moment outside the recording and a version that is not kept are refused, and nothing is taken")]
    [InlineData("""{"event":"watched","kind":"break","fromSec":915,"toSec":1002,"modelVersion":1}""", "event")]
    [InlineData("""{"kind":"break","fromSec":915,"toSec":1002,"modelVersion":1}""", "event")]
    [InlineData("""{"event":"skipped","kind":"programme","fromSec":915,"toSec":1002,"modelVersion":1}""", "kind")]
    [InlineData("""{"event":"skipped","kind":"commercial","fromSec":915,"toSec":1002,"modelVersion":1}""", "kind")]
    [InlineData("""{"event":"skipped","kind":"break","fromSec":-1,"toSec":1002,"modelVersion":1}""", "fromSec")]
    [InlineData("""{"event":"skipped","kind":"break","toSec":1002,"modelVersion":1}""", "fromSec")]
    [InlineData("""{"event":"skipped","kind":"break","fromSec":915,"toSec":1800.01,"modelVersion":1}""", "toSec")]
    [InlineData("""{"event":"skipped","kind":"break","fromSec":915,"toSec":1002,"modelVersion":7}""", "modelVersion")]
    [InlineData("""{"event":"skipped","kind":"break","fromSec":915,"toSec":1002,"modelVersion":0}""", "modelVersion")]
    [InlineData("""{"event":"skipped","kind":"break","fromSec":915,"toSec":1002,"modelVersion":1,"viewer":"someone"}""", "viewer")]
    public async Task WhatCannotBeReadIsRefused(string json, string named)
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(RecordingSegmentsFeature.SkipsPath(recording.Id), json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        string message = body.GetProperty("message").GetString()!;
        Assert.True(message.StartsWith($"{named}: ", StringComparison.Ordinal) || message.Contains($"here: {named} ", StringComparison.Ordinal), message);
        Assert.Empty(feature.Skips.Taken);
    }

    [Fact(DisplayName = "BR-CV-003: a version used before the one in use is still one an event may name")]
    public async Task AVersionUsedBeforeMayBeNamed()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        feature.Versions.Others.Add(feature.Versions.Active);
        feature.Versions.Active = Carina.TestSupport.HeldSegmentModelVersions.InUse(2, RecordingFeature.Noon);

        (HttpStatusCode status, _) = await feature.PostAsync(RecordingSegmentsFeature.SkipsPath(recording.Id), Skipped);

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact(DisplayName = "BR-CV-003: past six hundred events of a recording within an hour the next is refused, while another recording and the hour after take theirs")]
    public async Task PastSixHundredEventsAnHourTheNextIsRefused()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        Recording other = feature.Ended();
        DateTime now = feature.Clock.Now;

        for (int at = 0; at < SkipEvent.MostAnHour; at++)
        {
            await feature.Skips.TakeAsync(
                SkipEvent.Of(recording.Id, new ViewerAction(now.AddMinutes(-59).AddSeconds(at), ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(915), TimeSpan.FromSeconds(1002), 1)),
                CancellationToken.None);
        }

        (HttpStatusCode refused, JsonElement body) = await feature.PostAsync(RecordingSegmentsFeature.SkipsPath(recording.Id), Skipped);
        (HttpStatusCode elsewhere, _) = await feature.PostAsync(RecordingSegmentsFeature.SkipsPath(other.Id), Skipped);
        feature.Clock.Now = now.AddHours(1);
        (HttpStatusCode later, _) = await feature.PostAsync(RecordingSegmentsFeature.SkipsPath(recording.Id), Skipped);

        Assert.Equal((HttpStatusCode.TooManyRequests, HttpStatusCode.OK, HttpStatusCode.OK), (refused, elsewhere, later));
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal(SkipEvent.MostAnHour + 2, feature.Skips.Taken.Count);
    }

    [Fact(DisplayName = "BR-CV-003: an event on a recording still being written is taken within the length written so far")]
    public async Task AnEventOnARecordingStillBeingWrittenIsTaken()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.StillWriting();

        (HttpStatusCode within, _) = await feature.PostAsync(RecordingSegmentsFeature.SkipsPath(recording.Id), """{"event":"skipped","kind":"break","fromSec":600,"toSec":690,"modelVersion":1}""");
        (HttpStatusCode past, _) = await feature.PostAsync(RecordingSegmentsFeature.SkipsPath(recording.Id), Skipped);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.BadRequest), (within, past));
    }

    [Fact(DisplayName = "an event on a recording whose row is gone is not found, and an id that is not one is refused")]
    public async Task AnEventOnARecordingWhoseRowIsGoneIsNotFound()
    {
        await using var feature = new RecordingSegmentsFeature();

        (HttpStatusCode missing, _) = await feature.PostAsync(RecordingSegmentsFeature.SkipsPath(RecordingId.New()), Skipped);
        (HttpStatusCode malformed, _) = await feature.PostAsync("/api/recordings/not-an-id/skips", Skipped);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.BadRequest), (missing, malformed));
        Assert.Empty(feature.Skips.Taken);
    }

    [Fact(DisplayName = "BR-CA-001: an event sent from another origin, or by a caller who has not signed in, is refused and nothing is taken")]
    public async Task AnEventFromAnotherOriginOrNoSessionIsRefused()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        using HttpClient elsewhere = feature.FromElsewhere();
        using HttpClient anonymous = feature.Anonymous();

        (HttpStatusCode fromElsewhere, _) = await feature.PostAsync(RecordingSegmentsFeature.SkipsPath(recording.Id), Skipped, elsewhere);
        (HttpStatusCode signedOut, _) = await feature.PostAsync(RecordingSegmentsFeature.SkipsPath(recording.Id), Skipped, anonymous);

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized), (fromElsewhere, signedOut));
        Assert.Empty(feature.Skips.Taken);
    }
}
