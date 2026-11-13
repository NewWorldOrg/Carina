using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Thumbnails;

namespace Carina.Api.Tests.Feature.Recordings;

[Trait("Category", "Feature")]
public sealed class RemakeThumbnailActionTests
{
    [Fact]
    public async Task APictureIsNotAskedForOfARecordingThatIsStillBeingWritten()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();

        (HttpStatusCode status, _) = await feature.PostAsync(
            $"/api/recordings/{recording.Id.Wire}/thumbnail");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Empty(feature.Remaker.Asked);
    }

    [Theory]
    [InlineData(ThumbnailRemake.Drawn, HttpStatusCode.OK, "drawn", "ready")]
    [InlineData(ThumbnailRemake.Skipped, HttpStatusCode.OK, "skipped", "skipped")]
    [InlineData(ThumbnailRemake.Failed, HttpStatusCode.OK, "failed", "failed")]
    public async Task EveryAnswerThePassRecordsIsAnsweredWithTheStateItLeftBehind(
        ThumbnailRemake answer,
        HttpStatusCode expected,
        string named,
        string state)
    {
        await using var feature = new RecordingFeature();
        Recording recording = Ended(feature);
        feature.Remaker.Answer = answer;

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/recordings/{recording.Id.Wire}/thumbnail");

        Assert.Equal(expected, status);
        Assert.Equal(named, body.GetProperty("data").GetProperty("remake").GetString());
        Assert.Equal(state, body.GetProperty("data").GetProperty("thumbnail").GetProperty("state").GetString());
        Assert.Equal(recording.Id, Assert.Single(feature.Remaker.Asked));
    }

    [Theory]
    [InlineData(ThumbnailRemake.NothingToAskAbout, HttpStatusCode.NotFound)]
    [InlineData(ThumbnailRemake.NowhereToPutThem, HttpStatusCode.ServiceUnavailable)]
    [InlineData(ThumbnailRemake.OutOfReach, HttpStatusCode.ServiceUnavailable)]
    public async Task AnAnswerThatDrewNothingIsToldApartFromOneThatDid(
        ThumbnailRemake answer,
        HttpStatusCode expected)
    {
        await using var feature = new RecordingFeature();
        Recording recording = Ended(feature);
        feature.Remaker.Answer = answer;

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/recordings/{recording.Id.Wire}/thumbnail");

        Assert.Equal(expected, status);
        Assert.False(body.GetProperty("status").GetBoolean());
    }

    [Fact]
    public async Task EverySixthAnswerTheRemakeCanGiveIsOneThisEndpointDraws()
        => Assert.Equal(6, Enum.GetValues<ThumbnailRemake>().Length);

    [Fact]
    public async Task APictureIsNotAskedForOfARecordingNobodyHas()
    {
        await using var feature = new RecordingFeature();

        (HttpStatusCode status, _) = await feature.PostAsync(
            $"/api/recordings/{Guid.NewGuid():N}/thumbnail");

        Assert.Equal(HttpStatusCode.NotFound, status);
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
