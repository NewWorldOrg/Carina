using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Playback;
using Carina.Domain.Auth;
using Carina.Domain.Recordings;
using Carina.Domain.Viewing;

namespace Carina.Api.Tests.FeatureTest;

public sealed class PlaybackPositionEndpointTests
{
    private static readonly DateTime Noon = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "a recording nobody has watched is played from its beginning")]
    public async Task ARecordingNobodyHasWatchedIsPlayedFromItsBeginning()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using HttpResponseMessage answer = await feature.PictureAsync(recording);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(TimeSpan.Zero, Assert.Single(feature.Player.AskedFrom));
        Assert.Equal("0", Header(answer, PlaybackHeaders.StartsAt));
    }

    [Fact(DisplayName = "playing without saying where starts where this viewer left this recording")]
    public async Task PlayingWithoutSayingWhereStartsWhereThisViewerLeftIt()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        (HttpStatusCode kept, _) = await KeepAsync(feature.Client, recording.Id.Wire, new { positionSec = 600 });

        using HttpResponseMessage answer = await feature.PictureAsync(recording);

        Assert.Equal(HttpStatusCode.OK, kept);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(600), Assert.Single(feature.Player.AskedFrom));
        Assert.Equal("600", Header(answer, PlaybackHeaders.StartsAt));
    }

    [Fact(DisplayName = "saying where to start wins over where the watching was left")]
    public async Task SayingWhereToStartWinsOverWhereTheWatchingWasLeft()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        await KeepAsync(feature.Client, recording.Id.Wire, new { positionSec = 600 });

        using HttpResponseMessage answer = await feature.PictureAsync(recording, "?from=30");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(feature.Player.AskedFrom));
    }

    [Fact(DisplayName = "asking to start at the beginning is not the same as asking for nothing")]
    public async Task AskingToStartAtTheBeginningIsNotTheSameAsAskingForNothing()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        await KeepAsync(feature.Client, recording.Id.Wire, new { positionSec = 600 });

        using HttpResponseMessage answer = await feature.PictureAsync(recording, "?from=0");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(TimeSpan.Zero, Assert.Single(feature.Player.AskedFrom));
    }

    [Fact(DisplayName = "where another viewer left a recording is not where this one is taken to")]
    public async Task WhereAnotherViewerLeftARecordingIsNotWhereThisOneIsTakenTo()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        feature.Positions.Positions.Add(PlaybackPosition.Reached(
            recording.Id,
            new Subject("someone else"),
            TimeSpan.FromSeconds(900),
            Noon));

        using HttpResponseMessage answer = await feature.PictureAsync(recording);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(TimeSpan.Zero, Assert.Single(feature.Player.AskedFrom));
    }

    [Fact(DisplayName = "the plan says where the watching got to, so a player that seeks itself can go there")]
    public async Task ThePlanSaysWhereTheWatchingGotTo()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using (HttpResponseMessage unwatched = await feature.PlanAsync(recording))
        {
            JsonElement read = (await PlayFeature.PlanOfAsync(unwatched)).GetProperty("data");

            Assert.Equal(JsonValueKind.Null, read.GetProperty("resumeAtSec").ValueKind);
        }

        await KeepAsync(feature.Client, recording.Id.Wire, new { positionSec = 612.5 });

        using HttpResponseMessage watched = await feature.PlanAsync(recording);
        JsonElement plan = (await PlayFeature.PlanOfAsync(watched)).GetProperty("data");

        Assert.Equal(612.5, plan.GetProperty("resumeAtSec").GetDouble());
    }

    [Fact(DisplayName = "the screen is given both numbers it needs: how long a recording is, and where the watching got to")]
    public async Task TheScreenIsGivenBothNumbersItNeeds()
    {
        await using var recordings = new RecordingFeature();
        Recording written = Settled(recordings);

        (HttpStatusCode standing, JsonElement detail) =
            await recordings.GetAsync($"/api/recordings/{written.Id.Wire}");

        await using var feature = new PlayFeature();
        Recording playing = feature.Ended(RecordingOutcome.Complete);

        await KeepAsync(feature.Client, playing.Id.Wire, new { positionSec = 600 });

        using HttpResponseMessage answer = await feature.PlanAsync(playing);
        JsonElement plan = (await PlayFeature.PlanOfAsync(answer)).GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, standing);
        Assert.Equal(
            (long)TimeSpan.FromMinutes(30).TotalMilliseconds,
            detail.GetProperty("data").GetProperty("recording").GetProperty("writtenDurationMs").GetInt64());
        Assert.Equal(600, plan.GetProperty("resumeAtSec").GetDouble());
    }

    [Fact(DisplayName = "where the watching got to is kept once for a recording, whichever of the two it is watched from")]
    public async Task WhereTheWatchingGotToIsTheSameWhicheverOfTheTwoThePlanIsAskedFor()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        await KeepAsync(feature.Client, recording.Id.Wire, new { positionSec = 618.5 });

        using HttpResponseMessage asEncoded = await feature.PlanAsync(recording, "?source=artefact");
        using HttpResponseMessage asRecorded = await feature.PlanAsync(recording, "?source=recording");
        JsonElement artefact = (await PlayFeature.PlanOfAsync(asEncoded)).GetProperty("data");
        JsonElement itself = (await PlayFeature.PlanOfAsync(asRecorded)).GetProperty("data");

        Assert.Equal("artefact", artefact.GetProperty("source").GetString());
        Assert.Equal("recording", itself.GetProperty("source").GetString());
        Assert.Equal(618.5, artefact.GetProperty("resumeAtSec").GetDouble());
        Assert.Equal(618.5, itself.GetProperty("resumeAtSec").GetDouble());
        Assert.Single(feature.Positions.Positions);
    }

    [Fact(DisplayName = "a recording asked for as it was recorded is started where the watching got to, as it is when it is asked for encoded")]
    public async Task ARecordingAskedForAsItWasRecordedIsStartedWhereTheWatchingGotTo()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        await KeepAsync(feature.Client, recording.Id.Wire, new { positionSec = 300 });

        using HttpResponseMessage answer = await feature.PictureAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(300), Assert.Single(feature.Player.AskedFrom));
        Assert.Equal(recording.FileName, Assert.Single(feature.Player.Opened).Name);
    }

    private static Recording Settled(RecordingFeature feature, int eventId = 4_001)
    {
        Recording held = feature.Held(eventId: eventId);

        held.Wrote(TimeSpan.FromMinutes(30));
        held.Abort(RecordingFeature.Noon.AddMinutes(30));
        held.Settle(RecordingOutcome.Complete, 4_000, RecordingFeature.Noon.AddMinutes(30));

        return held;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> KeepAsync(
        HttpClient client,
        string recordingId,
        object body)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            new Uri($"/api/videos/{recordingId}/position", UriKind.Relative),
            body);
        string read = await response.Content.ReadAsStringAsync();

        if (!read.StartsWith('{'))
        {
            return (response.StatusCode, default);
        }

        using var document = JsonDocument.Parse(read);

        return (response.StatusCode, document.RootElement.Clone());
    }

    private static string? Header(HttpResponseMessage answer, string named)
        => answer.Headers.TryGetValues(named, out IEnumerable<string>? values) ? values.Single() : null;
}
