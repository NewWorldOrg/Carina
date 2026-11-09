using System.Net;
using System.Text;
using System.Text.Json;

using Carina.Domain.Recordings;
using Carina.Infrastructure.Thumbnails;

namespace Carina.Api.Tests.FeatureTest;

public sealed class RemakeThumbnailsEndpointTests
{
    private const string Path = "/api/recordings/thumbnails/remake";

    [Fact(DisplayName = "BR-KD-012: each id is checked on its own as the single recording's action checks it, put back in the pass's queue rather than drawn here, and answered in the order asked")]
    public async Task EachIdIsQueuedOnItsOwnAndAnsweredInTheOrderAsked()
    {
        await using RecordingFeature feature = new();
        Recording queued = Ended(feature);
        Recording nowhere = Ended(feature, 4002);
        Recording unknownToThePass = Ended(feature, 4003);
        Recording writing = feature.Held(eventId: 4004);
        string unknown = RecordingId.New().Wire;
        feature.Remaker.AskAgainAnswersByRecording[nowhere.Id] = ThumbnailAskedAgain.NowhereToPutThem;
        feature.Remaker.AskAgainAnswersByRecording[unknownToThePass.Id] = ThumbnailAskedAgain.NothingToAskAbout;

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(Path, new
        {
            ids = new[] { queued.Id.Wire, unknown, writing.Id.Wire, nowhere.Id.Wire, unknownToThePass.Id.Wire, queued.Id.Wire },
        });
        JsonElement[] rows = [.. body.GetProperty("data").GetProperty("items").EnumerateArray()];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [queued.Id.Wire, unknown, writing.Id.Wire, nowhere.Id.Wire, unknownToThePass.Id.Wire, queued.Id.Wire],
            rows.Select(row => row.GetProperty("id").GetString()!).ToArray());
        Assert.Equal(
            ["queued", "noSuchRecording", "stillRecording", "refused", "noSuchRecording", "queued"],
            rows.Select(row => row.GetProperty("outcome").GetString()!).ToArray());
        Assert.Equal([queued.Id, nowhere.Id, unknownToThePass.Id, queued.Id], feature.Remaker.AskedAgain);
        Assert.Empty(feature.Remaker.Asked);
        Assert.Equal(ThumbnailState.Pending, queued.ThumbnailState);
    }

    [Fact(DisplayName = "a row that queued the recording says nothing beside the outcome, and one that did not says why without naming what was sent")]
    public async Task ARowThatQueuedNothingSaysWhyWithoutNamingWhatWasSent()
    {
        await using RecordingFeature feature = new();
        Recording queued = Ended(feature);
        Recording writing = feature.Held(eventId: 4002);
        Recording nowhere = Ended(feature, 4003);
        string unknown = RecordingId.New().Wire;
        feature.Remaker.AskAgainAnswersByRecording[nowhere.Id] = ThumbnailAskedAgain.NowhereToPutThem;

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            Path,
            new { ids = new[] { queued.Id.Wire, writing.Id.Wire, unknown, nowhere.Id.Wire } });
        JsonElement[] rows = [.. body.GetProperty("data").GetProperty("items").EnumerateArray()];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["queued", "stillRecording", "noSuchRecording", "refused"], rows.Select(row => row.GetProperty("outcome").GetString()!).ToArray());
        Assert.Equal(string.Empty, rows[0].GetProperty("message").GetString());
        Assert.All(rows.Skip(1), row => Assert.NotEmpty(row.GetProperty("message").GetString()!));
        Assert.DoesNotContain(writing.Id.Wire, rows[1].GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(unknown, rows[2].GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("thumbnails", rows[3].GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(ThumbnailState.Pending, writing.ThumbnailState);
        Assert.DoesNotContain(writing.Id, feature.Remaker.AskedAgain);
    }

    [Fact(DisplayName = "a store that throws for one recording costs that row alone: it is answered as refused with a fixed message, and the rows after it are still queued")]
    public async Task AStoreThatThrowsForOneRecordingCostsThatRowAlone()
    {
        await using RecordingFeature feature = new();
        Recording before = Ended(feature);
        Recording broken = Ended(feature, 4002);
        Recording after = Ended(feature, 4003);
        feature.Remaker.ThrowsWhenAskedAgainFor.Add(broken.Id);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            Path,
            new { ids = new[] { before.Id.Wire, broken.Id.Wire, after.Id.Wire } });
        JsonElement[] rows = [.. body.GetProperty("data").GetProperty("items").EnumerateArray()];
        string said = rows[1].GetProperty("message").GetString()!;

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["queued", "refused", "queued"], rows.Select(row => row.GetProperty("outcome").GetString()!).ToArray());
        Assert.NotEmpty(said);
        Assert.DoesNotContain("store went away", said, StringComparison.Ordinal);
        Assert.DoesNotContain(broken.Id.Wire, said, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([before.Id, broken.Id, after.Id], feature.Remaker.AskedAgain);
        Assert.Equal(ThumbnailState.Pending, after.ThumbnailState);
    }

    [Fact(DisplayName = "BR-KD-012: more than a hundred ids, none, or an unreadable one is refused whole, and nothing is queued")]
    public async Task MoreThanAHundredIdsOrNoneOrAnUnreadableOneIsRefusedWhole()
    {
        await using RecordingFeature feature = new();
        Recording ended = Ended(feature);
        string[] aHundredAndOne = [.. Enumerable.Repeat(ended.Id.Wire, 101)];

        (HttpStatusCode tooMany, JsonElement said) = await feature.PostAsync(Path, new { ids = aHundredAndOne });
        (HttpStatusCode none, _) = await feature.PostAsync(Path, new { ids = Array.Empty<string>() });
        (HttpStatusCode unnamed, _) = await feature.PostAsync(Path, new { });
        (HttpStatusCode zeroes, _) = await feature.PostAsync(Path, new { ids = new[] { ended.Id.Wire, Guid.Empty.ToString("N") } });
        (HttpStatusCode hyphenated, _) = await feature.PostAsync(Path, new { ids = new[] { ended.Id.Value.ToString("D") } });

        Assert.Equal(HttpStatusCode.BadRequest, tooMany);
        Assert.Equal(HttpStatusCode.BadRequest, none);
        Assert.Equal(HttpStatusCode.BadRequest, unnamed);
        Assert.Equal(HttpStatusCode.BadRequest, zeroes);
        Assert.Equal(HttpStatusCode.BadRequest, hyphenated);
        Assert.Contains("ids", said.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(feature.Remaker.AskedAgain);
    }

    [Fact(DisplayName = "exactly a hundred ids are taken, and a recording named twice is queued twice")]
    public async Task ExactlyAHundredIdsAreTaken()
    {
        await using RecordingFeature feature = new();
        Recording ended = Ended(feature);
        string[] aHundred = [.. Enumerable.Repeat(ended.Id.Wire, 100)];

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(Path, new { ids = aHundred });
        string?[] outcomes = [.. body.GetProperty("data").GetProperty("items").EnumerateArray().Select(row => row.GetProperty("outcome").GetString())];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(100, outcomes.Length);
        Assert.All(outcomes, outcome => Assert.Equal("queued", outcome));
        Assert.Equal(100, feature.Remaker.AskedAgain.Count);
    }

    [Fact(DisplayName = "an id that is not thirty-two hexadecimal digits is refused as unreadable, without repeating it")]
    public async Task AnIdThatIsNotARecordingIdIsRefusedAsUnreadable()
    {
        await using RecordingFeature feature = new();
        using StringContent content = new("""{"ids":["not-an-id"]}""", Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await feature.Client.PostAsync(new Uri(Path, UriKind.Relative), content);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ids", body, StringComparison.Ordinal);
        Assert.DoesNotContain("not-an-id", body, StringComparison.Ordinal);
        Assert.Empty(feature.Remaker.AskedAgain);
    }

    [Fact(DisplayName = "BR-EA2-001: a request from another origin, or by a caller who has not signed in, is refused and nothing is queued")]
    public async Task ARequestFromAnotherOriginOrNoSessionIsRefused()
    {
        await using RecordingFeature feature = new();
        Recording ended = Ended(feature);
        using HttpClient elsewhere = feature.FromElsewhere();
        using HttpClient anonymous = feature.Anonymous();

        (HttpStatusCode fromElsewhere, _) = await feature.PostAsync(Path, new { ids = new[] { ended.Id.Wire } }, elsewhere);
        (HttpStatusCode signedOut, _) = await feature.PostAsync(Path, new { ids = new[] { ended.Id.Wire } }, anonymous);

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized), (fromElsewhere, signedOut));
        Assert.Empty(feature.Remaker.AskedAgain);
    }

    private static Recording Ended(RecordingFeature feature, int eventId = 4001)
    {
        Recording recording = feature.Held(eventId: eventId);

        recording.Wrote(TimeSpan.FromHours(1));
        recording.Abort(RecordingFeature.Noon.AddHours(1));
        recording.Settle(RecordingOutcome.Complete, 1_000, RecordingFeature.Noon.AddHours(1));

        return recording;
    }
}
