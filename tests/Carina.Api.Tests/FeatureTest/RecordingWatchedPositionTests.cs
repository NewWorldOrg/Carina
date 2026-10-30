using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Domain.Auth;
using Carina.Domain.Recordings;
using Carina.Domain.Viewing;

namespace Carina.Api.Tests.FeatureTest;

public sealed class RecordingWatchedPositionTests
{
    [Fact(DisplayName = "every row of the library says how far this viewer watched its recording")]
    public async Task EveryRowOfTheLibrarySaysHowFarThisViewerWatchedItsRecording()
    {
        await using var feature = new RecordingFeature();
        Recording watched = Ended(feature, eventId: 1);
        Recording unwatched = Ended(feature, eventId: 2);
        Recording sharedWithSomeoneElse = Ended(feature, eventId: 3);

        feature.Positions.Positions.Add(Reached(watched, TestAuthenticationHandler.Tester, 612.5));
        feature.Positions.Positions.Add(Reached(sharedWithSomeoneElse, "someone else", 900));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");
        Dictionary<string, JsonElement> rows = body
            .GetProperty("data")
            .GetProperty("items")
            .EnumerateArray()
            .ToDictionary(row => row.GetProperty("id").GetString()!, StringComparer.Ordinal);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(612.5, rows[watched.Id.Wire].GetProperty("watchedPositionSec").GetDouble());
        Assert.Equal(JsonValueKind.Null, rows[unwatched.Id.Wire].GetProperty("watchedPositionSec").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows[sharedWithSomeoneElse.Id.Wire].GetProperty("watchedPositionSec").ValueKind);
    }

    [Fact(DisplayName = "the places of a whole page are read in one question, however many rows it has")]
    public async Task ThePlacesOfAWholePageAreReadInOneQuestion()
    {
        await using var feature = new RecordingFeature();

        foreach (int eventId in Enumerable.Range(10, 5))
        {
            Recording recording = Ended(feature, eventId);
            feature.Positions.Positions.Add(Reached(recording, TestAuthenticationHandler.Tester, eventId));
        }

        (HttpStatusCode status, _) = await feature.GetAsync("/api/recordings");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, feature.Positions.ReadsOfMany);
        Assert.Equal(0, feature.Positions.ReadsOfOne);
    }

    [Fact(DisplayName = "a place a player has just sent is the place the library shows")]
    public async Task APlaceAPlayerHasJustSentIsThePlaceTheLibraryShows()
    {
        await using var feature = new RecordingFeature();
        Recording recording = Ended(feature, eventId: 20);

        using HttpResponseMessage kept = await feature.Client.PutAsJsonAsync(
            new Uri($"/api/videos/{recording.Id.Wire}/position", UriKind.Relative),
            new { positionSec = 61.5 });

        (_, JsonElement body) = await feature.GetAsync("/api/recordings");

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal(
            61.5,
            Assert.Single(body.GetProperty("data").GetProperty("items").EnumerateArray())
                .GetProperty("watchedPositionSec")
                .GetDouble());
    }

    [Fact(DisplayName = "an empty library asks for no places at all")]
    public async Task AnEmptyLibraryAsksForNoPlacesAtAll()
    {
        await using var feature = new RecordingFeature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/recordings");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, feature.Positions.ReadsOfMany);
    }

    [Fact(DisplayName = "one recording's detail says how far this viewer watched it, in one lookup")]
    public async Task OneRecordingsDetailSaysHowFarThisViewerWatchedIt()
    {
        await using var feature = new RecordingFeature();
        Recording watched = Ended(feature, eventId: 30);
        Recording unwatched = Ended(feature, eventId: 31);
        Recording sharedWithSomeoneElse = Ended(feature, eventId: 32);

        feature.Positions.Positions.Add(Reached(watched, TestAuthenticationHandler.Tester, 612.5));
        feature.Positions.Positions.Add(Reached(sharedWithSomeoneElse, "someone else", 900));

        JsonElement seen = await DetailOfAsync(feature, watched);

        Assert.Equal(612.5, seen.GetProperty("watchedPositionSec").GetDouble());
        Assert.Equal(1, feature.Positions.ReadsOfOne);
        Assert.Equal(0, feature.Positions.ReadsOfMany);
        Assert.Equal(JsonValueKind.Null, (await DetailOfAsync(feature, unwatched)).GetProperty("watchedPositionSec").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            (await DetailOfAsync(feature, sharedWithSomeoneElse)).GetProperty("watchedPositionSec").ValueKind);
    }

    [Fact(DisplayName = "the answer to a stop says how far this viewer watched the recording, as the detail does")]
    public async Task TheAnswerToAStopSaysHowFarThisViewerWatchedTheRecording()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        feature.Driver.Writing(Carina.Contracts.SessionId.Parse("session-a"), recording.Id.Wire);
        feature.Positions.Positions.Add(Reached(recording, TestAuthenticationHandler.Tester, 42));

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/recordings/{recording.Id.Wire}/stop",
            new { reason = "the wrong programme" });

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(
            42,
            body.GetProperty("data").GetProperty("recording").GetProperty("recording")
                .GetProperty("watchedPositionSec").GetDouble());
    }

    private static async Task<JsonElement> DetailOfAsync(RecordingFeature feature, Recording recording)
    {
        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");

        Assert.Equal(HttpStatusCode.OK, status);

        return body.GetProperty("data").GetProperty("recording");
    }

    private static PlaybackPosition Reached(Recording recording, string viewer, double seconds)
        => PlaybackPosition.Reached(
            recording.Id,
            new Subject(viewer),
            TimeSpan.FromSeconds(seconds),
            RecordingFeature.Noon.AddHours(2));

    private static Recording Ended(RecordingFeature feature, int eventId)
    {
        Recording recording = feature.Held(eventId: eventId);
        recording.Wrote(TimeSpan.FromHours(1));
        recording.Abort(RecordingFeature.Noon.AddHours(1));
        recording.Settle(RecordingOutcome.Complete, 1_000_000, RecordingFeature.Noon.AddHours(1));

        return recording;
    }
}
