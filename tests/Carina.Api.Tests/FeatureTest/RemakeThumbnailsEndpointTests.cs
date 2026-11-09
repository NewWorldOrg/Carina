using System.Net;
using System.Text.Json;

using Carina.Domain.Recordings;
using Carina.Infrastructure.Thumbnails;

namespace Carina.Api.Tests.FeatureTest;

public sealed class RemakeThumbnailsEndpointTests
{
    private const string Path = "/api/recordings/thumbnails/remake";

    [Fact(DisplayName = "BR-KD-012: each id is taken on its own through the single recording's path, the answer holds one row per id in the order asked, and a refusal of one leaves the rest drawn")]
    public async Task EachIdIsTakenOnItsOwnAndAnsweredInTheOrderAsked()
    {
        await using var feature = new RecordingFeature();
        Recording drawn = Ended(feature);
        Recording skipped = Ended(feature, 4002);
        Recording failed = Ended(feature, 4003);
        Recording outOfReach = Ended(feature, 4004);
        Recording unknownToThePass = Ended(feature, 4005);
        Recording writing = feature.Held(eventId: 4006);
        string unknown = RecordingId.New().Wire;
        feature.Remaker.AnswersByRecording[skipped.Id] = ThumbnailRemake.Skipped;
        feature.Remaker.AnswersByRecording[failed.Id] = ThumbnailRemake.Failed;
        feature.Remaker.AnswersByRecording[outOfReach.Id] = ThumbnailRemake.OutOfReach;
        feature.Remaker.AnswersByRecording[unknownToThePass.Id] = ThumbnailRemake.NothingToAskAbout;

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(Path, new
        {
            ids = new[]
            {
                drawn.Id.Wire,
                unknown,
                writing.Id.Wire,
                skipped.Id.Wire,
                failed.Id.Wire,
                outOfReach.Id.Wire,
                unknownToThePass.Id.Wire,
                drawn.Id.Wire,
            },
        });
        JsonElement[] rows = [.. body.GetProperty("data").GetProperty("items").EnumerateArray()];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [drawn.Id.Wire, unknown, writing.Id.Wire, skipped.Id.Wire, failed.Id.Wire, outOfReach.Id.Wire, unknownToThePass.Id.Wire, drawn.Id.Wire],
            rows.Select(row => row.GetProperty("id").GetString()!).ToArray());
        Assert.Equal(
            ["drawn", "noSuchRecording", "stillRecording", "skipped", "failed", "refused", "noSuchRecording", "drawn"],
            rows.Select(row => row.GetProperty("outcome").GetString()!).ToArray());
        Assert.Equal(
            [drawn.Id, skipped.Id, failed.Id, outOfReach.Id, unknownToThePass.Id, drawn.Id],
            feature.Remaker.Asked);
        Assert.Equal(ThumbnailState.Ready, drawn.ThumbnailState);
        Assert.Equal(ThumbnailState.Skipped, skipped.ThumbnailState);
        Assert.Equal(ThumbnailState.Failed, failed.ThumbnailState);
    }

    [Fact(DisplayName = "a row that drew a picture says nothing beside the outcome, and one that did not says why without naming what was sent")]
    public async Task ARowThatDrewNothingSaysWhyWithoutNamingWhatWasSent()
    {
        await using var feature = new RecordingFeature();
        Recording drawn = Ended(feature);
        Recording writing = feature.Held(eventId: 4002);
        Recording nowhere = Ended(feature, 4003);
        string unknown = RecordingId.New().Wire;
        feature.Remaker.AnswersByRecording[nowhere.Id] = ThumbnailRemake.NowhereToPutThem;

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            Path,
            new { ids = new[] { drawn.Id.Wire, writing.Id.Wire, unknown, nowhere.Id.Wire } });
        JsonElement[] rows = [.. body.GetProperty("data").GetProperty("items").EnumerateArray()];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["drawn", "stillRecording", "noSuchRecording", "refused"], rows.Select(row => row.GetProperty("outcome").GetString()!).ToArray());
        Assert.Equal(string.Empty, rows[0].GetProperty("message").GetString());
        Assert.All(rows.Skip(1), row => Assert.NotEmpty(row.GetProperty("message").GetString()!));
        Assert.DoesNotContain(writing.Id.Wire, rows[1].GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(unknown, rows[2].GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("thumbnails", rows[3].GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(ThumbnailState.Pending, writing.ThumbnailState);
    }

    [Fact(DisplayName = "BR-KD-012: more than a hundred ids, none, or an unreadable one is refused whole, and no picture is asked for")]
    public async Task MoreThanAHundredIdsOrNoneOrAnUnreadableOneIsRefusedWhole()
    {
        await using var feature = new RecordingFeature();
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
        Assert.Empty(feature.Remaker.Asked);
    }

    [Fact(DisplayName = "exactly a hundred ids are taken, and a recording named twice is drawn twice")]
    public async Task ExactlyAHundredIdsAreTaken()
    {
        await using var feature = new RecordingFeature();
        Recording ended = Ended(feature);
        string[] aHundred = [.. Enumerable.Repeat(ended.Id.Wire, 100)];

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(Path, new { ids = aHundred });
        string?[] outcomes = [.. body.GetProperty("data").GetProperty("items").EnumerateArray().Select(row => row.GetProperty("outcome").GetString())];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(100, outcomes.Length);
        Assert.All(outcomes, outcome => Assert.Equal("drawn", outcome));
        Assert.Equal(100, feature.Remaker.Asked.Count);
    }

    [Fact(DisplayName = "an id that is not thirty-two hexadecimal digits is refused as unreadable, without repeating it")]
    public async Task AnIdThatIsNotARecordingIdIsRefusedAsUnreadable()
    {
        await using var feature = new RecordingFeature();
        using var content = new StringContent("""{"ids":["not-an-id"]}""", System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await feature.Client.PostAsync(new Uri(Path, UriKind.Relative), content);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ids", body, StringComparison.Ordinal);
        Assert.DoesNotContain("not-an-id", body, StringComparison.Ordinal);
        Assert.Empty(feature.Remaker.Asked);
    }

    [Fact(DisplayName = "BR-EA2-001: a request from another origin, or by a caller who has not signed in, is refused and no picture is asked for")]
    public async Task ARequestFromAnotherOriginOrNoSessionIsRefused()
    {
        await using var feature = new RecordingFeature();
        Recording ended = Ended(feature);
        using HttpClient elsewhere = feature.FromElsewhere();
        using HttpClient anonymous = feature.Anonymous();

        (HttpStatusCode fromElsewhere, _) = await feature.PostAsync(Path, new { ids = new[] { ended.Id.Wire } }, elsewhere);
        (HttpStatusCode signedOut, _) = await feature.PostAsync(Path, new { ids = new[] { ended.Id.Wire } }, anonymous);

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized), (fromElsewhere, signedOut));
        Assert.Empty(feature.Remaker.Asked);
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
