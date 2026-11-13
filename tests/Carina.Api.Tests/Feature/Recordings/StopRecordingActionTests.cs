using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Recordings;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Recordings;

[Trait("Category", "Feature")]
public sealed class StopRecordingActionTests
{
    private static readonly SessionId Session = SessionId.Parse("session-a");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AStopWithoutAReasonStopsNothing(string? reason)
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        feature.Driver.Writing(Session, recording.Id.Wire);

        (HttpStatusCode status, _) = await feature.PostAsync(
            $"/api/recordings/{recording.Id.Wire}/stop",
            new { reason });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty(feature.Driver.Stopped);
        Assert.Empty(recording.OutcomeDetail);
        Assert.Null(recording.AbortedAt);
    }

    [Fact]
    public async Task AStopWithNoBodyAtAllStopsNothing()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        feature.Driver.Writing(Session, recording.Id.Wire);

        (HttpStatusCode status, _) = await feature.PostAsync($"/api/recordings/{recording.Id.Wire}/stop");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty(feature.Driver.Stopped);
    }

    [Fact]
    public async Task TheReasonAStopWasAskedForReachesTheDriverAndStaysOnTheRecording()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        feature.Driver.Writing(Session, recording.Id.Wire);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/recordings/{recording.Id.Wire}/stop",
            new { reason = "  the wrong programme  " });

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal((Session, "the wrong programme"), Assert.Single(feature.Driver.Stopped));

        OutcomeDetail kept = Assert.Single(recording.OutcomeDetail);

        Assert.Equal(RecordingFault.StoppedByHand, kept.Fault);
        Assert.Equal("the wrong programme", kept.Note);
        Assert.Equal(RecordingFeature.Noon.AddMinutes(30), recording.AbortedAt);

        JsonElement answered = Assert.Single(
            body.GetProperty("data").GetProperty("recording").GetProperty("recording")
                .GetProperty("outcomeDetail").EnumerateArray().ToArray());

        Assert.Equal("stoppedByHand", answered.GetProperty("fault").GetString());
        Assert.Equal("the wrong programme", answered.GetProperty("note").GetString());
    }

    [Fact]
    public async Task ARecordingThatHasAlreadyEndedIsNotStoppedAgain()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        recording.Wrote(TimeSpan.FromHours(1));
        recording.Abort(RecordingFeature.Noon.AddHours(1));
        recording.Settle(RecordingOutcome.Complete, 1_000, RecordingFeature.Noon.AddHours(1));
        feature.Driver.Writing(Session, recording.Id.Wire);

        (HttpStatusCode status, _) = await feature.PostAsync(
            $"/api/recordings/{recording.Id.Wire}/stop",
            new { reason = "too late" });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Empty(feature.Driver.Stopped);
    }

    [Fact]
    public async Task ALedgerRowNothingIsWritingIsARecordingToRecoverRatherThanOneToStop()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();

        (HttpStatusCode status, _) = await feature.PostAsync(
            $"/api/recordings/{recording.Id.Wire}/stop",
            new { reason = "nobody is writing this" });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Empty(recording.OutcomeDetail);
        Assert.Null(recording.AbortedAt);
    }

    [Fact]
    public async Task ADriverThatCannotBeReachedLeavesTheRecordingUntouched()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        feature.Driver.Writing(Session, recording.Id.Wire);
        feature.Driver.Unreachable = "the socket is not there";

        (HttpStatusCode status, _) = await feature.PostAsync(
            $"/api/recordings/{recording.Id.Wire}/stop",
            new { reason = "the wrong programme" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Empty(recording.OutcomeDetail);
        Assert.Null(recording.AbortedAt);
    }

    [Fact]
    public async Task ADriverThatRefusesTheStopLeavesTheRecordingUntouched()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        feature.Driver.Writing(Session, recording.Id.Wire);
        feature.Driver.RefusesToStop = new DriverProblem("http409", ["the session is already stopping"]);

        (HttpStatusCode status, _) = await feature.PostAsync(
            $"/api/recordings/{recording.Id.Wire}/stop",
            new { reason = "the wrong programme" });

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Empty(recording.OutcomeDetail);
        Assert.Null(recording.AbortedAt);
    }

    [Fact]
    public async Task AStopAnotherSiteCouldPostAsAFormIsRefused()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        feature.Driver.Writing(Session, recording.Id.Wire);

        using var form = new StringContent("reason=whatever", System.Text.Encoding.UTF8);
        form.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using HttpResponseMessage response = await feature.Client.PostAsync(
            new Uri($"/api/recordings/{recording.Id.Wire}/stop", UriKind.Relative),
            form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(feature.Driver.Stopped);
    }

    [Fact]
    public async Task ASecondStopFindsNothingLeftToStopAndAddsNoSecondReason()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        feature.Driver.Writing(Session, recording.Id.Wire);

        await feature.PostAsync(
            $"/api/recordings/{recording.Id.Wire}/stop",
            new { reason = "the wrong programme" });

        (HttpStatusCode status, _) = await feature.PostAsync(
            $"/api/recordings/{recording.Id.Wire}/stop",
            new { reason = "again" });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Single(feature.Driver.Stopped);
        Assert.Single(recording.OutcomeDetail);
    }

    [Fact]
    public async Task ARecordingThatEndsWhileItIsBeingStoppedLeavesTheDriverStoppedAndTheReasonUnwritten()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        feature.Driver.Writing(Session, recording.Id.Wire);
        feature.Recordings.WhenHalting = () =>
        {
            recording.Wrote(TimeSpan.FromMinutes(30));
            recording.Note(new OutcomeDetail(
                RecordingFault.DriverLost,
                null,
                string.Empty,
                RecordingFeature.Noon.AddMinutes(20)));
            recording.Settle(RecordingOutcome.Failed, 0, RecordingFeature.Noon.AddMinutes(25));
        };

        (HttpStatusCode status, _) = await feature.PostAsync(
            $"/api/recordings/{recording.Id.Wire}/stop",
            new { reason = "the wrong programme" });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Single(feature.Driver.Stopped);
        Assert.DoesNotContain(
            recording.OutcomeDetail,
            detail => detail.Fault is RecordingFault.StoppedByHand);
        Assert.Null(recording.AbortedAt);
    }

    [Fact]
    public async Task AStopIsNotAskedForOfARecordingNobodyHas()
    {
        await using var feature = new RecordingFeature();

        (HttpStatusCode status, _) = await feature.PostAsync(
            $"/api/recordings/{Guid.NewGuid():N}/stop",
            new { reason = "the wrong programme" });

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Empty(feature.Driver.Stopped);
    }

    [Fact]
    public async Task AStopNeverReachesForTheSessionOfADifferentRecording()
    {
        await using var feature = new RecordingFeature();
        Recording mine = feature.Held(eventId: 1);
        Recording other = feature.Held(eventId: 2);
        feature.Driver.Writing(Session, other.Id.Wire);

        (HttpStatusCode status, _) = await feature.PostAsync(
            $"/api/recordings/{mine.Id.Wire}/stop",
            new { reason = "the wrong programme" });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Empty(feature.Driver.Stopped);
        Assert.Empty(mine.OutcomeDetail);
        Assert.Empty(other.OutcomeDetail);
        Assert.Null(other.AbortedAt);
    }

    [Fact]
    public async Task AStopIsAcknowledgedAsSomethingAskedForRatherThanSomethingFinished()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        feature.Driver.Writing(Session, recording.Id.Wire);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/recordings/{recording.Id.Wire}/stop",
            new { reason = "the wrong programme" });
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.True(data.GetProperty("stopWasAsked").GetBoolean());
        Assert.True(data.GetProperty("stillWriting").GetBoolean());
        Assert.Equal("the wrong programme", data.GetProperty("reason").GetString());
        Assert.Equal(
            RecordingFeature.Noon.AddMinutes(30),
            data.GetProperty("askedAt").GetDateTime());
        Assert.Equal(
            "inFlight",
            data.GetProperty("recording").GetProperty("recording").GetProperty("standing").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            data.GetProperty("recording").GetProperty("recording").GetProperty("outcome").ValueKind);
    }

    [Fact]
    public async Task ARecordingThatWasAskedToStopIsStillBeingWrittenUntilTheLedgerSaysOtherwise()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        feature.Driver.Writing(Session, recording.Id.Wire);

        await feature.PostAsync(
            $"/api/recordings/{recording.Id.Wire}/stop",
            new { reason = "the wrong programme" });

        (_, JsonElement detail) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        (_, JsonElement listed) = await feature.GetAsync("/api/recordings?standing=inFlight");

        Assert.Equal(
            "inFlight",
            detail.GetProperty("data").GetProperty("recording").GetProperty("standing").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            detail.GetProperty("data").GetProperty("recording").GetProperty("outcome").ValueKind);
        Assert.Equal(recording.Id.Wire, Only(listed).GetProperty("id").GetString());
    }

    [Fact]
    public async Task TheTwoWaysAStopCanBeRefusedAreToldApart()
    {
        await using var feature = new RecordingFeature();
        Recording ended = Ended(feature, eventId: 1);
        Recording writing = feature.Held(eventId: 2);
        feature.Driver.Writing(Session, ended.Id.Wire);

        (HttpStatusCode over, JsonElement overBody) = await feature.PostAsync(
            $"/api/recordings/{ended.Id.Wire}/stop",
            new { reason = "too late" });
        (HttpStatusCode nobody, JsonElement nobodyBody) = await feature.PostAsync(
            $"/api/recordings/{writing.Id.Wire}/stop",
            new { reason = "nobody is writing this" });

        string overMessage = overBody.GetProperty("message").GetString()!;
        string nobodyMessage = nobodyBody.GetProperty("message").GetString()!;

        Assert.Equal(HttpStatusCode.Conflict, over);
        Assert.Equal(HttpStatusCode.Conflict, nobody);
        Assert.NotEqual(overMessage, nobodyMessage);
        Assert.Contains("Complete", overMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("recover", overMessage, StringComparison.Ordinal);
        Assert.Contains("recover", nobodyMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Complete", nobodyMessage, StringComparison.Ordinal);
    }

    private static Recording Ended(RecordingFeature feature, int eventId = 4001)
    {
        Recording recording = feature.Held(eventId: eventId);

        recording.Wrote(TimeSpan.FromHours(1));
        recording.Abort(RecordingFeature.Noon.AddHours(1));
        recording.Settle(RecordingOutcome.Complete, 1_000, RecordingFeature.Noon.AddHours(1));

        return recording;
    }

    private static JsonElement Only(JsonElement body)
    {
        JsonElement items = body.GetProperty("data").GetProperty("items");

        Assert.Equal(1, items.GetArrayLength());

        return items[0];
    }
}
