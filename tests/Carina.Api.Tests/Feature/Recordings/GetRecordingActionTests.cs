using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Auth;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Viewing;

namespace Carina.Api.Tests.Feature.Recordings;

[Trait("Category", "Feature")]
public sealed class GetRecordingActionTests
{
    private const string Cast = "Cast\nA. Mirek, B. Sandoval\nProduction\nThe second half is a repeat.";

    private const int ShelfSize = 53;

    private const int Stations = 6;

    private static readonly TimeSpan BetweenAirings = TimeSpan.FromHours(60);

    private static readonly DateTime FirstAired = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime EndedAt = RecordingFeature.Noon.AddHours(1);

    private static readonly OutputRoot EncodeShelf = new("encodes");

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
    public async Task ARecordingWhoseEndWasFollowedLaterSaysTheEndItWasPromisedOnTheListAndOnTheDetail()
    {
        await using var feature = new RecordingFeature();
        Recording followed = feature.Held();
        DateTime promised = followed.ExpectedWindowEnd;
        followed.Extend(promised.AddMinutes(25));

        (_, JsonElement listed) = await feature.GetAsync("/api/recordings");
        (_, JsonElement detail) = await feature.GetAsync($"/api/recordings/{followed.Id.Wire}");
        JsonElement item = Assert.Single(listed.GetProperty("data").GetProperty("items").EnumerateArray());
        JsonElement described = detail.GetProperty("data");

        Assert.Equal(promised, item.GetProperty("promisedWindowEnd").GetDateTime());
        Assert.Equal(promised.AddMinutes(25), item.GetProperty("expectedWindow").GetProperty("end").GetDateTime());
        Assert.Equal(promised, described.GetProperty("recording").GetProperty("promisedWindowEnd").GetDateTime());
        Assert.Equal(
            promised.AddMinutes(25),
            described.GetProperty("reconciliation").GetProperty("expectedWindow").GetProperty("end").GetDateTime());
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

    private static Recording Ended(RecordingFeature feature, int eventId = 4001)
    {
        Recording recording = feature.Held(eventId: eventId);

        recording.Wrote(TimeSpan.FromHours(1));
        recording.Abort(RecordingFeature.Noon.AddHours(1));
        recording.Settle(RecordingOutcome.Complete, 1_000, RecordingFeature.Noon.AddHours(1));

        return recording;
    }

    [Fact]
    public async Task TheOneRecordingAskedForCarriesTheSameThreeAnswersThePageDoes()
    {
        await using var feature = new RecordingFeature();
        Recording held = feature.Held(
            extended: Cast,
            genres: [new ProgrammeGenre(7, 1)],
            groupKey: "an-evening-in-three-parts",
            groupRole: BroadcastGroupRole.MovementPrimary);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/recordings/{held.Id.Wire}");
        JsonElement recording = body.GetProperty("data").GetProperty("recording");
        JsonElement programme = recording.GetProperty("programme");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Cast, programme.GetProperty("extended").GetString());
        Assert.Equal(7, programme.GetProperty("genres")[0].GetProperty("kind").GetInt32());
        Assert.Equal(1, programme.GetProperty("genres")[0].GetProperty("sort").GetInt32());
        Assert.Equal(
            "an-evening-in-three-parts",
            recording.GetProperty("broadcastGroup").GetProperty("key").GetString());
        Assert.Equal("movementPrimary", recording.GetProperty("broadcastGroup").GetProperty("role").GetString());
    }

    [Fact]
    public async Task TheOneRecordingSaysTheSameThingTheListSaidAboutIt()
    {
        await using var feature = new RecordingFeature();
        Recording recording = Measured(feature, dropped: 0, total: 5_000_000, scrambled: 4_800_000);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement drops = body.GetProperty("data").GetProperty("recording").GetProperty("drops");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("mayNotBeWatchable", drops.GetProperty("quality").GetString());
        Assert.Equal("mayNotBeWatchable", drops.GetProperty("scrambleQuality").GetString());
    }

