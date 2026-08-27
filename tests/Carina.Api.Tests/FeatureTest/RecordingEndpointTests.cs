using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Thumbnails;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class RecordingEndpointTests
{
    private static readonly SessionId Session = SessionId.Parse("session-a");

    [Fact]
    public async Task ThePageSaysHowManyRecordingsThereAreAndWhichPageThisIs()
    {
        await using var feature = new RecordingFeature();
        feature.Held(eventId: 1);
        feature.Held(eventId: 2);
        feature.Held(eventId: 3);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings?perPage=2");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        Assert.Equal(1, data.GetProperty("currentPage").GetInt32());
        Assert.Equal(2, data.GetProperty("lastPage").GetInt32());
        Assert.Equal(2, data.GetProperty("perPage").GetInt32());
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("from=2026-01-01T00:00:00Z&to=2027-06-01T00:00:00Z")]
    [InlineData("from=2026-08-24T12:00:00Z&to=2026-08-24T11:00:00Z")]
    [InlineData("standing=inFlight&outcome=complete")]
    [InlineData("standing=99")]
    [InlineData("drops=99")]
    [InlineData("outcome=99")]
    [InlineData("sort=99")]
    [InlineData("channel=nonsense")]
    public async Task ARequestOutsideWhatTheEndpointAnswersIsRefused(string query)
    {
        await using var feature = new RecordingFeature();
        feature.Held();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/recordings?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task APageSizeOverTheCeilingIsCutDownToItAndAnsweredAsTheSizeThatWasUsed()
    {
        await using var feature = new RecordingFeature();

        foreach (int eventId in Enumerable.Range(1, RecordingQuery.MostPerPage + 1))
        {
            feature.Held(eventId: eventId);
        }

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/recordings?perPage={RecordingQuery.MostPerPage + 1}");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(RecordingQuery.MostPerPage + 1, data.GetProperty("total").GetInt32());
        Assert.Equal(RecordingQuery.MostPerPage, data.GetProperty("items").GetArrayLength());
        Assert.Equal(RecordingQuery.MostPerPage, data.GetProperty("perPage").GetInt32());
        Assert.Equal(2, data.GetProperty("lastPage").GetInt32());
    }

    [Fact]
    public async Task ThePageSizeAnsweredIsTheOneTheAnswerWasCutTo()
    {
        await using var feature = new RecordingFeature();

        foreach (int eventId in Enumerable.Range(1, 5))
        {
            feature.Held(eventId: eventId);
        }

        (_, JsonElement body) = await feature.GetAsync("/api/recordings?perPage=2");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(data.GetProperty("items").GetArrayLength(), data.GetProperty("perPage").GetInt32());
        Assert.Equal(2, data.GetProperty("perPage").GetInt32());
    }

    [Fact]
    public async Task APageSizeBelowOneIsAnsweredAsTheSizeThatWasUsedInstead()
    {
        await using var feature = new RecordingFeature();
        feature.Held();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings?perPage=0");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            RecordingQuery.DefaultPerPage,
            body.GetProperty("data").GetProperty("perPage").GetInt32());
    }

    [Fact]
    public async Task ThePageSizeAtTheCeilingIsAnswered()
    {
        await using var feature = new RecordingFeature();
        feature.Held();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/recordings?perPage={RecordingQuery.MostPerPage}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(RecordingQuery.MostPerPage, body.GetProperty("data").GetProperty("perPage").GetInt32());
    }

    [Fact]
    public async Task NothingCountedIsNotTheSameAnswerAsCountedAndClean()
    {
        await using var feature = new RecordingFeature();
        Recording counted = feature.Held(eventId: 1);
        counted.Measure(DropCounters.Counted(0, 1000), DropTimeline.Unlocated, null, 0, RecordingFeature.Noon);
        feature.Held(eventId: 2);

        (_, JsonElement clean) = await feature.GetAsync("/api/recordings?drops=clean");
        (_, JsonElement unmeasured) = await feature.GetAsync("/api/recordings?drops=unmeasured");

        JsonElement countedDrops = Only(clean).GetProperty("drops");
        JsonElement uncountedDrops = Only(unmeasured).GetProperty("drops");

        Assert.True(countedDrops.GetProperty("ccMeasured").GetBoolean());
        Assert.Equal(0, countedDrops.GetProperty("ccDroppedPackets").GetInt64());
        Assert.Equal(1000, countedDrops.GetProperty("ccTotalPackets").GetInt64());

        Assert.False(uncountedDrops.GetProperty("ccMeasured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, uncountedDrops.GetProperty("ccDroppedPackets").ValueKind);
        Assert.Equal(JsonValueKind.Null, uncountedDrops.GetProperty("ccTotalPackets").ValueKind);
    }

    [Fact]
    public async Task ARecordingThatLostPacketsIsTheOneTheDropFilterFinds()
    {
        await using var feature = new RecordingFeature();
        Recording lost = feature.Held(eventId: 1);
        lost.Measure(DropCounters.Counted(4, 1000), DropTimeline.Unlocated, null, 0, RecordingFeature.Noon);
        Recording clean = feature.Held(eventId: 2);
        clean.Measure(DropCounters.Counted(0, 1000), DropTimeline.Unlocated, null, 0, RecordingFeature.Noon);
        feature.Held(eventId: 3);

        (_, JsonElement body) = await feature.GetAsync("/api/recordings?drops=dropped");

        Assert.Equal(lost.Id.Wire, Only(body).GetProperty("id").GetString());
    }

    [Fact]
    public async Task ScramblingThatNothingCountedIsNotReadAsNoneLeftScrambled()
    {
        await using var feature = new RecordingFeature();
        feature.Held();

        (_, JsonElement body) = await feature.GetAsync("/api/recordings");

        Assert.Equal(
            JsonValueKind.Null,
            Only(body).GetProperty("drops").GetProperty("scrambledPackets").ValueKind);
    }

    [Fact]
    public async Task TheStateOfARecordingIsAnsweredBesideTheOutcomeItHasNotReachedYet()
    {
        await using var feature = new RecordingFeature();
        feature.Held();

        (_, JsonElement body) = await feature.GetAsync("/api/recordings?standing=inFlight");
        JsonElement row = Only(body);

        Assert.Equal("inFlight", row.GetProperty("standing").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("outcome").ValueKind);
    }

    [Fact]
    public async Task AChannelFilterAnswersOnlyTheRecordingsOfThatChannel()
    {
        await using var feature = new RecordingFeature();
        Recording wanted = feature.Held(networkId: 4, serviceId: 101, eventId: 1);
        feature.Held(networkId: 4, serviceId: 102, eventId: 2);

        (_, JsonElement body) = await feature.GetAsync("/api/recordings?channel=4-101");

        Assert.Equal(wanted.Id.Wire, Only(body).GetProperty("id").GetString());
    }

    [Fact]
    public async Task ASpanAnswersOnlyTheRecordingsWrittenInsideIt()
    {
        await using var feature = new RecordingFeature();
        Recording inside = feature.Held(eventId: 1, startedAt: RecordingFeature.Noon);
        feature.Held(eventId: 2, startedAt: RecordingFeature.Noon.AddDays(-2));

        (_, JsonElement body) = await feature.GetAsync(
            "/api/recordings?from=2026-08-24T00:00:00Z&to=2026-08-25T00:00:00Z");

        Assert.Equal(inside.Id.Wire, Only(body).GetProperty("id").GetString());
    }

    [Fact]
    public async Task TheDetailNamesEveryReasonSeparatelyRatherThanAsOneSentence()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        recording.Note(new OutcomeDetail(
            RecordingFault.TuneFailed,
            TuneFailureKind.IncompletePsi,
            "the table never completed",
            RecordingFeature.Noon));
        recording.Note(new OutcomeDetail(
            RecordingFault.ScramblingUnresolved,
            null,
            "the card said no",
            RecordingFeature.Noon));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement reasons = body.GetProperty("data").GetProperty("recording").GetProperty("outcomeDetail");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, reasons.GetArrayLength());
        Assert.Equal("tuneFailed", reasons[0].GetProperty("fault").GetString());
        Assert.Equal("incompletePsi", reasons[0].GetProperty("tuneFailure").GetString());
        Assert.Equal("the table never completed", reasons[0].GetProperty("note").GetString());
        Assert.Equal("scramblingUnresolved", reasons[1].GetProperty("fault").GetString());
        Assert.Equal(JsonValueKind.Null, reasons[1].GetProperty("tuneFailure").ValueKind);
    }

    [Fact]
    public async Task TheDetailWeighsTheFileAgainstTheWindowThatWasPromised()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Note(new OutcomeDetail(RecordingFault.DriverLost, null, string.Empty, RecordingFeature.Noon));
        recording.Settle(RecordingOutcome.Truncated, 1_234_567, RecordingFeature.Noon.AddMinutes(30));

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement weighed = body.GetProperty("data").GetProperty("reconciliation");

        Assert.True(weighed.GetProperty("sizeObserved").GetBoolean());
        Assert.Equal(1_234_567, weighed.GetProperty("fileSizeBytes").GetInt64());
        Assert.Equal(1_800_000, weighed.GetProperty("writtenDurationMs").GetInt64());
        Assert.Equal(3_600_000, weighed.GetProperty("expectedWindow").GetProperty("durationMs").GetInt64());
        Assert.Equal(0.5, weighed.GetProperty("coverage").GetDouble(), 6);
        Assert.True(weighed.GetProperty("stoppedUnasked").GetBoolean());
    }

    [Fact]
    public async Task TheDetailSaysHowOftenTheRecordingBrokeAndWhetherItCameBack()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        recording.Interrupt(RecordingFault.DriverLost, RecordingFeature.Noon.AddMinutes(5));
        recording.Resume(RecordingFeature.Noon.AddMinutes(6));
        recording.Interrupt(RecordingFault.DiskExhausted, RecordingFeature.Noon.AddMinutes(9));

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement broke = body.GetProperty("data").GetProperty("interruptions");

        Assert.Equal(2, broke.GetArrayLength());
        Assert.Equal("driverLost", broke[0].GetProperty("fault").GetString());
        Assert.NotEqual(JsonValueKind.Null, broke[0].GetProperty("resumedAt").ValueKind);
        Assert.Equal("diskExhausted", broke[1].GetProperty("fault").GetString());
        Assert.Equal(JsonValueKind.Null, broke[1].GetProperty("resumedAt").ValueKind);
        Assert.Equal(1, body.GetProperty("data").GetProperty("recording").GetProperty("resumeCount").GetInt32());
    }

    [Fact]
    public async Task TheDetailSaysWhyThereIsNoPicture()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        recording.Illustrate(ThumbnailState.Failed, ThumbnailFault.SourceOutOfReach);

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement picture = body.GetProperty("data").GetProperty("recording").GetProperty("thumbnail");

        Assert.Equal("failed", picture.GetProperty("state").GetString());
        Assert.Equal("sourceOutOfReach", picture.GetProperty("fault").GetString());
    }

    [Fact]
    public async Task APictureOfSomethingCutShortSaysThatItIsCutShort()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Note(new OutcomeDetail(RecordingFault.DriverLost, null, string.Empty, RecordingFeature.Noon));
        recording.Settle(RecordingOutcome.Truncated, 1_000, RecordingFeature.Noon.AddMinutes(30));
        recording.Illustrate(ThumbnailState.Ready);

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement picture = body.GetProperty("data").GetProperty("recording").GetProperty("thumbnail");

        Assert.Equal("ready", picture.GetProperty("state").GetString());
        Assert.True(picture.GetProperty("showsAnUnfinishedRecording").GetBoolean());
    }

    [Fact]
    public async Task ARecordingNobodyHasIsNotFound()
    {
        await using var feature = new RecordingFeature();

        (HttpStatusCode status, _) = await feature.GetAsync($"/api/recordings/{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdefg")]
    public async Task ANameThatIsNotOneTheLedgerCouldHoldIsRefused(string id)
    {
        await using var feature = new RecordingFeature();

        (HttpStatusCode status, _) = await feature.GetAsync($"/api/recordings/{id}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

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

        using var form = new StringContent("reason=whatever", Encoding.UTF8);
        form.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using HttpResponseMessage response = await feature.Client.PostAsync(
            new Uri($"/api/recordings/{recording.Id.Wire}/stop", UriKind.Relative),
            form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(feature.Driver.Stopped);
    }

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

    [Theory]
    [InlineData("DELETE", "/api/recordings/{0}")]
    [InlineData("DELETE", "/api/recordings")]
    public async Task ThisDomainHasNoWayToDeleteARecording(string method, string path)
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();

        using var asking = new HttpRequestMessage(
            new HttpMethod(method),
            new Uri(
                string.Format(System.Globalization.CultureInfo.InvariantCulture, path, recording.Id.Wire),
                UriKind.Relative));
        using var empty = new StringContent("{}", Encoding.UTF8, "application/json");

        asking.Content = empty;

        using HttpResponseMessage response = await feature.Client.SendAsync(asking);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData("standing=sideways")]
    [InlineData("outcome=nearly")]
    [InlineData("drops=maybe")]
    [InlineData("sort=alphabetically")]
    [InlineData("perPage=abc")]
    [InlineData("page=abc")]
    [InlineData("from=whenever")]
    [InlineData("descending=perhaps")]
    public async Task AValueTheEndpointCannotEvenReadIsRefusedRatherThanIgnored(string query)
    {
        await using var feature = new RecordingFeature();
        feature.Held();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/recordings?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task TwoChannelsNamedSeparatelyAreBothAnswered()
    {
        await using var feature = new RecordingFeature();
        feature.Held(networkId: 4, serviceId: 101, eventId: 1);
        feature.Held(networkId: 4, serviceId: 102, eventId: 2);
        feature.Held(networkId: 4, serviceId: 103, eventId: 3);

        (_, JsonElement body) = await feature.GetAsync("/api/recordings?channel=4-101&channel=4-102");

        Assert.Equal(2, body.GetProperty("data").GetProperty("total").GetInt32());
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
    public async Task APictureIsNotAskedForOfARecordingNobodyHas()
    {
        await using var feature = new RecordingFeature();

        (HttpStatusCode status, _) = await feature.PostAsync(
            $"/api/recordings/{Guid.NewGuid():N}/thumbnail");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Empty(feature.Remaker.Asked);
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
    public async Task ARecordingStillBeingWrittenIsNotSaidToHaveStoppedWithoutBeingAsked()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");

        Assert.False(
            body.GetProperty("data").GetProperty("reconciliation").GetProperty("stoppedUnasked").GetBoolean());
    }

    [Fact]
    public async Task ARecordingThatEndedWithoutAnybodyAskingItToSaysSo()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Note(new OutcomeDetail(
            RecordingFault.DriverLost,
            null,
            string.Empty,
            RecordingFeature.Noon.AddMinutes(10)));
        recording.Settle(RecordingOutcome.Truncated, 1_000, RecordingFeature.Noon.AddMinutes(30));

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");

        Assert.True(
            body.GetProperty("data").GetProperty("reconciliation").GetProperty("stoppedUnasked").GetBoolean());
    }

    [Fact]
    public async Task ARecordingThisSideAskedToStopIsNotSaidToHaveStoppedUnasked()
    {
        await using var feature = new RecordingFeature();
        Recording recording = Ended(feature);

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");

        Assert.False(
            body.GetProperty("data").GetProperty("reconciliation").GetProperty("stoppedUnasked").GetBoolean());
    }

    [Fact]
    public async Task AWindowTooShortToMeasureInMillisecondsStillAnswersACoverage()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held(window: TimeSpan.FromTicks(5_000));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/recordings/{recording.Id.Wire}");
        JsonElement weighed = body.GetProperty("data").GetProperty("reconciliation");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, weighed.GetProperty("expectedWindow").GetProperty("durationMs").GetInt64());
        Assert.Equal(0, weighed.GetProperty("coverage").GetDouble());
    }

    [Fact]
    public async Task AWindowTooShortToMeasureInMillisecondsWeighsWhatWasWrittenAgainstIt()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held(window: TimeSpan.FromTicks(5_000));
        recording.Wrote(TimeSpan.FromMilliseconds(1));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/recordings/{recording.Id.Wire}");
        JsonElement weighed = body.GetProperty("data").GetProperty("reconciliation");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, weighed.GetProperty("expectedWindow").GetProperty("durationMs").GetInt64());
        Assert.Equal(1, weighed.GetProperty("writtenDurationMs").GetInt64());
        Assert.Equal(2, weighed.GetProperty("coverage").GetDouble(), 6);
    }

    [Fact]
    public async Task AWindowOfAWholeNumberOfMillisecondsIsWeighedTheSameWayAsAnyOther()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held(window: TimeSpan.FromMinutes(60));
        recording.Wrote(TimeSpan.FromMinutes(45));

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");

        Assert.Equal(
            0.75,
            body.GetProperty("data").GetProperty("reconciliation").GetProperty("coverage").GetDouble(),
            6);
    }

    [Fact]
    public async Task ARecordingNothingHasPlacedInTheStreamSaysThereIsNowhereToPutIt()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement positions = body.GetProperty("data").GetProperty("positions");

        Assert.False(positions.GetProperty("located").GetBoolean());
        Assert.Equal(JsonValueKind.Null, positions.GetProperty("anchorPcr").ValueKind);
        Assert.Empty(positions.GetProperty("buckets").EnumerateArray());
        Assert.Empty(positions.GetProperty("reanchors").EnumerateArray());
    }

    [Fact]
    public async Task ARecordingSomethingPlacedInTheStreamSaysWhereItWas()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        recording.Measure(
            DropCounters.Counted(3, 1000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 3, 0)], [new PcrReanchor(20, 5, 6)]),
            null,
            0,
            RecordingFeature.Noon);

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement positions = body.GetProperty("data").GetProperty("positions");

        Assert.True(positions.GetProperty("located").GetBoolean());
        Assert.Equal(900_000, positions.GetProperty("anchorPcr").GetInt64());
        Assert.Equal(12, positions.GetProperty("buckets")[0].GetProperty("second").GetInt32());
        Assert.Equal(20, positions.GetProperty("reanchors")[0].GetProperty("second").GetInt32());
    }

    [Fact]
    public async Task EveryRecordingIsHandedOverExactlyOnceAcrossThePagesThatHoldThem()
    {
        await using var feature = new RecordingFeature();

        foreach (int eventId in Enumerable.Range(1, 7))
        {
            feature.Held(eventId: eventId, startedAt: RecordingFeature.Noon);
        }

        var seen = new List<string>();

        foreach (int page in Enumerable.Range(1, 4))
        {
            (_, JsonElement body) = await feature.GetAsync($"/api/recordings?perPage=2&page={page}");

            seen.AddRange(body.GetProperty("data").GetProperty("items").EnumerateArray()
                .Select(row => row.GetProperty("id").GetString()!));
        }

        Assert.Equal(7, seen.Count);
        Assert.Equal(7, seen.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            feature.Recordings.Recordings.Select(recording => recording.Id.Wire).Order(StringComparer.Ordinal),
            seen.Order(StringComparer.Ordinal));
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
