using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class EncodingEndpointTests
{
    [Fact(DisplayName = "BR-EV-001: a profile is defined out of enumerated values and numbers, and comes back on the list")]
    public async Task AProfileIsDefinedAndComesBackOnTheList()
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/profiles", new
        {
            label = "Viewing",
            codec = "h264",
            resolution = "asSource",
            deinterlace = "everyFrame",
            rateFactor = 22,
            quantiser = 24,
        });
        (_, JsonElement listed) = await feature.GetAsync("/api/encoding/profiles");
        JsonElement item = Assert.Single(listed.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("Viewing", body.GetProperty("data").GetProperty("label").GetString());
        Assert.Equal("h264", item.GetProperty("codec").GetString());
        Assert.Equal("everyFrame", item.GetProperty("deinterlace").GetString());
        Assert.Equal(22, item.GetProperty("rateFactor").GetInt32());
        Assert.Equal(body.GetProperty("data").GetProperty("id").GetGuid(), item.GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("codec", "av1")]
    [InlineData("codec", "9")]
    [InlineData("rateFactor", "52")]
    [InlineData("quantiser", "-1")]
    [InlineData("label", "\"\"")]
    public async Task AProfileOutsideTheEnumeratedValuesIsRefusedNamingTheField(string field, string value)
    {
        await using var feature = new EncodingFeature();
        string json = $$$"""{"label":"Viewing","codec":"h264","resolution":"hd","deinterlace":"leave","rateFactor":22,"quantiser":24,"{{{field}}}":{{{(value.StartsWith('"') ? value : value == "av1" ? "\"av1\"" : value)}}}}""";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await feature.Client.PostAsync(new Uri("/api/encoding/profiles", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(feature.Profiles.Profiles);
    }

    [Fact(DisplayName = "BR-EV-001: a destination names a root out of the declared set that this process holds, and comes back on the list")]
    public async Task ADestinationNamingAHeldRootIsDefined()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/destinations", new
        {
            label = "Shelf",
            outputRoot = "encodes",
            defaultProfileId = profile.Id.Value,
        });
        (_, JsonElement listed) = await feature.GetAsync("/api/encoding/destinations");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("encodes", body.GetProperty("data").GetProperty("outputRoot").GetString());
        Assert.Equal(profile.Id.Value, body.GetProperty("data").GetProperty("defaultProfileId").GetGuid());
        Assert.Single(listed.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    [Fact(DisplayName = "BR-EV-001: a destination naming the root the recordings are read from is refused at saving")]
    public async Task ADestinationNamingTheRootTheRecordingsAreReadFromIsRefused()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/destinations", new
        {
            label = "Wrong shelf",
            outputRoot = "primary",
            defaultProfileId = profile.Id.Value,
        });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("holds for writing", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(feature.Destinations.Destinations);
    }

    [Fact(DisplayName = "BR-EV-001: a destination naming a root nobody declares is refused")]
    public async Task ADestinationNamingARootNobodyDeclaresIsRefused()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/destinations", new
        {
            label = "Nowhere",
            outputRoot = "elsewhere",
            defaultProfileId = profile.Id.Value,
        });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("outputRoot", body.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-EV-001: while the driver cannot say what it declares, no destination is saved")]
    public async Task WhileTheDriverCannotSayWhatItDeclaresNoDestinationIsSaved()
    {
        await using var feature = new EncodingFeature();
        feature.Driver.Unreachable = "no socket at that path";
        EncodeProfile profile = feature.Defined();

        (HttpStatusCode status, _) = await feature.PostAsync("/api/encoding/destinations", new
        {
            label = "Shelf",
            outputRoot = "encodes",
            defaultProfileId = profile.Id.Value,
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Empty(feature.Destinations.Destinations);
    }

    [Fact(DisplayName = "BR-ES-001: a recording that has ended is queued by hand and stands as queued on attempt 1")]
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

    [Fact(DisplayName = "BR-ED2-004: a recording that failed has nothing to encode")]
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

    [Fact(DisplayName = "BR-ED2-009: a recording with a job waiting or running is not queued a second time")]
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

    [Fact(DisplayName = "BR-ED2-009: a recording already encoded with a profile is not encoded with it again, because the artefact would collide")]
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

    [Fact(DisplayName = "BR-ED2-012: a failed job comes back after a second attempt is queued, so a failure can be retried one recording at a time")]
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

    [Fact(DisplayName = "BR-ED2-008: the job surface takes one recording at a time and has no way in that takes a list")]
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

    [Fact(DisplayName = "BR-ED2-014: the list says of each job where it stands, how it ran, how far it got, how long it has been quiet, whether that is a stall, and why it failed")]
    public async Task TheListSaysWhereEachJobStands()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording recording = feature.Recorded();
        EncodeJob running = feature.Queued(recording, profile, destination);
        running.Start(EncodingFeature.Noon.AddMinutes(-25));
        running.Routed(new EncodeRoute(EncodeEncoder.Vaapi, EncodeEncoder.Software, EncodeSwerve.TheCardIsOutOfReach));
        running.Spawned(new RunningProgramme(4242, EncodingFeature.Noon.AddMinutes(-25)));
        running.Reached(EncodeProgress.Of(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), 2.0, false), EncodingFeature.Noon.AddMinutes(-15));
        EncodeJob failed = feature.Queued(feature.Recorded(), profile, destination);
        failed.Start(EncodingFeature.Noon.AddMinutes(-40));
        failed.Fail(EncodeFailure.NotEnoughRoom, "No space left on device", EncodingFeature.Noon.AddMinutes(-35));
        EncodeJob waiting = feature.Queued(feature.Recorded(), profile, destination);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/encoding/jobs");
        JsonElement data = body.GetProperty("data");
        JsonElement[] items = [.. data.GetProperty("items").EnumerateArray()];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        Assert.Equal(EncodeJobQuery.DefaultPerPage, data.GetProperty("perPage").GetInt32());
        Assert.Equal(3, items.Length);
        JsonElement ran = items.Single(item => item.GetProperty("id").GetGuid() == running.Id.Value);
        Assert.Equal("running", ran.GetProperty("status").GetString());
        Assert.Equal("vaapi", ran.GetProperty("route").GetProperty("asked").GetString());
        Assert.Equal("software", ran.GetProperty("route").GetProperty("ran").GetString());
        Assert.Equal("theCardIsOutOfReach", ran.GetProperty("route").GetProperty("swerved").GetString());
        Assert.Equal(0.333, ran.GetProperty("headway").GetProperty("portion").GetDouble(), 3);
        Assert.Equal(600, ran.GetProperty("headway").GetProperty("leftSeconds").GetInt32());
        Assert.Equal(900, ran.GetProperty("quietForSeconds").GetInt32());
        Assert.True(ran.GetProperty("stalled").GetBoolean());
        Assert.DoesNotContain("4242", ran.GetRawText(), StringComparison.Ordinal);
        Assert.False(ran.TryGetProperty("programme", out _));
        JsonElement fell = items.Single(item => item.GetProperty("id").GetGuid() == failed.Id.Value);
        Assert.Equal("failed", fell.GetProperty("status").GetString());
        Assert.Equal("notEnoughRoom", fell.GetProperty("failure").GetProperty("failure").GetString());
        Assert.Equal("No space left on device", fell.GetProperty("failure").GetProperty("note").GetString());
        JsonElement waits = items.Single(item => item.GetProperty("id").GetGuid() == waiting.Id.Value);
        Assert.Equal("queued", waits.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, waits.GetProperty("quietForSeconds").ValueKind);
    }

    [Fact]
    public async Task TheListIsNarrowedToTheStandingsAskedForAndPaged()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        feature.Queued(feature.Recorded(), profile, destination);
        feature.Queued(feature.Recorded(), profile, destination);
        EncodeJob ended = feature.Queued(feature.Recorded(), profile, destination);
        ended.Start(EncodingFeature.Noon.AddMinutes(-20));
        ended.Cancel(EncodingFeature.Noon.AddMinutes(-19));

        (_, JsonElement queued) = await feature.GetAsync("/api/encoding/jobs?status=queued&perPage=1");
        (_, JsonElement cancelled) = await feature.GetAsync("/api/encoding/jobs?status=cancelled&status=running");
        (HttpStatusCode refused, _) = await feature.GetAsync("/api/encoding/jobs?page=0");

        Assert.Equal(2, queued.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal(1, queued.GetProperty("data").GetProperty("items").GetArrayLength());
        Assert.Equal(2, queued.GetProperty("data").GetProperty("lastPage").GetInt32());
        Assert.Equal(1, cancelled.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, refused);
    }

    [Fact(DisplayName = "BR-ED2-012: calling a waiting job off is a person's act and is kept apart from a failure")]
    public async Task CallingAWaitingJobOffIsKeptApartFromAFailure()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob job = feature.Queued(feature.Recorded(), profile, destination);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync($"/api/encoding/jobs/{job.Id.Value}/cancel");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("cancelled", body.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").GetProperty("failure").ValueKind);
        Assert.Equal(EncodeJobStatus.Cancelled, job.Status);
        Assert.Empty(feature.Strays.Stopped);
    }

    [Fact(DisplayName = "BR-ED2-012: calling a running job off writes the ledger first, then stops the programme written against it")]
    public async Task CallingARunningJobOffStopsTheProgrammeWrittenAgainstIt()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob job = feature.Queued(feature.Recorded(), profile, destination);
        job.Start(EncodingFeature.Noon.AddMinutes(-20));
        var programme = new RunningProgramme(4242, EncodingFeature.Noon.AddMinutes(-20));
        job.Spawned(programme);
        EncodeJobStatus? statusWhenStopped = null;
        feature.Jobs.WhenSaving = saved => statusWhenStopped ??= saved.Status;

        (HttpStatusCode status, _) = await feature.PostAsync($"/api/encoding/jobs/{job.Id.Value}/cancel");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(EncodeJobStatus.Cancelled, statusWhenStopped);
        Assert.Equal([programme], feature.Strays.Stopped);
        Assert.Null(job.Programme);
    }

    [Fact]
    public async Task AJobThatAlreadyEndedCannotBeCalledOff()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob job = feature.Queued(feature.Recorded(), profile, destination);
        job.Start(EncodingFeature.Noon.AddMinutes(-20));
        job.Fail(EncodeFailure.TimedOut, "no headway", EncodingFeature.Noon.AddMinutes(-10));

        (HttpStatusCode status, _) = await feature.PostAsync($"/api/encoding/jobs/{job.Id.Value}/cancel");
        (HttpStatusCode unknown, _) = await feature.PostAsync($"/api/encoding/jobs/{Guid.NewGuid()}/cancel");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(EncodeJobStatus.Failed, job.Status);
        Assert.Equal(HttpStatusCode.NotFound, unknown);
    }

    [Fact(DisplayName = "BR-EA2-003: nothing under the encoding surface deletes, and nothing there takes a list of recordings")]
    public async Task NothingUnderTheEncodingSurfaceDeletes()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob job = feature.Queued(feature.Recorded(), profile, destination);

        using HttpResponseMessage jobs = await feature.Client.DeleteAsync(new Uri($"/api/encoding/jobs/{job.Id.Value}", UriKind.Relative));
        using HttpResponseMessage profiles = await feature.Client.DeleteAsync(new Uri($"/api/encoding/profiles/{profile.Id.Value}", UriKind.Relative));
        using HttpResponseMessage destinations = await feature.Client.DeleteAsync(new Uri($"/api/encoding/destinations/{destination.Id.Value}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, jobs.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, profiles.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, destinations.StatusCode);
        Assert.Single(feature.Jobs.Jobs);
    }
}
