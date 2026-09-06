using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class RecordingEncodeStandingTests
{
    private static readonly OutputRoot Shelf = new("encodes");

    [Fact(DisplayName = "BR-ES-002: every row of the library says where its recording stands with the encoder")]
    public async Task EveryRowOfTheLibrarySaysWhereItsRecordingStandsWithTheEncoder()
    {
        await using var feature = new RecordingFeature();
        Recording encoded = Ended(feature, eventId: 1);
        Recording waiting = Ended(feature, eventId: 2);
        Recording untouched = Ended(feature, eventId: 3);
        Place(feature, encoded, EncodeJobStatus.Completed);
        Place(feature, waiting, EncodeJobStatus.Queued);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");
        Dictionary<string, string?> standings = body
            .GetProperty("data")
            .GetProperty("items")
            .EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("id").GetString()!,
                row => row.GetProperty("encode").GetProperty("standing").GetString(),
                StringComparer.Ordinal);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("completed", standings[encoded.Id.Wire]);
        Assert.Equal("queued", standings[waiting.Id.Wire]);
        Assert.Equal("notEncoded", standings[untouched.Id.Wire]);
    }

    [Fact(DisplayName = "BR-ES-002: a recording still being written stands unencoded rather than saying nothing")]
    public async Task ARecordingStillBeingWrittenStandsUnencoded()
    {
        await using var feature = new RecordingFeature();
        Recording writing = feature.Held();

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{writing.Id.Wire}");

        Assert.Equal(
            "notEncoded",
            body.GetProperty("data").GetProperty("recording").GetProperty("encode").GetProperty("standing").GetString());
    }

    [Fact(DisplayName = "BR-ES-002: one recording's detail says what its row in the list says")]
    public async Task OneRecordingsDetailSaysWhatItsRowInTheListSays()
    {
        await using var feature = new RecordingFeature();
        Recording recording = Ended(feature, eventId: 4);
        Place(feature, recording, EncodeJobStatus.Completed);
        Place(feature, recording, EncodeJobStatus.Completed);
        Place(feature, recording, EncodeJobStatus.Running);

        (_, JsonElement listed) = await feature.GetAsync("/api/recordings");
        (HttpStatusCode status, JsonElement detail) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement row = listed.GetProperty("data").GetProperty("items")[0];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("completed", row.GetProperty("encode").GetProperty("standing").GetString());
        Assert.Equal(
            "completed",
            detail.GetProperty("data").GetProperty("recording").GetProperty("encode").GetProperty("standing").GetString());
    }

    [Fact(DisplayName = "BR-ES-002: a job that failed shows on the row until something is queued in its place")]
    public async Task AJobThatFailedShowsOnTheRowUntilSomethingIsQueuedInItsPlace()
    {
        await using var feature = new RecordingFeature();
        Recording recording = Ended(feature, eventId: 5);
        Place(feature, recording, EncodeJobStatus.Failed);

        (_, JsonElement broken) = await feature.GetAsync("/api/recordings");

        Place(feature, recording, EncodeJobStatus.Queued);

        (_, JsonElement retried) = await feature.GetAsync("/api/recordings");

        Assert.Equal(
            "failed",
            broken.GetProperty("data").GetProperty("items")[0].GetProperty("encode").GetProperty("standing").GetString());
        Assert.Equal(
            "queued",
            retried.GetProperty("data").GetProperty("items")[0].GetProperty("encode").GetProperty("standing").GetString());
    }

    private static Recording Ended(RecordingFeature feature, int eventId)
    {
        Recording recording = feature.Held(eventId: eventId);
        recording.Wrote(TimeSpan.FromHours(1));
        recording.Abort(RecordingFeature.Noon.AddHours(1));
        recording.Settle(RecordingOutcome.Complete, 1_000_000, RecordingFeature.Noon.AddHours(1));

        return recording;
    }

    private static void Place(RecordingFeature feature, Recording recording, EncodeJobStatus standing)
    {
        var profile = EncodeProfileId.New();
        EncodeJob job = EncodeJob.Queue(
            EncodeJobId.New(),
            recording.Id,
            profile,
            EncodeDestinationId.New(),
            Shelf,
            RecordingFeature.Noon.AddHours(2));

        if (standing is not EncodeJobStatus.Queued)
        {
            job.Start(RecordingFeature.Noon.AddHours(2).AddMinutes(1));
        }

        if (standing is EncodeJobStatus.Completed)
        {
            job.Name(EncodeFileName.Artefact(recording.Id, profile));
            job.Complete(RecordingFeature.Noon.AddHours(3));
        }

        if (standing is EncodeJobStatus.Failed)
        {
            job.Fail(EncodeFailure.FfmpegExitedNonZero, "the programme exited 255", RecordingFeature.Noon.AddHours(3));
        }

        if (standing is EncodeJobStatus.Cancelled)
        {
            job.Cancel(RecordingFeature.Noon.AddHours(3));
        }

        feature.Jobs.Jobs.Add(job);
    }
}
