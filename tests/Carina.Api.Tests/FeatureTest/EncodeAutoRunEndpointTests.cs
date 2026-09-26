using System.Net;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

public sealed class EncodeAutoRunEndpointTests
{
    [Fact(DisplayName = "a machine nobody has settled answers what it was deployed with, and says nobody settled it")]
    public async Task AMachineNobodyHasSettledAnswersWhatItWasDeployedWith()
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/encoding/settings");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(data.GetProperty("automatically").GetBoolean());
        Assert.Equal(2, data.GetProperty("mostCores").GetInt32());
        Assert.Equal(6, data.GetProperty("coresThisMachineHas").GetInt32());
        Assert.False(data.GetProperty("stored").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("updatedAt").ValueKind);
    }

    [Fact(DisplayName = "BR-ED2-004: what the auto-run takes is stated, and it is what ended with a file")]
    public async Task WhatTheAutoRunTakesIsStated()
    {
        await using var feature = new EncodingFeature();

        (_, JsonElement body) = await feature.GetAsync("/api/encoding/settings");

        Assert.Equal(
            ["complete", "truncated"],
            body.GetProperty("data").GetProperty("subject").EnumerateArray().Select(each => each.GetString()));
    }

    [Fact(DisplayName = "BR-ED2-004: a machine offering more than one destination says the auto-run cannot settle where an artefact goes")]
    public async Task AMachineOfferingMoreThanOneDestinationSaysTheAutoRunCannotSettleWhereAnArtefactGoes()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        feature.Placed(profile);
        feature.Placed(profile, new OutputRoot("elsewhere"));

        (_, JsonElement body) = await feature.GetAsync("/api/encoding/settings");

        Assert.Equal("moreThanOneIsOffered", body.GetProperty("data").GetProperty("whereArtefactsGo").GetString());
    }

    [Fact(DisplayName = "BR-ED2-004: one destination settles where an artefact goes, and a machine with none says nothing is defined")]
    public async Task OneDestinationSettlesWhereAnArtefactGoesAndNoneSaysNothingIsDefined()
    {
        await using var nothing = new EncodingFeature();
        await using var one = new EncodingFeature();
        one.Placed(one.Defined());

        (_, JsonElement undefined) = await nothing.GetAsync("/api/encoding/settings");
        (_, JsonElement settled) = await one.PutAsync("/api/encoding/settings", new { automatically = true, mostCores = 2 });

        Assert.Equal("nothingIsDefined", undefined.GetProperty("data").GetProperty("whereArtefactsGo").GetString());
        Assert.Equal("settled", settled.GetProperty("data").GetProperty("whereArtefactsGo").GetString());
    }

    [Fact(DisplayName = "settling the auto-run writes one row, answers it as settled, and signals")]
    public async Task SettlingTheAutoRunWritesOneRowAndSignals()
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            "/api/encoding/settings",
            new { automatically = false, mostCores = 4 });
        (_, JsonElement read) = await feature.GetAsync("/api/encoding/settings");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(data.GetProperty("automatically").GetBoolean());
        Assert.Equal(4, data.GetProperty("mostCores").GetInt32());
        Assert.True(data.GetProperty("stored").GetBoolean());
        Assert.Equal(EncodingFeature.Noon, data.GetProperty("updatedAt").GetDateTime());
        Assert.Equal(4, read.GetProperty("data").GetProperty("mostCores").GetInt32());
        Assert.Equal(1, feature.AutoRun.Saves);
        Assert.Contains(AppEventName.EncodeJobs, feature.Events.Signalled);
    }

    [Fact(DisplayName = "BR-ED2-004: turning the auto-run off stops making jobs, and leaves the one already running alone")]
    public async Task TurningTheAutoRunOffLeavesTheJobAlreadyRunningAlone()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob running = feature.Queued(feature.Recorded(), profile, destination);
        running.Start(EncodingFeature.Noon.AddMinutes(-20));
        EncodeJob waiting = feature.Queued(feature.Recorded(), profile, destination);

        (HttpStatusCode status, _) = await feature.PutAsync(
            "/api/encoding/settings",
            new { automatically = false, mostCores = 2 });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(EncodeJobStatus.Running, running.Status);
        Assert.Equal(EncodeJobStatus.Queued, waiting.Status);
        Assert.Empty(feature.Jobs.Moves);
        Assert.Empty(feature.Strays.Stopped);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7)]
    public async Task ACapOutsideWhatThisMachineHasIsRefusedNamingTheField(int mostCores)
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            "/api/encoding/settings",
            new { automatically = true, mostCores });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("mostCores", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(0, feature.AutoRun.Saves);
    }

    [Theory]
    [InlineData("""{"mostCores":2}""", "automatically")]
    [InlineData("""{"automatically":true}""", "mostCores")]
    [InlineData("{}", "automatically")]
    public async Task SettlingHalfOfItIsRefusedNamingWhatIsMissing(string json, string named)
    {
        await using var feature = new EncodingFeature();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await feature.Client.PutAsync(
            new Uri("/api/encoding/settings", UriKind.Relative),
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(named, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, feature.AutoRun.Saves);
    }

    [Fact(DisplayName = "with nothing finished the answer says how many there are rather than nought seconds")]
    public async Task WithNothingFinishedTheAnswerSaysHowManyThereAre()
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/encoding/jobs/durations");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, data.GetProperty("jobs").GetInt32());
        Assert.Equal(EncodeSpells.FewestToAverage, data.GetProperty("fewestToAverage").GetInt32());
        Assert.Equal(EncodeSpells.MostLookedAt, data.GetProperty("lookedAtAtMost").GetInt32());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("averageSeconds").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("from").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("to").ValueKind);
    }

    [Fact(DisplayName = "an average is not made out of fewer jobs than it takes")]
    public async Task AnAverageIsNotMadeOutOfFewerJobsThanItTakes()
    {
        await using var feature = new EncodingFeature();
        Finished(feature, 2);

        (_, JsonElement body) = await feature.GetAsync("/api/encoding/jobs/durations");

        Assert.Equal(2, body.GetProperty("data").GetProperty("jobs").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").GetProperty("averageSeconds").ValueKind);
    }

    [Fact(DisplayName = "with enough of them the answer carries the average and the window it was made over")]
    public async Task WithEnoughOfThemTheAnswerCarriesTheAverageAndTheWindow()
    {
        await using var feature = new EncodingFeature();
        Finished(feature, 3);

        (_, JsonElement body) = await feature.GetAsync("/api/encoding/jobs/durations");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(3, data.GetProperty("jobs").GetInt32());
        Assert.Equal(600, data.GetProperty("averageSeconds").GetDouble());
        Assert.Equal(EncodingFeature.Noon.AddMinutes(-19), data.GetProperty("from").GetDateTime());
        Assert.Equal(EncodingFeature.Noon.AddMinutes(-13), data.GetProperty("to").GetDateTime());
    }

    [Fact(DisplayName = "a job that failed and one that was called off say nothing about how long an encode takes")]
    public async Task AJobThatFailedOrWasCalledOffIsNotCounted()
    {
        await using var feature = new EncodingFeature();
        Finished(feature, 3);
        EncodeProfile profile = feature.Defined("Another");
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob failed = feature.Queued(feature.Recorded(), profile, destination);
        failed.Start(EncodingFeature.Noon.AddMinutes(-10));
        failed.Fail(EncodeFailure.FfmpegExitedNonZero, "it stopped", EncodingFeature.Noon.AddMinutes(-9));

        (_, JsonElement body) = await feature.GetAsync("/api/encoding/jobs/durations");

        Assert.Equal(3, body.GetProperty("data").GetProperty("jobs").GetInt32());
        Assert.Equal(600, body.GetProperty("data").GetProperty("averageSeconds").GetDouble());
    }

    private static void Finished(EncodingFeature feature, int jobs)
    {
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);

        for (int each = 0; each < jobs; each++)
        {
            Recording recording = feature.Recorded();
            EncodeJob job = feature.Queued(recording, profile, destination);
            job.Start(EncodingFeature.Noon.AddMinutes(-29 + (3 * each)));
            job.Name(EncodeFileName.Artefact(recording.Id, profile.Id));
            job.Complete(EncodingFeature.Noon.AddMinutes(-19 + (3 * each)));
        }
    }
}
