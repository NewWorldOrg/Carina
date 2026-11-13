using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.Feature.Encoding;

[Trait("Category", "Feature")]
public sealed class GetEncodeDurationsActionTests
{
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
