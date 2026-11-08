using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

public sealed class RerunEncodeJobsEndpointTests
{
    private const string Path = "/api/encoding/jobs/retry";

    [Fact(DisplayName = "BR-ED2-008: each id is taken on its own, the answer holds one row per id in the order asked, and a refusal of one leaves the rest queued")]
    public async Task EachIdIsTakenOnItsOwnAndAnsweredInTheOrderAsked()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording recording = feature.Recorded();
        EncodeJob failed = feature.Failed(recording, profile, destination);
        EncodeJob failedBefore = feature.Failed(recording, profile, destination);
        EncodeJob completed = feature.Completed(feature.Recorded(), profile, destination);
        Recording encoded = feature.Recorded();
        feature.Completed(encoded, profile, destination);
        EncodeJob failedBesideAnArtefact = feature.Failed(encoded, profile, destination);
        Recording remade = feature.Recorded();
        feature.Completed(remade, profile, destination);
        EncodeJob failedRemake = feature.Failed(remade, profile, destination, makesItAgain: true);
        Recording gone = feature.Recorded();
        EncodeJob failedOnARecordingGone = feature.Failed(gone, profile, destination);
        feature.Recordings.Recordings.Remove(gone);
        int held = feature.Jobs.Jobs.Count;
        Guid unknown = Guid.NewGuid();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(Path, new
        {
            ids = new[]
            {
                failed.Id.Value,
                unknown,
                completed.Id.Value,
                failedBefore.Id.Value,
                failedBesideAnArtefact.Id.Value,
                failedRemake.Id.Value,
                failedOnARecordingGone.Id.Value,
            },
        });
        JsonElement[] rows = [.. body.GetProperty("data").GetProperty("items").EnumerateArray()];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [failed.Id.Value, unknown, completed.Id.Value, failedBefore.Id.Value, failedBesideAnArtefact.Id.Value, failedRemake.Id.Value, failedOnARecordingGone.Id.Value],
            rows.Select(row => row.GetProperty("id").GetGuid()).ToArray());
        Assert.Equal(
            ["queued", "noSuchJob", "notFailed", "alreadyInTheQueue", "alreadyEncoded", "queued", "refused"],
            rows.Select(row => row.GetProperty("outcome").GetString()!).ToArray());
        Assert.Equal(held + 2, feature.Jobs.Jobs.Count);
    }

    [Fact(DisplayName = "a row that queued a job carries the job as the list would, made from the failed job's recording, profile and destination")]
    public async Task ARowThatQueuedAJobCarriesTheJob()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording recording = feature.Recorded();
        EncodeJob failed = feature.Failed(recording, profile, destination);

        (_, JsonElement body) = await feature.PostAsync(Path, new { ids = new[] { failed.Id.Value } });
        JsonElement row = Assert.Single(body.GetProperty("data").GetProperty("items").EnumerateArray());
        JsonElement job = row.GetProperty("job");
        EncodeJob queued = Assert.Single(feature.Jobs.Jobs, held => held.Status is EncodeJobStatus.Queued);

        Assert.Equal(string.Empty, row.GetProperty("message").GetString());
        Assert.Equal(queued.Id.Value, job.GetProperty("id").GetGuid());
        Assert.Equal("queued", job.GetProperty("status").GetString());
        Assert.Equal(1, job.GetProperty("attempt").GetInt32());
        Assert.Equal(recording.Id.Wire, job.GetProperty("recordingId").GetString());
        Assert.Equal(profile.Id.Value, job.GetProperty("profileId").GetGuid());
        Assert.Equal(destination.Id.Value, job.GetProperty("destinationId").GetGuid());
        Assert.False(queued.MakesItAgain);
        Assert.Equal(EncodeJobStatus.Failed, failed.Status);
    }

    [Fact(DisplayName = "a failed attempt to make an artefact again is run again as one, so it takes the name over as the person first asked")]
    public async Task AFailedRemakeIsRunAgainAsARemake()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording recording = feature.Recorded();
        feature.Completed(recording, profile, destination);
        EncodeJob failedRemake = feature.Failed(recording, profile, destination, makesItAgain: true);

        (_, JsonElement body) = await feature.PostAsync(Path, new { ids = new[] { failedRemake.Id.Value } });
        JsonElement row = Assert.Single(body.GetProperty("data").GetProperty("items").EnumerateArray());
        EncodeJob queued = Assert.Single(feature.Jobs.Jobs, held => held.Status is EncodeJobStatus.Queued);

        Assert.Equal("queued", row.GetProperty("outcome").GetString());
        Assert.True(queued.MakesItAgain);
    }

    [Fact(DisplayName = "a row that queued nothing says why without naming what was sent, and carries no job")]
    public async Task ARowThatQueuedNothingSaysWhyAndCarriesNoJob()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob running = feature.Queued(feature.Recorded(), profile, destination);
        running.Start(EncodingFeature.Noon.AddMinutes(-20));
        EncodeJob cancelled = feature.Queued(feature.Recorded(), profile, destination);
        cancelled.Cancel(EncodingFeature.Noon.AddMinutes(-20));
        Guid unknown = Guid.NewGuid();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(Path, new { ids = new[] { running.Id.Value, cancelled.Id.Value, unknown } });
        JsonElement[] rows = [.. body.GetProperty("data").GetProperty("items").EnumerateArray()];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["notFailed", "notFailed", "noSuchJob"], rows.Select(row => row.GetProperty("outcome").GetString()!).ToArray());
        Assert.All(rows, row => Assert.Equal(JsonValueKind.Null, row.GetProperty("job").ValueKind));
        Assert.All(rows, row => Assert.NotEmpty(row.GetProperty("message").GetString()!));
        Assert.DoesNotContain(unknown.ToString(), rows[2].GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EncodeJobStatus.Running, running.Status);
        Assert.Equal(2, feature.Jobs.Jobs.Count);
    }

    [Fact(DisplayName = "BR-ED2-008 / BR-EV-005: more than a hundred ids, none, or one that is all zeroes is refused whole, and nothing is queued")]
    public async Task MoreThanAHundredIdsOrNoneOrAnEmptyOneIsRefusedWhole()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob failed = feature.Failed(feature.Recorded(), profile, destination);
        Guid[] aHundredAndOne = [.. Enumerable.Repeat(failed.Id.Value, 101)];

        (HttpStatusCode tooMany, JsonElement said) = await feature.PostAsync(Path, new { ids = aHundredAndOne });
        (HttpStatusCode none, _) = await feature.PostAsync(Path, new { ids = Array.Empty<Guid>() });
        (HttpStatusCode unnamed, _) = await feature.PostAsync(Path, new { });
        (HttpStatusCode zeroes, _) = await feature.PostAsync(Path, new { ids = new[] { failed.Id.Value, Guid.Empty } });

        Assert.Equal(HttpStatusCode.BadRequest, tooMany);
        Assert.Equal(HttpStatusCode.BadRequest, none);
        Assert.Equal(HttpStatusCode.BadRequest, unnamed);
        Assert.Equal(HttpStatusCode.BadRequest, zeroes);
        Assert.Contains("ids", said.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Single(feature.Jobs.Jobs);
    }

    [Fact(DisplayName = "exactly a hundred ids are taken")]
    public async Task ExactlyAHundredIdsAreTaken()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob failed = feature.Failed(feature.Recorded(), profile, destination);
        Guid[] aHundred = [.. Enumerable.Repeat(failed.Id.Value, 100)];

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(Path, new { ids = aHundred });
        string?[] outcomes = [.. body.GetProperty("data").GetProperty("items").EnumerateArray().Select(row => row.GetProperty("outcome").GetString())];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(100, outcomes.Length);
        Assert.Equal("queued", outcomes[0]);
        Assert.All(outcomes.Skip(1), outcome => Assert.Equal("alreadyInTheQueue", outcome));
        Assert.Equal(2, feature.Jobs.Jobs.Count);
    }

    [Fact(DisplayName = "an id that is not a UUID is refused as unreadable, without repeating it")]
    public async Task AnIdThatIsNotAUuidIsRefusedAsUnreadable()
    {
        await using var feature = new EncodingFeature();
        using var content = new StringContent("""{"ids":["not-an-id"]}""", System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await feature.Client.PostAsync(new Uri(Path, UriKind.Relative), content);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ids", body, StringComparison.Ordinal);
        Assert.DoesNotContain("not-an-id", body, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-EA2-001: a request from another origin, or by a caller who has not signed in, is refused and nothing is queued")]
    public async Task ARequestFromAnotherOriginOrNoSessionIsRefused()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob failed = feature.Failed(feature.Recorded(), profile, destination);
        using HttpClient elsewhere = feature.FromElsewhere();
        using HttpClient anonymous = feature.Anonymous();

        (HttpStatusCode fromElsewhere, _) = await feature.PostAsync(Path, new { ids = new[] { failed.Id.Value } }, elsewhere);
        (HttpStatusCode signedOut, _) = await feature.PostAsync(Path, new { ids = new[] { failed.Id.Value } }, anonymous);

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized), (fromElsewhere, signedOut));
        Assert.Single(feature.Jobs.Jobs);
    }
}
