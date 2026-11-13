using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Encoding;

[Trait("Category", "Feature")]
public sealed class CancelEncodeJobActionTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "calling a waiting job off is a person's act and is kept apart from a failure")]
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

    [Fact(DisplayName = "calling a running job off writes the ledger first, then stops the programme written against it")]
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

    [Fact]
    public async Task CallingAJobOffTellsTheScreensTheJobsMoved()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob job = feature.Queued(feature.Recorded(), profile, destination);
        feature.Events.Signalled.Clear();

        await feature.PostAsync($"/api/encoding/jobs/{job.Id.Value}/cancel");

        Assert.Equal([AppEventName.EncodeJobs], feature.Events.Signalled);
    }

    [Theory(DisplayName = "calling a running job off leaves the result of the recording it was made from as it was")]
    [InlineData(RecordingOutcome.Complete)]
    [InlineData(RecordingOutcome.Failed)]
    public async Task CallingARunningJobOffLeavesTheRecordingsResultAsItWas(RecordingOutcome outcome)
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        Recording recording = feature.Recorded(outcome);
        EncodeJob job = feature.Queued(recording, profile, destination);
        job.Start(EncodingFeature.Noon.AddMinutes(-20));
        job.Spawned(new RunningProgramme(4242, EncodingFeature.Noon.AddMinutes(-20)));
        string before = WhatItSays((await feature.Recordings.FindAsync(recording.Id, Cancel))!);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync($"/api/encoding/jobs/{job.Id.Value}/cancel");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("cancelled", body.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(before, WhatItSays((await feature.Recordings.FindAsync(recording.Id, Cancel))!));
    }

    private static IEnumerable<JsonElement> Numbers(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Numbers(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Numbers),
        JsonValueKind.Number => [element],
        _ => [],
    };

    private static string WhatItSays(Recording recording)
        => JsonSerializer.Serialize(new
        {
            recording.Outcome,
            recording.OutcomeDetail,
            recording.FileSizeObserved,
            recording.ObservedAt,
            recording.StoppedAtActual,
            recording.AbortedAt,
            recording.WrittenDurationMs,
            recording.ExpectedWindowEnd,
            recording.PromisedWindowEnd,
        });
}