    [Fact(DisplayName = "BR-KD-031: a recording descrambled since is read by what it lost alone, and still says how much was left scrambled when it was received")]
    public async Task ARecordingDescrambledSinceIsReadByWhatItLostAlone()
    {
        await using var feature = new RecordingFeature();
        Recording recording = Measured(feature, dropped: 0, total: 5_000_000, scrambled: 4_800_000);
        DateTime ended = RecordingFeature.Noon.AddHours(1);

        recording.Wrote(TimeSpan.FromHours(1));
        recording.Note(new OutcomeDetail(RecordingFault.ScramblingUnresolved, null, string.Empty, RecordingFeature.Noon));
        recording.Abort(ended);
        recording.Settle(RecordingOutcome.Complete, 1_234_567, ended);

        (_, JsonElement before) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement leftScrambled = before.GetProperty("data").GetProperty("recording").GetProperty("drops");

        recording.Descrambled(ended.AddDays(1));

        (_, JsonElement list) = await feature.GetAsync("/api/recordings");
        (_, JsonElement one) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        (_, JsonElement clean) = await feature.GetAsync("/api/recordings?drops=clean");

        JsonElement listed = list.GetProperty("data").GetProperty("items")[0].GetProperty("drops");
        JsonElement detailed = one.GetProperty("data").GetProperty("recording").GetProperty("drops");

        Assert.Equal("mayNotBeWatchable", leftScrambled.GetProperty("quality").GetString());
        Assert.Equal("mayNotBeWatchable", leftScrambled.GetProperty("scrambleQuality").GetString());
        Assert.Equal("good", listed.GetProperty("quality").GetString());
        Assert.Equal("good", listed.GetProperty("scrambleQuality").GetString());
        Assert.Equal("good", detailed.GetProperty("quality").GetString());
        Assert.Equal("good", detailed.GetProperty("scrambleQuality").GetString());
        Assert.Equal(4_800_000, detailed.GetProperty("scrambledPackets").GetInt64());
        Assert.Equal(
            recording.Id.Wire,
            Assert.Single(clean.GetProperty("data").GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
    }

    private static Recording Measured(RecordingFeature feature, long dropped, long total, long scrambled)
    {
        Recording recording = feature.Held();

        recording.Measure(
            DropCounters.Counted(dropped, total),
            DropTimeline.Unlocated,
            scrambled,
            0,
            RecordingFeature.Noon.AddMinutes(10));

        return recording;
    }

    [Fact(DisplayName = "the detail of every carried recording says nothing was counted, as the list did")]
    public async Task TheDetailOfEveryCarriedRecordingSaysNothingWasCounted()
    {
        await using var feature = new RecordingFeature();

        foreach (Recording carried in Carry(feature))
        {
            (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/recordings/{carried.Id.Wire}");
            JsonElement data = body.GetProperty("data");

            Assert.Equal(HttpStatusCode.OK, status);
            SaysNothingWasCounted(data.GetProperty("recording").GetProperty("drops"));
            Assert.False(data.GetProperty("positions").GetProperty("located").GetBoolean());
        }
    }

    private static List<Recording> Carry(RecordingFeature feature)
    {
        List<Recording> carried = [];

        foreach (int at in Enumerable.Range(0, ShelfSize))
        {
            Recording recording = Carried(at);
            feature.Recordings.Recordings.Add(recording);
            carried.Add(recording);
        }

        return carried;
    }

    private static Recording Carried(int at)
    {
        RecordingId id = RecordingId.New();
        DateTime start = FirstAired + (BetweenAirings * at);
        DateTime end = start.AddMinutes(30);

        return Recording.Rehydrate(
            id,
            null,
            new ProgrammeRef(new NetworkId(7), new ServiceId(1_001 + (at % Stations)), new EventId(5_001 + at), start),
            new OutputRoot("carried"),
            RecordingFileName.For(id, ".m2ts"),
            188 * (at + 1),
            end,
            start,
            end,
            end,
            (long)(end - start).TotalMilliseconds,
            0,
            [],
            start,
            end,
            end,
            RecordingOutcome.Complete,
            [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            null,
            ThumbnailState.Pending,
            ProgrammeSnapshot.Of(
                string.Create(CultureInfo.InvariantCulture, $"a carried programme {at + 1}"),
                string.Empty,
                [],
                [],
                start,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone);
    }

    private static void SaysNothingWasCounted(JsonElement drops)
    {
        Assert.Equal("unmeasured", drops.GetProperty("quality").GetString());
        Assert.Equal("unmeasured", drops.GetProperty("scrambleQuality").GetString());
        Assert.False(drops.GetProperty("ccMeasured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, drops.GetProperty("ccDroppedPackets").ValueKind);
        Assert.Equal(JsonValueKind.Null, drops.GetProperty("ccTotalPackets").ValueKind);
        Assert.Equal(JsonValueKind.Null, drops.GetProperty("scrambledPackets").ValueKind);
        Assert.Equal(JsonValueKind.Null, drops.GetProperty("eovfCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, drops.GetProperty("measuredUpdatedAt").ValueKind);
    }

    [Fact]
    public async Task ARecordingThatCameOutWholeButScrambledSaysSoInTheDetailAndTheList()
    {
        await using var feature = new RecordingFeature();
        Recording recording = Scrambled(feature);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement detail = body.GetProperty("data").GetProperty("recording");
        (_, JsonElement list) = await feature.GetAsync("/api/recordings");
        JsonElement listed = Assert.Single(list.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("complete", detail.GetProperty("outcome").GetString());
        Assert.True(detail.GetProperty("leftScrambled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("descrambledAt").ValueKind);
        Assert.True(listed.GetProperty("leftScrambled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("descrambledAt").ValueKind);
    }

    [Fact]
    public async Task ARecordingDescrambledSinceSaysWhenAndIsNoLongerLeftScrambled()
    {
        await using var feature = new RecordingFeature();
        Recording recording = Scrambled(feature);
        recording.Descrambled(EndedAt.AddDays(1));

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement detail = body.GetProperty("data").GetProperty("recording");

        Assert.False(detail.GetProperty("leftScrambled").GetBoolean());
        Assert.Equal(EndedAt.AddDays(1), detail.GetProperty("descrambledAt").GetDateTime().ToUniversalTime());
        Assert.Equal("complete", detail.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task ARecordingThatNamesNoScramblingIsNotLeftScrambled()
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        recording.Wrote(TimeSpan.FromHours(1));
        recording.Abort(EndedAt);
        recording.Settle(RecordingOutcome.Complete, 1_234_567, EndedAt);

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement detail = body.GetProperty("data").GetProperty("recording");

        Assert.False(detail.GetProperty("leftScrambled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("descrambledAt").ValueKind);
    }

    private static Recording Scrambled(RecordingFeature feature)
    {
        Recording recording = feature.Held();
        recording.Wrote(TimeSpan.FromHours(1));
        recording.Note(new OutcomeDetail(
            RecordingFault.ScramblingUnresolved,
            null,
            string.Empty,
            RecordingFeature.Noon));
        recording.Abort(EndedAt);
        recording.Settle(RecordingOutcome.Complete, 1_234_567, EndedAt);

        return recording;
    }

    [Fact]
    public async Task ARecordingThatCarriedOnAfterAGapSaysWhereAndHowLongInTheDetailAndTheList()
    {
        await using RecordingFeature feature = new();
        Recording recording = feature.Held();
        recording.Missed(new RecordingGap(
            recording.StartedAtActual.AddSeconds(312),
            recording.StartedAtActual.AddSeconds(314.5)));
        recording.Wrote(TimeSpan.FromHours(1));
        recording.Abort(EndedAt);
        recording.Settle(RecordingOutcome.Complete, 1_234_567, EndedAt);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement detail = body.GetProperty("data").GetProperty("recording");
        (_, JsonElement list) = await feature.GetAsync("/api/recordings");
        JsonElement listed = Assert.Single(list.GetProperty("data").GetProperty("items").EnumerateArray());
        JsonElement gap = Assert.Single(detail.GetProperty("gaps").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("complete", detail.GetProperty("outcome").GetString());
        Assert.Equal(2.5, gap.GetProperty("seconds").GetDouble(), 3);
        Assert.Equal(312, gap.GetProperty("atSecond").GetDouble(), 3);
        Assert.Equal(2_500, detail.GetProperty("missedMs").GetInt64());
        Assert.Equal(2_500, listed.GetProperty("missedMs").GetInt64());
        Assert.Single(listed.GetProperty("gaps").EnumerateArray());
    }

    [Fact]
    public async Task ARecordingThatMissedNothingAnswersNoGap()
    {
        await using RecordingFeature feature = new();
        Recording recording = feature.Held();

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");
        JsonElement detail = body.GetProperty("data").GetProperty("recording");

        Assert.Empty(detail.GetProperty("gaps").EnumerateArray());
        Assert.Equal(0, detail.GetProperty("missedMs").GetInt64());
    }

    [Fact(DisplayName = "a recording still being written stands unencoded rather than saying nothing")]
    public async Task ARecordingStillBeingWrittenStandsUnencoded()
    {
        await using var feature = new RecordingFeature();
        Recording writing = feature.Held();

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{writing.Id.Wire}");

        Assert.Equal(
            "notEncoded",
            body.GetProperty("data").GetProperty("recording").GetProperty("encode").GetProperty("standing").GetString());
    }

    [Fact(DisplayName = "one recording's detail says what its row in the list says")]
    public async Task OneRecordingsDetailSaysWhatItsRowInTheListSays()
    {
        await using var feature = new RecordingFeature();
        Recording recording = EndedWithEncodeChoice(feature, eventId: 4);
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

    [Fact(DisplayName = "one recording's detail says whether it asks for an encode, the same as its row")]
    public async Task OneRecordingsDetailSaysWhetherItAsksForAnEncode()
    {
        await using var feature = new RecordingFeature();
        Recording passedOver = EndedWithEncodeChoice(feature, eventId: 8, encodeWhenRecorded: false);

        (HttpStatusCode status, JsonElement detail) = await feature.GetAsync($"/api/recordings/{passedOver.Id.Wire}");
        (_, JsonElement listed) = await feature.GetAsync("/api/recordings");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(detail
            .GetProperty("data")
            .GetProperty("recording")
            .GetProperty("encode")
            .GetProperty("whenRecorded")
            .GetBoolean());
        Assert.False(listed
            .GetProperty("data")
            .GetProperty("items")[0]
            .GetProperty("encode")
            .GetProperty("whenRecorded")
            .GetBoolean());
    }

    [Fact(DisplayName = "a recording still being written says it asks for an encode rather than saying nothing")]
    public async Task ARecordingStillBeingWrittenSaysItAsksForAnEncode()
    {
        await using var feature = new RecordingFeature();
        Recording writing = feature.Held();

        (_, JsonElement body) = await feature.GetAsync($"/api/recordings/{writing.Id.Wire}");

        Assert.True(body
            .GetProperty("data")
            .GetProperty("recording")
            .GetProperty("encode")
            .GetProperty("whenRecorded")
            .GetBoolean());
    }

    private static Recording EndedWithEncodeChoice(RecordingFeature feature, int eventId, bool encodeWhenRecorded = true)
    {
        Recording recording = feature.Held(eventId: eventId, encodeWhenRecorded: encodeWhenRecorded);
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
            EncodeShelf,
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

    [Fact(DisplayName = "one recording's detail says how far this viewer watched it, in one lookup")]
    public async Task OneRecordingsDetailSaysHowFarThisViewerWatchedIt()
    {
        await using var feature = new RecordingFeature();
        Recording watched = EndedAfterAnHour(feature, eventId: 30);
        Recording unwatched = EndedAfterAnHour(feature, eventId: 31);
        Recording sharedWithSomeoneElse = EndedAfterAnHour(feature, eventId: 32);

        feature.Positions.Positions.Add(Reached(watched, TestAuthenticationHandler.Tester, 612.5));
        feature.Positions.Positions.Add(Reached(sharedWithSomeoneElse, "someone else", 900));

        JsonElement seen = await DetailOfAsync(feature, watched);

        Assert.Equal(612.5, seen.GetProperty("watchedPositionSec").GetDouble());
        Assert.Equal(1, feature.Positions.ReadsOfOne);
        Assert.Equal(0, feature.Positions.ReadsOfMany);
        Assert.Equal(JsonValueKind.Null, (await DetailOfAsync(feature, unwatched)).GetProperty("watchedPositionSec").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            (await DetailOfAsync(feature, sharedWithSomeoneElse)).GetProperty("watchedPositionSec").ValueKind);
    }

    [Fact(DisplayName = "the screen is given both numbers it needs: how long a recording is, and where the watching got to")]
    public async Task TheScreenIsGivenBothNumbersItNeeds()
    {
        await using var recordings = new RecordingFeature();
        Recording written = Settled(recordings);

        (HttpStatusCode standing, JsonElement detail) =
            await recordings.GetAsync($"/api/recordings/{written.Id.Wire}");

        await using var feature = new PlayFeature();
        Recording playing = feature.Ended(RecordingOutcome.Complete);

        await KeepAsync(feature.Client, playing.Id.Wire, new { positionSec = 600 });

        using HttpResponseMessage answer = await feature.PlanAsync(playing);
        JsonElement plan = (await PlayFeature.PlanOfAsync(answer)).GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, standing);
        Assert.Equal(
            (long)TimeSpan.FromMinutes(30).TotalMilliseconds,
            detail.GetProperty("data").GetProperty("recording").GetProperty("writtenDurationMs").GetInt64());
        Assert.Equal(600, plan.GetProperty("resumeAtSec").GetDouble());
    }

    private static async Task<JsonElement> DetailOfAsync(RecordingFeature feature, Recording recording)
    {
        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/recordings/{recording.Id.Wire}");

        Assert.Equal(HttpStatusCode.OK, status);

        return body.GetProperty("data").GetProperty("recording");
    }

    private static PlaybackPosition Reached(Recording recording, string viewer, double seconds)
        => PlaybackPosition.Reached(
            recording.Id,
            new Subject(viewer),
            TimeSpan.FromSeconds(seconds),
            RecordingFeature.Noon.AddHours(2));

    private static Recording EndedAfterAnHour(RecordingFeature feature, int eventId)
    {
        Recording recording = feature.Held(eventId: eventId);
        recording.Wrote(TimeSpan.FromHours(1));
        recording.Abort(RecordingFeature.Noon.AddHours(1));
        recording.Settle(RecordingOutcome.Complete, 1_000_000, RecordingFeature.Noon.AddHours(1));

        return recording;
    }

    private static Recording Settled(RecordingFeature feature, int eventId = 4_001)
    {
        Recording held = feature.Held(eventId: eventId);

        held.Wrote(TimeSpan.FromMinutes(30));
        held.Abort(RecordingFeature.Noon.AddMinutes(30));
        held.Settle(RecordingOutcome.Complete, 4_000, RecordingFeature.Noon.AddMinutes(30));

        return held;
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
