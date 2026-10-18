using System.Net;
using System.Text;
using System.Text.Json;

using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Api.Tests.FeatureTest;

public sealed class RecordingSegmentAnswerEndpointTests
{
    private const string ThreeStretches =
        """
        {
          "segments": [
            { "startsAtSec": 0, "endsAtSec": 912.3, "kind": "programme" },
            { "startsAtSec": 912.3, "endsAtSec": 1002.3, "kind": "break" },
            { "startsAtSec": 1002.3, "endsAtSec": 1800, "kind": "programme" }
          ],
          "confirmed": true
        }
        """;

    [Fact(DisplayName = "BR-CS-002: an answer is kept and answered in the shape the stretches are read in, standing over the detection: no version, no probability, and the detection's boundary candidates")]
    public async Task AnAnswerIsKeptAndStandsOverTheDetection()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        await feature.DetectedAsync(recording.Id, [TimeSpan.FromSeconds(912.28), TimeSpan.FromSeconds(1002.3)], (0, 300, SegmentKind.Programme, 0.9), (300, 360, SegmentKind.Break, 0.6), (360, 1800, SegmentKind.Programme, 0.9));

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(RecordingSegmentsFeature.Path(recording.Id), ThreeStretches);
        (_, JsonElement read) = await feature.GetAsync(RecordingSegmentsFeature.Path(recording.Id));
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(data.GetRawText(), read.GetProperty("data").GetRawText());
        Assert.Equal(("answered", "answered"), (data.GetProperty("source").GetString(), data.GetProperty("state").GetString()));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("modelVersion").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("detectedAt").ValueKind);
        Assert.True(data.GetProperty("confirmed").GetBoolean());
        Assert.Equal(feature.Clock.Now, data.GetProperty("answeredAt").GetDateTime().ToUniversalTime());
        Assert.Equal(0.05, data.GetProperty("cmShare").GetDouble());
        Assert.Equal(1800, data.GetProperty("lengthSec").GetDouble());
        Assert.Equal(
            [(0d, 912.3d, "programme"), (912.3d, 1002.3d, "break"), (1002.3d, 1800d, "programme")],
            data.GetProperty("segments").EnumerateArray().Select(segment => (
                segment.GetProperty("startsAtSec").GetDouble(),
                segment.GetProperty("endsAtSec").GetDouble(),
                segment.GetProperty("kind").GetString()!)));
        Assert.All(data.GetProperty("segments").EnumerateArray(), segment => Assert.Equal(JsonValueKind.Null, segment.GetProperty("probability").ValueKind));
        Assert.Equal([912.28, 1002.3], data.GetProperty("snaps").EnumerateArray().Select(snap => snap.GetDouble()));
    }

    [Fact(DisplayName = "BR-CV-001: an answer may be sent for a recording with no detection, unconfirmed, and two stretches of one kind side by side are kept as one")]
    public async Task AnAnswerMayBeSentWithNoDetection()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            RecordingSegmentsFeature.Path(recording.Id),
            """{"segments":[{"startsAtSec":0,"endsAtSec":600,"kind":"programme"},{"startsAtSec":600,"endsAtSec":1800,"kind":"programme"}],"confirmed":false}""");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(data.GetProperty("confirmed").GetBoolean());
        Assert.Equal(0, data.GetProperty("cmShare").GetDouble());
        Assert.Single(data.GetProperty("segments").EnumerateArray());
        Assert.Empty(data.GetProperty("snaps").EnumerateArray());
        Assert.Equal((false, (DateTime?)null), (feature.Answers.Of(recording.Id)!.Confirmed, feature.Answers.Of(recording.Id)!.ConfirmedAt));
    }

    [Theory(DisplayName = "BR-CV-001: stretches that do not cover the recording as an answer must are refused by what is wrong alone, and nothing is kept")]
    [InlineData("""[{"startsAtSec":0,"endsAtSec":900,"kind":"programme"},{"startsAtSec":901,"endsAtSec":1800,"kind":"break"}]""", "segments: a gap between segments")]
    [InlineData("""[]""", "segments: a gap between segments")]
    [InlineData("""[{"startsAtSec":0,"endsAtSec":901,"kind":"programme"},{"startsAtSec":900,"endsAtSec":1800,"kind":"break"}]""", "segments: segments overlap")]
    [InlineData("""[{"startsAtSec":0,"endsAtSec":1800.5,"kind":"programme"}]""", "segments: outside the recording")]
    [InlineData("""[{"startsAtSec":-1,"endsAtSec":1800,"kind":"programme"}]""", "segments: outside the recording")]
    [InlineData("""[{"startsAtSec":0,"endsAtSec":60,"kind":"opening"},{"startsAtSec":60,"endsAtSec":1800,"kind":"programme"}]""", "segments: unknown kind")]
    [InlineData("""[{"startsAtSec":0,"endsAtSec":60,"kind":"Break"},{"startsAtSec":60,"endsAtSec":1800,"kind":"programme"}]""", "segments: unknown kind")]
    [InlineData("""[{"startsAtSec":0,"endsAtSec":1800}]""", "segments: unknown kind")]
    [InlineData("""[null]""", "segments: unknown kind")]
    [InlineData("""[{"startsAtSec":0,"endsAtSec":900,"kind":"programme"},{"startsAtSec":900,"endsAtSec":900,"kind":"break"},{"startsAtSec":900,"endsAtSec":1800,"kind":"programme"}]""", "segments: a segment ends before it starts")]
    public async Task StretchesThatDoNotCoverTheRecordingAreRefused(string segments, string message)
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(RecordingSegmentsFeature.Path(recording.Id), $$"""{"segments":{{segments}},"confirmed":true}""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(message, body.GetProperty("message").GetString());
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.Equal(0, feature.Answers.Writes);
    }

    [Fact(DisplayName = "BR-CV-001: more than two hundred stretches an hour are refused as too many")]
    public async Task MoreThanTwoHundredStretchesAnHourAreRefused()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        StringBuilder segments = new();

        for (int at = 0; at < 101; at++)
        {
            string kind = at % 2 == 0 ? "programme" : "break";
            string ends = at == 100 ? "1800" : $"{(at + 1) * 17}";
            segments.Append(at == 0 ? string.Empty : ",").Append($$"""{"startsAtSec":{{at * 17}},"endsAtSec":{{ends}},"kind":"{{kind}}"}""");
        }

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(RecordingSegmentsFeature.Path(recording.Id), $$"""{"segments":[{{segments}}],"confirmed":false}""");

        Assert.Equal((HttpStatusCode.BadRequest, "segments: too many segments"), (status, body.GetProperty("message").GetString()));
        Assert.Equal(0, feature.Answers.Writes);
    }

    [Theory(DisplayName = "an answer that does not say whether it is confirmed, names something an answer does not carry, or leaves out where a stretch starts is refused")]
    [InlineData("""{"segments":[{"startsAtSec":0,"endsAtSec":1800,"kind":"programme"}]}""", "confirmed")]
    [InlineData("""{"segments":[{"startsAtSec":0,"endsAtSec":1800,"kind":"programme"}],"confirmed":true,"modelVersion":1}""", "modelVersion")]
    [InlineData("""{"segments":[{"startsAtSec":0,"endsAtSec":1800,"kind":"programme","probability":1}],"confirmed":true}""", "probability")]
    [InlineData("""{"segments":[{"endsAtSec":1800,"kind":"programme"}],"confirmed":true}""", "segments")]
    public async Task AnAnswerThatCannotBeReadIsRefused(string json, string named)
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(RecordingSegmentsFeature.Path(recording.Id), json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(named, body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(0, feature.Answers.Writes);
    }

    [Fact(DisplayName = "BR-CV-001: a recording still being written takes no answer")]
    public async Task ARecordingStillBeingWrittenTakesNoAnswer()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.StillWriting();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            RecordingSegmentsFeature.Path(recording.Id),
            """{"segments":[{"startsAtSec":0,"endsAtSec":750,"kind":"programme"}],"confirmed":true}""");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal(0, feature.Answers.Writes);
    }

    [Fact(DisplayName = "a recording whose row is gone takes no answer and has none to withdraw, whatever is kept of it")]
    public async Task ARecordingWhoseRowIsGoneTakesNoAnswer()
    {
        await using var feature = new RecordingSegmentsFeature();
        RecordingId gone = RecordingId.New();

        (HttpStatusCode answered, _) = await feature.PutAsync(RecordingSegmentsFeature.Path(gone), ThreeStretches);
        (HttpStatusCode withdrawn, _) = await feature.DeleteAsync(RecordingSegmentsFeature.AnswerPath(gone));
        (HttpStatusCode malformed, _) = await feature.PutAsync("/api/recordings/not-an-id/segments", ThreeStretches);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.BadRequest), (answered, withdrawn, malformed));
        Assert.Equal(0, feature.Answers.Writes);
    }

    [Fact(DisplayName = "BR-CS-002: withdrawing the answer puts the recording back to its detection, keeps the answer's row, and a second withdrawal finds none standing")]
    public async Task WithdrawingTheAnswerPutsTheRecordingBackToItsDetection()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        await feature.DetectedAsync(recording.Id, [], (0, 300, SegmentKind.Programme, 0.9), (300, 360, SegmentKind.Break, 0.8), (360, 1800, SegmentKind.Programme, 0.9));
        await feature.PutAsync(RecordingSegmentsFeature.Path(recording.Id), ThreeStretches);
        feature.Clock.Now = feature.Clock.Now.AddMinutes(5);

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync(RecordingSegmentsFeature.AnswerPath(recording.Id));
        (_, JsonElement read) = await feature.GetAsync(RecordingSegmentsFeature.Path(recording.Id));
        (HttpStatusCode again, JsonElement refused) = await feature.DeleteAsync(RecordingSegmentsFeature.AnswerPath(recording.Id));
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(data.GetRawText(), read.GetProperty("data").GetRawText());
        Assert.Equal(("detected", "marked", 1), (data.GetProperty("source").GetString(), data.GetProperty("state").GetString(), data.GetProperty("modelVersion").GetInt32()));
        Assert.False(data.GetProperty("confirmed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("answeredAt").ValueKind);
        Assert.Equal([300d, 360d], data.GetProperty("segments").EnumerateArray().Skip(1).Select(segment => segment.GetProperty("startsAtSec").GetDouble()));
        Assert.Equal(HttpStatusCode.NotFound, again);
        Assert.False(refused.GetProperty("status").GetBoolean());
        RecordingAnswer kept = feature.Answers.Of(recording.Id)!;
        Assert.Equal(((DateTime?)feature.Clock.Now, false, 3), (kept.WithdrawnAt, kept.Stands, kept.Segments.Count));
    }

    [Fact(DisplayName = "BR-CS-002: withdrawing the answer of a recording not detected leaves it standing with nothing, and an answer sent again stands once more, counted")]
    public async Task AnAnswerSentAfterItsWithdrawalStandsAgain()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        (HttpStatusCode nothingToWithdraw, _) = await feature.DeleteAsync(RecordingSegmentsFeature.AnswerPath(recording.Id));
        await feature.PutAsync(RecordingSegmentsFeature.Path(recording.Id), ThreeStretches);

        (_, JsonElement withdrawn) = await feature.DeleteAsync(RecordingSegmentsFeature.AnswerPath(recording.Id));
        (HttpStatusCode status, JsonElement answered) = await feature.PutAsync(RecordingSegmentsFeature.Path(recording.Id), ThreeStretches);

        Assert.Equal(HttpStatusCode.NotFound, nothingToWithdraw);
        Assert.Equal(("none", "none"), (withdrawn.GetProperty("data").GetProperty("source").GetString(), withdrawn.GetProperty("data").GetProperty("state").GetString()));
        Assert.Empty(withdrawn.GetProperty("data").GetProperty("segments").EnumerateArray());
        Assert.Equal((HttpStatusCode.OK, "answered"), (status, answered.GetProperty("data").GetProperty("source").GetString()));
        Assert.Equal((2, true), (feature.Answers.Of(recording.Id)!.Answers, feature.Answers.Of(recording.Id)!.Stands));
    }

    [Fact(DisplayName = "BR-CA-001: an answer or a withdrawal sent from another origin is refused and writes nothing, and so is an answer sent as a form")]
    public async Task AWriteFromAnotherOriginIsRefused()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        await feature.PutAsync(RecordingSegmentsFeature.Path(recording.Id), ThreeStretches);
        using HttpClient elsewhere = feature.FromElsewhere();
        using var form = new StringContent("confirmed=true", Encoding.UTF8, "application/x-www-form-urlencoded");

        (HttpStatusCode answered, _) = await feature.PutAsync(RecordingSegmentsFeature.Path(recording.Id), ThreeStretches, elsewhere);
        (HttpStatusCode withdrawn, _) = await feature.DeleteAsync(RecordingSegmentsFeature.AnswerPath(recording.Id), elsewhere);
        using HttpResponseMessage formed = await feature.Client.PutAsync(new Uri(RecordingSegmentsFeature.Path(recording.Id), UriKind.Relative), form);

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (answered, withdrawn));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, formed.StatusCode);
        Assert.Equal(1, feature.Answers.Writes);
        Assert.True(feature.Answers.Of(recording.Id)!.Stands);
    }

    [Fact(DisplayName = "a caller who has not signed in can neither answer nor withdraw")]
    public async Task ACallerWhoHasNotSignedInCannotWrite()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        using HttpClient anonymous = feature.Anonymous();

        (HttpStatusCode answered, _) = await feature.PutAsync(RecordingSegmentsFeature.Path(recording.Id), ThreeStretches, anonymous);
        (HttpStatusCode withdrawn, _) = await feature.DeleteAsync(RecordingSegmentsFeature.AnswerPath(recording.Id), anonymous);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (answered, withdrawn));
        Assert.Equal(0, feature.Answers.Writes);
    }
}
