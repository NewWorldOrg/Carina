using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

public sealed class EncodeCallOffLeavesTheRecordingAsItWasTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Theory(DisplayName = "BR-ED2-012: calling a running job off leaves the result of the recording it was made from as it was")]
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
