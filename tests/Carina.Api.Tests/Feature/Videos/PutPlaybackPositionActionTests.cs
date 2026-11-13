using System.Net;
using System.Text.Json;

using Carina.Api.Common;
using Carina.Api.Services;
using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Recordings;
using Carina.Domain.Viewing;

namespace Carina.Api.Tests.Feature.Videos;

[Trait("Category", "Feature")]
public sealed class PutPlaybackPositionActionTests
{
    [Fact(DisplayName = "a player sending where it has got to over and over leaves one place, the last one")]
    public async Task APlayerSendingWhereItHasGotToOverAndOverLeavesOnePlace()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        foreach (double second in new[] { 10d, 20d, 30d })
        {
            (HttpStatusCode kept, _) = await KeepAsync(
                feature.Client,
                recording.Id.Wire,
                new { positionSec = second });

            Assert.Equal(HttpStatusCode.OK, kept);
        }

        PlaybackPosition only = Assert.Single(feature.Positions.Positions);

        Assert.Equal(TimeSpan.FromSeconds(30), only.Position);
        Assert.Equal(recording.Id, only.RecordingId);
    }

    [Fact(DisplayName = "the answer says what was kept and when it was kept")]
    public async Task TheAnswerSaysWhatWasKeptAndWhenItWasKept()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        (HttpStatusCode status, JsonElement body) = await KeepAsync(
            feature.Client,
            recording.Id.Wire,
            new { positionSec = 61.5 });

        PlaybackPosition only = Assert.Single(feature.Positions.Positions);
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("status").GetBoolean());
        Assert.Equal(recording.Id.Wire, data.GetProperty("recordingId").GetString());
        Assert.Equal(61.5, data.GetProperty("positionSec").GetDouble());
        Assert.Equal(only.UpdatedAt, data.GetProperty("updatedAt").GetDateTime());
    }

    [Fact(DisplayName = "a recording the ledger does not hold is nowhere to keep a place in")]
    public async Task ARecordingTheLedgerDoesNotHoldIsNowhereToKeepAPlaceIn()
    {
        await using var feature = new PlayFeature();

        (HttpStatusCode status, JsonElement body) = await KeepAsync(
            feature.Client,
            RecordingId.New().Wire,
            new { positionSec = 10 });

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Empty(feature.Positions.Positions);
    }

    [Fact(DisplayName = "something that is not a recording id is refused before anything is written")]
    public async Task SomethingThatIsNotARecordingIdIsRefused()
    {
        await using var feature = new PlayFeature();

        (HttpStatusCode status, JsonElement body) = await KeepAsync(
            feature.Client,
            "not-a-recording",
            new { positionSec = 10 });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(RecordingIdText.Description, body.GetProperty("message").GetString());
        Assert.Empty(feature.Positions.Positions);
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(null)]
    public async Task APlaceThatIsNotASecondIntoTheRecordingIsRefused(double? second)
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        (HttpStatusCode status, JsonElement body) = await KeepAsync(
            feature.Client,
            recording.Id.Wire,
            new { positionSec = second });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(PlaybackPositionService.ThePositionsThereAre, body.GetProperty("message").GetString());
        Assert.Empty(feature.Positions.Positions);
    }

    [Fact(DisplayName = "a recording still being written has no watching of it to remember")]
    public async Task ARecordingStillBeingWrittenHasNoWatchingOfItToRemember()
    {
        await using var feature = new PlayFeature();
        Recording writing = feature.StillWriting();

        (HttpStatusCode status, _) = await KeepAsync(
            feature.Client,
            writing.Id.Wire,
            new { positionSec = 10 });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Empty(feature.Positions.Positions);
    }

    [Fact(DisplayName = "a client carrying no credentials keeps nothing and is refused")]
    public async Task AClientCarryingNoCredentialsKeepsNothing()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        (HttpStatusCode status, _) = await KeepAsync(
            feature.Stranger,
            recording.Id.Wire,
            new { positionSec = 10 });

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Empty(feature.Positions.Positions);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> KeepAsync(
        HttpClient client,
        string recordingId,
        object body)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            new Uri($"/api/videos/{recordingId}/position", UriKind.Relative),
            body);
        string read = await response.Content.ReadAsStringAsync();

        if (!read.StartsWith('{'))
        {
            return (response.StatusCode, default);
        }

        using var document = JsonDocument.Parse(read);

        return (response.StatusCode, document.RootElement.Clone());
    }
}
