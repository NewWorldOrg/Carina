using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Encoding;

[Trait("Category", "Feature")]
public sealed class QueueEncodeJobActionTests
{
    [Fact(DisplayName = "a recording that has ended is queued by hand and stands as queued on attempt 1")]
    public async Task ARecordingThatHasEndedIsQueuedByHand()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording recording = feature.Recorded();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/jobs", new
        {
            recordingId = recording.Id.Wire,
            destinationId = destination.Id.Value,
        });
        JsonElement job = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("queued", job.GetProperty("status").GetString());
        Assert.Equal(1, job.GetProperty("attempt").GetInt32());
        Assert.Equal(recording.Id.Wire, job.GetProperty("recordingId").GetString());
        Assert.Equal(profile.Id.Value, job.GetProperty("profileId").GetGuid());
        Assert.Equal("encodes", job.GetProperty("outputRoot").GetString());
        Assert.Equal(JsonValueKind.Null, job.GetProperty("route").ValueKind);
        Assert.Equal(JsonValueKind.Null, job.GetProperty("headway").ValueKind);
        Assert.Equal(JsonValueKind.Null, job.GetProperty("quietForSeconds").ValueKind);
        Assert.False(job.GetProperty("stalled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, job.GetProperty("failure").ValueKind);
        Assert.Equal(JsonValueKind.Null, job.GetProperty("timeline").ValueKind);
        Assert.Equal(EncodeJobStatus.Queued, Assert.Single(feature.Jobs.Jobs).Status);
    }

    [Fact]
    public async Task AJobNamesTheProfileItWasAskedForOverTheDestinationsDefault()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile usual = feature.Defined();
        EncodeProfile asked = feature.Defined("Sharper");
        EncodeDestination destination = feature.Placed(usual);
        Recording recording = feature.Recorded();

        (_, JsonElement body) = await feature.PostAsync("/api/encoding/jobs", new
        {
            recordingId = recording.Id.Wire,
            profileId = asked.Id.Value,
            destinationId = destination.Id.Value,
        });

        Assert.Equal(asked.Id.Value, body.GetProperty("data").GetProperty("profileId").GetGuid());
    }

    [Fact]
    public async Task ARecordingTheLedgerDoesNotHoldIsNotQueued()
    {
        await using var feature = new EncodingFeature();
        EncodeDestination destination = feature.Placed(feature.Defined());

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/jobs", new
        {
            recordingId = RecordingId.New().Wire,
            destinationId = destination.Id.Value,
        });

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("no recording", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(feature.Jobs.Jobs);
    }

    [Fact]
    public async Task ARecordingStillBeingWrittenIsNotQueued()
    {
        await using var feature = new EncodingFeature();
        EncodeDestination destination = feature.Placed(feature.Defined());
        Recording recording = feature.Recorded(outcome: null);

        (HttpStatusCode status, _) = await feature.PostAsync("/api/encoding/jobs", new
        {
            recordingId = recording.Id.Wire,
            destinationId = destination.Id.Value,
        });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Empty(feature.Jobs.Jobs);
    }

    [Fact(DisplayName = "a recording that failed has nothing to encode")]
    public async Task ARecordingThatFailedIsNotQueued()
    {
        await using var feature = new EncodingFeature();
        EncodeDestination destination = feature.Placed(feature.Defined());
        Recording recording = feature.Recorded(RecordingOutcome.Failed);

        (HttpStatusCode status, _) = await feature.PostAsync("/api/encoding/jobs", new
        {
            recordingId = recording.Id.Wire,
            destinationId = destination.Id.Value,
        });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Empty(feature.Jobs.Jobs);
    }

    [Fact(DisplayName = "a recording with a job waiting or running is not queued a second time")]
    public async Task ARecordingWithAJobUnderwayIsNotQueuedTwice()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording recording = feature.Recorded();
        feature.Queued(recording, profile, destination);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/jobs", new
        {
            recordingId = recording.Id.Wire,
            destinationId = destination.Id.Value,
        });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("not queued twice", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Single(feature.Jobs.Jobs);
    }

    [Fact(DisplayName = "a recording already encoded with a profile is not encoded with it again, because the artefact would collide")]
    public async Task ARecordingAlreadyEncodedWithAProfileIsNotEncodedWithItAgain()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording recording = feature.Recorded();
        EncodeJob done = feature.Queued(recording, profile, destination);
        done.Start(EncodingFeature.Noon.AddMinutes(-20));
        done.Name(EncodeFileName.Artefact(recording.Id, profile.Id));
        done.Complete(EncodingFeature.Noon.AddMinutes(-10));

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/jobs", new
        {
            recordingId = recording.Id.Wire,
            destinationId = destination.Id.Value,
        });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("already encoded", body.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a recording already encoded with one profile is not given a second artefact with another")]
    public async Task ARecordingAlreadyEncodedWithOneProfileIsNotGivenASecondArtefactWithAnother()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile first = feature.Defined();
        EncodeProfile another = feature.Defined("Smaller");
        EncodeDestination destination = feature.Placed(first);
        Recording recording = feature.Recorded();
        feature.Completed(recording, first, destination);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/jobs", new
        {
            recordingId = recording.Id.Wire,
            destinationId = destination.Id.Value,
            profileId = another.Id.Value,
        });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("already encoded", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Single(feature.Jobs.Jobs);
    }

    [Fact(DisplayName = "a recording already encoded with a profile is encoded with it again when a person asks outright, and the new job says so")]
    public async Task ARecordingAlreadyEncodedIsEncodedAgainWhenAPersonAsksOutright()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording recording = feature.Recorded();
        EncodeJob made = feature.Completed(recording, profile, destination);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/jobs", new
        {
            recordingId = recording.Id.Wire,
            destinationId = destination.Id.Value,
            makeItAgain = true,
        });

        EncodeJob queued = feature.Jobs.Jobs.Single(job => !job.Id.Equals(made.Id));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(2, feature.Jobs.Jobs.Count);
        Assert.Equal(queued.Id.Value, body.GetProperty("data").GetProperty("id").GetGuid());
        Assert.True(queued.MakesItAgain);
        Assert.Equal(profile.Id, queued.ProfileId);
        Assert.Equal(EncodeJobStatus.Queued, queued.Status);
        Assert.Equal(EncodeJobStatus.Completed, made.Status);
        Assert.Equal(EncodeFileName.Artefact(recording.Id, profile.Id), made.ArtefactName);
        Assert.Null(made.NameGivenUpAt);
    }

    [Fact(DisplayName = "saying outright not to make it again is answered exactly as saying nothing is, so the refusal is what it always was")]
    public async Task SayingNotToMakeItAgainIsRefusedJustAsSayingNothingIs()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording recording = feature.Recorded();
        feature.Completed(recording, profile, destination);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/jobs", new
        {
            recordingId = recording.Id.Wire,
            destinationId = destination.Id.Value,
            makeItAgain = false,
        });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("already encoded", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Single(feature.Jobs.Jobs);
    }

    [Fact(DisplayName = "a recording whose job is still waiting is not queued a second time, even to be made again")]
    public async Task ARecordingWithAJobUnderwayIsNotMadeAgainEither()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording recording = feature.Recorded();
        feature.Queued(recording, profile, destination);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/jobs", new
        {
            recordingId = recording.Id.Wire,
            destinationId = destination.Id.Value,
            makeItAgain = true,
        });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("not queued twice", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Single(feature.Jobs.Jobs);
    }

    [Fact(DisplayName = "a failed job comes back after a second attempt is queued, so a failure can be retried one recording at a time")]
    public async Task AFailedJobDoesNotStopTheRecordingBeingQueuedAgain()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording recording = feature.Recorded();
        EncodeJob failed = feature.Queued(recording, profile, destination);
        failed.Start(EncodingFeature.Noon.AddMinutes(-20));
        failed.Fail(EncodeFailure.SourceMissing, "the recording file is not where the ledger says", EncodingFeature.Noon.AddMinutes(-10));

        (HttpStatusCode status, _) = await feature.PostAsync("/api/encoding/jobs", new
        {
            recordingId = recording.Id.Wire,
            destinationId = destination.Id.Value,
        });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(2, feature.Jobs.Jobs.Count);
    }

    [Theory]
    [InlineData("""{"destinationId":"0f8fad5b-d9cb-469f-a165-70867728950e"}""", "recordingId")]
    [InlineData("""{"recordingId":"not-an-id","destinationId":"0f8fad5b-d9cb-469f-a165-70867728950e"}""", "recordingId")]
    [InlineData("""{"recordingId":"1872e6a880e94ac6a8f93f740239ef00"}""", "destinationId")]
    [InlineData("""{"recordingId":"1872e6a880e94ac6a8f93f740239ef00","destinationId":"00000000-0000-0000-0000-000000000000"}""", "destinationId")]
    [InlineData("""{"recordingId":"1872e6a880e94ac6a8f93f740239ef00","destinationId":"0f8fad5b-d9cb-469f-a165-70867728950e","profileId":"00000000-0000-0000-0000-000000000000"}""", "profileId")]
    public async Task AJobAskedForWithoutNamingWhatItNeedsIsRefusedNamingTheField(string json, string field)
    {
        await using var feature = new EncodingFeature();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await feature.Client.PostAsync(new Uri("/api/encoding/jobs", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "the job surface takes one recording at a time and has no way in that takes a list")]
    public async Task TheJobSurfaceTakesOneRecordingAtATime()
    {
        await using var feature = new EncodingFeature();
        EncodeDestination destination = feature.Placed(feature.Defined());
        Recording first = feature.Recorded();
        Recording second = feature.Recorded();
        string json = $$"""{"recordingId":["{{first.Id.Wire}}","{{second.Id.Wire}}"],"destinationId":"{{destination.Id.Value}}"}""";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await feature.Client.PostAsync(new Uri("/api/encoding/jobs", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(feature.Jobs.Jobs);
    }

    [Fact]
    public async Task QueueingAJobByHandTellsTheScreensTheJobsMoved()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording recording = feature.Recorded();
        feature.Events.Signalled.Clear();

        await feature.PostAsync("/api/encoding/jobs", new
        {
            recordingId = recording.Id.Wire,
            destinationId = destination.Id.Value,
        });

        Assert.Equal([AppEventName.EncodeJobs], feature.Events.Signalled);
    }

    [Fact]
    public async Task AJobThatWasRefusedTellsTheScreensNothing()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        feature.Events.Signalled.Clear();

        (HttpStatusCode status, _) = await feature.PostAsync("/api/encoding/jobs", new
        {
            recordingId = RecordingId.New().Wire,
            destinationId = destination.Id.Value,
        });

        Assert.NotEqual(HttpStatusCode.Created, status);
        Assert.Empty(feature.Events.Signalled);
    }


    private static IEnumerable<JsonElement> Numbers(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Numbers(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Numbers),
        JsonValueKind.Number => [element],
        _ => [],
    };
}
