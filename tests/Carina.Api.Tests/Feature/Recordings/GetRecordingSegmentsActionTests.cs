using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Carina.Api.Tests.Feature.Recordings;

[Trait("Category", "Feature")]
public sealed class GetRecordingSegmentsActionTests
{
    [Fact(DisplayName = "BR-CS-002: the stretches the version in use detected are answered in the recording's own time, and nothing of an answer a person gave or of where boundaries could be drawn")]
    public async Task TheDetectedStretchesAreAnsweredInTheRecordingsOwnTime()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        await feature.DetectedAsync(
            recording.Id,
            [TimeSpan.FromSeconds(912.28), TimeSpan.FromSeconds(912.31), TimeSpan.FromSeconds(1002.3), TimeSpan.FromSeconds(1347)],
            (0, 912.3, SegmentKind.Programme, 0.98),
            (912.3, 1002.3, SegmentKind.Break, 0.93),
            (1002.3, 1800, SegmentKind.Programme, 0.99));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(RecordingSegmentsFeature.Path(recording.Id));
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["cmShare", "detectedAt", "lengthSec", "modelVersion", "segments", "source", "state"],
            data.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(1800, data.GetProperty("lengthSec").GetDouble());
        Assert.Equal("detected", data.GetProperty("source").GetString());
        Assert.Equal("marked", data.GetProperty("state").GetString());
        Assert.Equal(1, data.GetProperty("modelVersion").GetInt32());
        Assert.Equal(RecordingFeature.Noon.AddMinutes(32), data.GetProperty("detectedAt").GetDateTime().ToUniversalTime());
        Assert.Equal(0.05, data.GetProperty("cmShare").GetDouble());
        Assert.Equal(
            [(0d, 912.3d, "programme", 0.98), (912.3d, 1002.3d, "break", 0.93), (1002.3d, 1800d, "programme", 0.99)],
            data.GetProperty("segments").EnumerateArray().Select(segment => (
                segment.GetProperty("startsAtSec").GetDouble(),
                segment.GetProperty("endsAtSec").GetDouble(),
                segment.GetProperty("kind").GetString()!,
                segment.GetProperty("probability").GetDouble())));
    }

    [Fact(DisplayName = "BR-CS-002: an opening and an ending the version in use detected are answered by their own kinds beside the programme and a break")]
    public async Task AnOpeningAndAnEndingAreAnsweredByTheirOwnKinds()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        await feature.DetectedAsync(
            recording.Id,
            [],
            (0, 90, SegmentKind.Opening, 0.92),
            (90, 900, SegmentKind.Programme, 0.99),
            (900, 960, SegmentKind.Break, 0.93),
            (960, 1710, SegmentKind.Programme, 0.99),
            (1710, 1800, SegmentKind.Ending, 0.9));

        JsonElement data = (await feature.GetAsync(RecordingSegmentsFeature.Path(recording.Id))).Body.GetProperty("data");

        Assert.Equal(
            ["opening", "programme", "break", "programme", "ending"],
            data.GetProperty("segments").EnumerateArray().Select(segment => segment.GetProperty("kind").GetString()));
    }

    [Fact(DisplayName = "BR-CS-002: a recording where only the programme was found is answered as nothing found, apart from one not detected")]
    public async Task NothingFoundIsApartFromNotDetected()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording plain = feature.Ended();
        Recording notDetected = feature.Ended();
        await feature.DetectedAsync(plain.Id, [], (0, 1800, SegmentKind.Programme, 0.97));

        JsonElement found = (await feature.GetAsync(RecordingSegmentsFeature.Path(plain.Id))).Body.GetProperty("data");
        JsonElement none = (await feature.GetAsync(RecordingSegmentsFeature.Path(notDetected.Id))).Body.GetProperty("data");

        Assert.Equal(("detected", "nothingFound", 0d), (found.GetProperty("source").GetString(), found.GetProperty("state").GetString(), found.GetProperty("cmShare").GetDouble()));
        Assert.Single(found.GetProperty("segments").EnumerateArray());
        Assert.Equal(("none", "none"), (none.GetProperty("source").GetString(), none.GetProperty("state").GetString()));
        Assert.Equal(JsonValueKind.Null, none.GetProperty("modelVersion").ValueKind);
        Assert.Equal(JsonValueKind.Null, none.GetProperty("detectedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, none.GetProperty("cmShare").ValueKind);
        Assert.Empty(none.GetProperty("segments").EnumerateArray());
        Assert.Equal(1800, none.GetProperty("lengthSec").GetDouble());
    }

    [Fact(DisplayName = "BR-CS-002: a recording whose sequence was thrown away is answered with no stretches and the share breaks took")]
    public async Task ASequenceThrownAwayIsAnsweredWithTheShareAlone()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        await feature.DetectedAsync(recording.Id, [TimeSpan.FromSeconds(600)], (0, 600, SegmentKind.Programme, 0.6), (600, 1800, SegmentKind.Break, 0.8));

        JsonElement data = (await feature.GetAsync(RecordingSegmentsFeature.Path(recording.Id))).Body.GetProperty("data");

        Assert.Equal("discarded", data.GetProperty("state").GetString());
        Assert.Empty(data.GetProperty("segments").EnumerateArray());
        Assert.Equal(0.6667, data.GetProperty("cmShare").GetDouble());
    }

    [Fact(DisplayName = "a recording still being written is answered, with the length written so far")]
    public async Task ARecordingStillBeingWrittenIsAnswered()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.StillWriting();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(RecordingSegmentsFeature.Path(recording.Id));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(750, body.GetProperty("data").GetProperty("lengthSec").GetDouble());
        Assert.Equal("none", body.GetProperty("data").GetProperty("state").GetString());
    }

    [Fact(DisplayName = "a recording whose row is gone is not found, whatever detection is kept of it, and an id that is not one is refused")]
    public async Task ARecordingWhoseRowIsGoneIsNotFound()
    {
        await using var feature = new RecordingSegmentsFeature();
        RecordingId gone = RecordingId.New();
        await feature.DetectedAsync(gone, [], (0, 1800, SegmentKind.Programme, 0.97));

        (HttpStatusCode missing, JsonElement body) = await feature.GetAsync(RecordingSegmentsFeature.Path(gone));
        (HttpStatusCode malformed, _) = await feature.GetAsync("/api/recordings/not-an-id/segments");

        Assert.Equal(HttpStatusCode.NotFound, missing);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, malformed);
    }

    [Fact(DisplayName = "BR-CV-001: the stretches of a recording take no answer from a person: nothing puts them, and nothing withdraws an answer")]
    public async Task TheStretchesTakeNoAnswer()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        await feature.DetectedAsync(recording.Id, [], (0, 1800, SegmentKind.Programme, 0.97));
        int keeps = feature.Detections.Keeps;

        (HttpStatusCode put, _) = await feature.PutAsync(
            RecordingSegmentsFeature.Path(recording.Id),
            """{"segments": [{"startsAtSec": 0, "endsAtSec": 1800, "kind": "programme"}], "confirmed": true}""");
        (HttpStatusCode withdraw, _) = await feature.DeleteAsync($"{RecordingSegmentsFeature.Path(recording.Id)}/answer");
        JsonElement data = (await feature.GetAsync(RecordingSegmentsFeature.Path(recording.Id))).Body.GetProperty("data");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, put);
        Assert.Equal(HttpStatusCode.NotFound, withdraw);
        Assert.Equal(("detected", "nothingFound"), (data.GetProperty("source").GetString(), data.GetProperty("state").GetString()));
        Assert.Equal(keeps, feature.Detections.Keeps);
    }

    [Fact(DisplayName = "a caller who has not signed in cannot read the stretches of a recording")]
    public async Task ACallerWhoHasNotSignedInCannotRead()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        using HttpClient anonymous = feature.Anonymous();

        (HttpStatusCode status, _) = await feature.GetAsync(RecordingSegmentsFeature.Path(recording.Id), anonymous);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal(0, feature.Versions.Reads);
    }
}
