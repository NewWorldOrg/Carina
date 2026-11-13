using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

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
public sealed class ListRecordingsActionTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const string Cast = "Cast\nA. Mirek, B. Sandoval\nProduction\nThe second half is a repeat.";

    private const int ShelfSize = 53;

    private const int Stations = 6;

    private static readonly TimeSpan BetweenAirings = TimeSpan.FromHours(60);

    private static readonly DateTime FirstAired = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private const string Newest = "/api/recordings?sort=startedAt&descending=true&perPage=2";

    private static readonly OutputRoot EncodeShelf = new("encodes");

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
        counted.Measure(DropCounters.Counted(0, 1000), DropTimeline.Unlocated, 0, 0, RecordingFeature.Noon);
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
    public async Task ARecordingThatLostNothingButWasLeftScrambledIsNotAnsweredAsClean()
    {
        await using var feature = new RecordingFeature();
        Recording unlocked = feature.Held(eventId: 1);
        unlocked.Measure(DropCounters.Counted(0, 1000), DropTimeline.Unlocated, 0, 0, RecordingFeature.Noon);
        Recording locked = feature.Held(eventId: 2);
        locked.Measure(DropCounters.Counted(0, 1000), DropTimeline.Unlocated, 900, 0, RecordingFeature.Noon);

        (_, JsonElement body) = await feature.GetAsync("/api/recordings?drops=clean");

        Assert.Equal(unlocked.Id.Wire, Only(body).GetProperty("id").GetString());
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
    public async Task TheListSaysWhenEachFileWasLastWeighed()
    {
        await using var feature = new RecordingFeature();
        Recording weighed = feature.Held(eventId: 1);
        weighed.Wrote(TimeSpan.FromMinutes(30));
        weighed.Note(new OutcomeDetail(RecordingFault.DriverLost, null, string.Empty, RecordingFeature.Noon));
        weighed.Settle(RecordingOutcome.Truncated, 1_234_567, RecordingFeature.Noon.AddMinutes(30));
        Recording writing = feature.Held(eventId: 2);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");
        JsonElement[] items = [.. body.GetProperty("data").GetProperty("items").EnumerateArray()];
        JsonElement settled = items.Single(item => item.GetProperty("id").GetString() == weighed.Id.Wire);
        JsonElement unweighed = items.Single(item => item.GetProperty("id").GetString() == writing.Id.Wire);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1_234_567, settled.GetProperty("fileSizeBytes").GetInt64());
        Assert.Equal(RecordingFeature.Noon.AddMinutes(30), settled.GetProperty("observedAt").GetDateTime());
        Assert.Equal(JsonValueKind.Null, unweighed.GetProperty("fileSizeBytes").ValueKind);
        Assert.Equal(JsonValueKind.Null, unweighed.GetProperty("observedAt").ValueKind);
    }

    [Fact]
    public async Task ARecordingNobodyFollowedWasPromisedTheEndItHas()
    {
        await using var feature = new RecordingFeature();
        Recording held = feature.Held();

        (_, JsonElement listed) = await feature.GetAsync("/api/recordings");
        JsonElement item = Assert.Single(listed.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal(held.ExpectedWindowEnd, item.GetProperty("promisedWindowEnd").GetDateTime());
        Assert.Equal(
            item.GetProperty("expectedWindow").GetProperty("end").GetDateTime(),
            item.GetProperty("promisedWindowEnd").GetDateTime());
    }

    [Theory]
    [InlineData(ThumbnailState.Pending, null, "pending")]
    [InlineData(ThumbnailState.Ready, null, "ready")]
    [InlineData(ThumbnailState.Skipped, null, "skipped")]
    [InlineData(ThumbnailState.Failed, ThumbnailFault.SourceOutOfReach, "failed")]
    public async Task TheListSaysWhereThePictureOfEachRecordingStands(
        ThumbnailState state,
        ThumbnailFault? fault,
        string said)
    {
        await using var feature = new RecordingFeature();
        Recording recording = feature.Held();
        recording.Illustrate(state, fault);

        (_, JsonElement body) = await feature.GetAsync("/api/recordings");
        JsonElement item = body.GetProperty("data").GetProperty("items")[0];

        Assert.Equal(said, item.GetProperty("thumbnail").GetProperty("state").GetString());
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

    private static JsonElement Only(JsonElement body)
    {
        JsonElement items = body.GetProperty("data").GetProperty("items");

        Assert.Equal(1, items.GetArrayLength());

        return items[0];
    }

    [Fact]
    public async Task ARecordingNobodyTriedToThrowAwayCarriesNoMark()
    {
        await using var feature = new RecordingFeature();
        EndedAfterTwentyMinutes(feature);

        (_, JsonElement shelf) = await feature.GetAsync("/api/recordings");
        JsonElement row = Assert.Single(shelf.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal(JsonValueKind.Null, row.GetProperty("unfinishedDeletion").ValueKind);
    }

    private static Recording EndedAfterTwentyMinutes(RecordingFeature feature)
    {
        Recording held = feature.Held();

        held.Abort(RecordingFeature.Noon.AddMinutes(20));
        held.Settle(RecordingOutcome.Complete, 1_000_000, RecordingFeature.Noon.AddMinutes(20));

        return held;
    }

    [Fact]
    public async Task TheGenresTheProgrammeWasFiledUnderComeBackInTheOrderTheyWereKept()
    {
        await using var feature = new RecordingFeature();
        feature.Held(genres: [new ProgrammeGenre(7, 1), new ProgrammeGenre(11, 5)]);

        JsonElement genres = (await OnlyProgrammeAsync(feature)).GetProperty("genres");

        Assert.Equal(2, genres.GetArrayLength());
        Assert.Equal(7, genres[0].GetProperty("kind").GetInt32());
        Assert.Equal(1, genres[0].GetProperty("sort").GetInt32());
        Assert.Equal(11, genres[1].GetProperty("kind").GetInt32());
        Assert.Equal(5, genres[1].GetProperty("sort").GetInt32());
    }

    [Fact]
    public async Task AProgrammeFiledUnderNoGenreStillSaysWhatItIsAndCarriesAnEmptyListRatherThanNothing()
    {
        await using var feature = new RecordingFeature();
        feature.Held(genres: [], extended: Cast);

        JsonElement programme = await OnlyProgrammeAsync(feature);
        JsonElement genres = programme.GetProperty("genres");

        Assert.Equal(JsonValueKind.Array, genres.ValueKind);
        Assert.Equal(0, genres.GetArrayLength());
        Assert.Equal("A programme", programme.GetProperty("name").GetString());
        Assert.Equal(Cast, programme.GetProperty("extended").GetString());
    }

    [Fact]
    public async Task TheExtendedTextComesBackWholeAndIsNotTheSummaryOverAgain()
    {
        await using var feature = new RecordingFeature();
        feature.Held(summary: "What it is about", extended: Cast);

        JsonElement programme = await OnlyProgrammeAsync(feature);

        Assert.Equal(Cast, programme.GetProperty("extended").GetString());
        Assert.Equal("What it is about", programme.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task ARecordingWhoseProgrammeWasNeverReadCarriesEmptyTextAndNoGenresRatherThanNulls()
    {
        await using var feature = new RecordingFeature();
        feature.Held(name: string.Empty, summary: string.Empty, extended: string.Empty, genres: []);

        JsonElement programme = await OnlyProgrammeAsync(feature);

        Assert.Equal(string.Empty, programme.GetProperty("name").GetString());
        Assert.Equal(string.Empty, programme.GetProperty("summary").GetString());
        Assert.Equal(string.Empty, programme.GetProperty("extended").GetString());
        Assert.Equal(JsonValueKind.Array, programme.GetProperty("genres").ValueKind);
        Assert.Equal(0, programme.GetProperty("genres").GetArrayLength());
    }

    [Fact]
    public async Task ARecordingThatBelongsToNoBroadcastNamesNoKeyAndStandsAlone()
    {
        await using var feature = new RecordingFeature();
        feature.Held(groupKey: null, groupRole: BroadcastGroupRole.Standalone);

        JsonElement group = (await OnlyAsync(feature)).GetProperty("broadcastGroup");

        Assert.Equal(JsonValueKind.Null, group.GetProperty("key").ValueKind);
        Assert.Equal("standalone", group.GetProperty("role").GetString());
    }

    [Theory]
    [InlineData(BroadcastGroupRole.Standalone, "standalone")]
    [InlineData(BroadcastGroupRole.MovementPrimary, "movementPrimary")]
    [InlineData(BroadcastGroupRole.MovementSuppressed, "movementSuppressed")]
    [InlineData(BroadcastGroupRole.RelaySegment, "relaySegment")]
    public async Task TheKeyAndTheRoleAreBothAnsweredForEveryRoleARecordingCanHold(
        BroadcastGroupRole role,
        string spelling)
    {
        await using var feature = new RecordingFeature();
        feature.Held(groupKey: "an-evening-in-three-parts", groupRole: role);

        JsonElement group = (await OnlyAsync(feature)).GetProperty("broadcastGroup");

        Assert.Equal("an-evening-in-three-parts", group.GetProperty("key").GetString());
        Assert.Equal(spelling, group.GetProperty("role").GetString());
    }

    [Fact]
    public async Task TheSegmentsOfOneBroadcastCarryTheSameKeyAndTheRecordingBesideThemCarriesNone()
    {
        await using var feature = new RecordingFeature();
        feature.Held(eventId: 1, groupKey: "an-evening-in-three-parts", groupRole: BroadcastGroupRole.RelaySegment);
        feature.Held(eventId: 2, groupKey: "an-evening-in-three-parts", groupRole: BroadcastGroupRole.RelaySegment);
        feature.Held(eventId: 3);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");
        Dictionary<int, (string? Key, string? Role)> grouped = body
            .GetProperty("data")
            .GetProperty("items")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("programme").GetProperty("eventId").GetInt32(),
                item => (
                    item.GetProperty("broadcastGroup").GetProperty("key").GetString(),
                    item.GetProperty("broadcastGroup").GetProperty("role").GetString()));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, grouped.Count);
        Assert.Equal(("an-evening-in-three-parts", "relaySegment"), grouped[1]);
        Assert.Equal(("an-evening-in-three-parts", "relaySegment"), grouped[2]);
        Assert.Equal((null, "standalone"), grouped[3]);
    }

    [Fact]
    public async Task TheGenreAClientIsGeneratedForIsTheOneTheGuideAlreadyHands()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonObject schemas = document["components"]!["schemas"]!.AsObject();

        string guide = schemas["ProgrammeResponder"]!["properties"]!["genres"]!["items"]!["$ref"]!.GetValue<string>();
        JsonNode recorded = schemas["RecordingProgrammeResponder"]!["properties"]!["genres"]!;

        Assert.Equal("array", recorded["type"]!.GetValue<string>());
        Assert.Equal(guide, recorded["items"]!["$ref"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheExtendedTextIsDescribedAsTextThatIsAlwaysThere()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonNode extended = document["components"]!["schemas"]!["RecordingProgrammeResponder"]!
            ["properties"]!["extended"]!;

        Assert.Equal(["string"], Types(extended));
    }

    [Fact]
    public async Task TheBroadcastKeyIsDescribedAsAbsentableAndTheRoleAsTheFourItCanHold()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonObject schemas = document["components"]!["schemas"]!.AsObject();
        JsonObject group = schemas["RecordingBroadcastGroupResponder"]!["properties"]!.AsObject();

        Assert.Equal(["null", "string"], Types(group["key"]!).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["movementPrimary", "movementSuppressed", "relaySegment", "standalone"],
            schemas["BroadcastGroupRole"]!["enum"]!
                .AsArray()
                .Select(value => value!.GetValue<string>())
                .Order(StringComparer.Ordinal));
        Assert.Contains(
            "BroadcastGroupRole",
            group["role"]!["$ref"]?.GetValue<string>() ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSoundTheBroadcastAnnouncedComesBackBesideTheNameItWasRecordedUnder()
    {
        await using var feature = new RecordingFeature();
        feature.Held(audio: AudioMode.DualMono, sounds: 2);

        JsonElement programme = await OnlyProgrammeAsync(feature);

        Assert.Equal("dualMono", programme.GetProperty("audio").GetString());
        Assert.Equal(2, programme.GetProperty("sounds").GetInt32());
    }

    [Fact]
    public async Task ARecordingOfABroadcastThatAnnouncedNoSoundSaysUndeterminedRatherThanNothing()
    {
        await using var feature = new RecordingFeature();
        feature.Held();

        JsonElement programme = await OnlyProgrammeAsync(feature);

        Assert.Equal("undetermined", programme.GetProperty("audio").GetString());
        Assert.Equal(0, programme.GetProperty("sounds").GetInt32());
    }

    [Fact]
    public async Task TheSoundAClientIsGeneratedForIsTheOneTheGuideAlreadyHands()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonObject schemas = document["components"]!["schemas"]!.AsObject();

        string guide = schemas["ProgrammeResponder"]!["properties"]!["audio"]!["$ref"]!.GetValue<string>();
        JsonNode recorded = schemas["RecordingProgrammeResponder"]!["properties"]!["audio"]!;

        Assert.Equal(guide, recorded["$ref"]!.GetValue<string>());
        Assert.Equal(
            ["dualMono", "mono", "stereo", "surround", "undetermined"],
            schemas["AudioMode"]!["enum"]!
                .AsArray()
                .Select(value => value!.GetValue<string>())
                .Order(StringComparer.Ordinal));
    }

    private static string[] Types(JsonNode schema)
        => schema["type"] switch
        {
            JsonArray many => [.. many.Select(value => value!.GetValue<string>())],
            JsonValue one => [one.GetValue<string>()],
            _ => [],
        };

    private static async Task<JsonElement> OnlyAsync(RecordingFeature feature)
    {
        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement items = body.GetProperty("data").GetProperty("items");

        Assert.Equal(1, items.GetArrayLength());

        return items[0];
    }

    private static async Task<JsonElement> OnlyProgrammeAsync(RecordingFeature feature)
        => (await OnlyAsync(feature)).GetProperty("programme");

    [Fact]
    public async Task ARecordingLeftEncryptedIsNotOfferedAsAGoodOneEvenThoughNothingWasLost()
    {
        await using var feature = new RecordingFeature();
        Measured(feature, dropped: 0, total: 5_000_000, scrambled: 4_800_000);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");
        JsonElement drops = body.GetProperty("data").GetProperty("items")[0].GetProperty("drops");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("mayNotBeWatchable", drops.GetProperty("quality").GetString());
        Assert.Equal(0, drops.GetProperty("ccDroppedPackets").GetInt64());
        Assert.Equal(4_800_000, drops.GetProperty("scrambledPackets").GetInt64());
    }

    [Fact]
    public async Task ARecordingTheCardUnlockedAndNothingWasLostFromIsGood()
    {
        await using var feature = new RecordingFeature();
        Measured(feature, dropped: 0, total: 7_000_000, scrambled: 0);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");
        JsonElement drops = body.GetProperty("data").GetProperty("items")[0].GetProperty("drops");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("good", drops.GetProperty("quality").GetString());
        Assert.Equal("good", drops.GetProperty("scrambleQuality").GetString());
        Assert.Equal(0, drops.GetProperty("scrambledPackets").GetInt64());
    }

    [Fact]
    public async Task ARecordingLeftScrambledSaysScramblingIsWhatMakesItUnwatchable()
    {
        await using var feature = new RecordingFeature();
        Measured(feature, dropped: 0, total: 1_000_000, scrambled: 20_000);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");
        JsonElement drops = body.GetProperty("data").GetProperty("items")[0].GetProperty("drops");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("mayNotBeWatchable", drops.GetProperty("quality").GetString());
        Assert.Equal("mayNotBeWatchable", drops.GetProperty("scrambleQuality").GetString());
    }

    [Fact]
    public async Task ARecordingThatOnlyLostPacketsSaysItsScramblingWasGood()
    {
        await using var feature = new RecordingFeature();
        Measured(feature, dropped: 50_000, total: 1_000_000, scrambled: 0);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");
        JsonElement drops = body.GetProperty("data").GetProperty("items")[0].GetProperty("drops");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("mayNotBeWatchable", drops.GetProperty("quality").GetString());
        Assert.Equal("good", drops.GetProperty("scrambleQuality").GetString());
    }

    [Fact]
    public async Task ARecordingNothingCountedSaysSoRatherThanReadingAsGood()
    {
        await using var feature = new RecordingFeature();
        feature.Held();

        (_, JsonElement body) = await feature.GetAsync("/api/recordings");
        JsonElement drops = body.GetProperty("data").GetProperty("items")[0].GetProperty("drops");

        Assert.Equal("unmeasured", drops.GetProperty("quality").GetString());
        Assert.Equal("unmeasured", drops.GetProperty("scrambleQuality").GetString());
        Assert.False(drops.GetProperty("ccMeasured").GetBoolean());
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

    [Theory(DisplayName = "every page of a carried shelf reads unmeasured in either order, with no count standing in for one")]
    [InlineData("startedAt", false)]
    [InlineData("startedAt", true)]
    [InlineData("programmeStartsAt", false)]
    [InlineData("programmeStartsAt", true)]
    public async Task EveryPageOfACarriedShelfReadsUnmeasuredInEitherOrder(string sort, bool descending)
    {
        await using var feature = new RecordingFeature();
        Carry(feature);

        List<JsonElement> seen = [];

        foreach (int page in Enumerable.Range(1, 3))
        {
            (HttpStatusCode status, JsonElement body) = await feature.GetAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"/api/recordings?sort={sort}&descending={(descending ? "true" : "false")}&perPage=20&page={page}"));

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(ShelfSize, body.GetProperty("data").GetProperty("total").GetInt32());
            seen.AddRange(body.GetProperty("data").GetProperty("items").EnumerateArray());
        }

        Assert.Equal(ShelfSize, seen.Count);
        Assert.Equal(ShelfSize, seen.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.All(seen, item => SaysNothingWasCounted(item.GetProperty("drops")));
    }

    [Fact(DisplayName = "the drop reading finds the whole carried shelf unmeasured and none of it clean or dropped")]
    public async Task TheDropReadingFindsTheWholeCarriedShelfUnmeasured()
    {
        await using var feature = new RecordingFeature();
        Carry(feature);

        Assert.Equal(ShelfSize, await TotalAsync(feature, "/api/recordings?drops=unmeasured&perPage=200"));
        Assert.Equal(0, await TotalAsync(feature, "/api/recordings?drops=clean&perPage=200"));
        Assert.Equal(0, await TotalAsync(feature, "/api/recordings?drops=dropped&perPage=200"));
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

    private static async Task<int> TotalAsync(RecordingFeature feature, string path)
    {
        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, status);

        return body.GetProperty("data").GetProperty("total").GetInt32();
    }

    [Fact]
    public async Task ARefusedChoiceNamesTheValuesItTakes()
    {
        await using var feature = new RecordingFeature();

        (_, JsonElement answer) = await feature.GetAsync("/api/recordings?sort=alphabetically");
        string message = answer.GetProperty("message").GetString()!;

        Assert.Contains("sort", message, StringComparison.Ordinal);
        Assert.Contains("startedAt", message, StringComparison.Ordinal);
        Assert.Contains("programmeStartsAt", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedListOfChoicesNamesTheValuesEachOneTakes()
    {
        await using var feature = new RecordingFeature();

        (_, JsonElement answer) = await feature.GetAsync("/api/recordings?outcome=complete&outcome=nearly");
        string message = answer.GetProperty("message").GetString()!;

        Assert.Contains("outcome", message, StringComparison.Ordinal);
        Assert.Contains("truncated", message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "D-11: following next reaches every recording once, newest first")]
    public async Task FollowingNextReachesEveryRecordingOnceNewestFirst()
    {
        await using var feature = new RecordingFeature();
        List<Recording> held =
        [
            .. Enumerable.Range(1, 5).Select(at => feature.Held(eventId: at, startedAt: RecordingFeature.Noon.AddMinutes(at % 3))),
        ];

        List<string> walked = [];
        string path = Newest;

        while (true)
        {
            (HttpStatusCode status, JsonElement body) = await feature.GetAsync(path);
            JsonElement data = body.GetProperty("data");

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(5, data.GetProperty("total").GetInt32());
            walked.AddRange(data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!));

            if (data.GetProperty("next").GetString() is not { } next)
            {
                break;
            }

            path = $"{Newest}&after={next}";
        }

        Assert.Equal(5, walked.Count);
        Assert.Equal(held.Select(recording => recording.Id.Wire).Order(StringComparer.Ordinal), walked.Order(StringComparer.Ordinal));
        Assert.Equal(
            held.OrderByDescending(recording => recording.StartedAtActual).Select(recording => recording.StartedAtActual),
            walked.Select(id => held.Single(recording => recording.Id.Wire == id).StartedAtActual));
    }

    [Fact(DisplayName = "D-11: the last page names no next")]
    public async Task TheLastPageNamesNoNext()
    {
        await using var feature = new RecordingFeature();
        feature.Held(eventId: 1);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(Newest);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").GetProperty("next").ValueKind);
    }

    [Fact(DisplayName = "D-11: a page number and a position asked for at once are refused")]
    public async Task APageNumberAndAPositionAskedForAtOnceAreRefused()
    {
        await using var feature = new RecordingFeature();
        feature.Held(eventId: 1);
        feature.Held(eventId: 2, startedAt: RecordingFeature.Noon.AddMinutes(1));
        feature.Held(eventId: 3, startedAt: RecordingFeature.Noon.AddMinutes(2));
        string next = (await feature.GetAsync(Newest)).Body.GetProperty("data").GetProperty("next").GetString()!;

        (HttpStatusCode status, _) = await feature.GetAsync($"{Newest}&after={next}&page=2");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Theory(DisplayName = "D-11: a position that is not one this endpoint wrote, or was written for another order, is refused")]
    [InlineData("&after=not-a-position")]
    [InlineData("&after=written-for-oldest-first")]
    public async Task APositionThatIsNotOneThisEndpointWroteIsRefused(string asked)
    {
        await using var feature = new RecordingFeature();
        feature.Held(eventId: 1);
        feature.Held(eventId: 2, startedAt: RecordingFeature.Noon.AddMinutes(1));
        feature.Held(eventId: 3, startedAt: RecordingFeature.Noon.AddMinutes(2));
        string oldestFirst = (await feature.GetAsync("/api/recordings?sort=startedAt&descending=false&perPage=2"))
            .Body.GetProperty("data").GetProperty("next").GetString()!;

        (HttpStatusCode status, _) = await feature.GetAsync(
            Newest + asked.Replace("written-for-oldest-first", oldestFirst, StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task AKeywordLeavesOnlyTheRecordingsWhoseTitleCarriesIt()
    {
        await using var feature = new RecordingFeature();
        Recording wanted = feature.Held(eventId: 1, name: "Zephyr diary");
        feature.Held(eventId: 2, name: "Harbour lights");

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings?keyword=zephyr");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(wanted.Id.Wire, Only(body).GetProperty("id").GetString());
        Assert.Equal(1, body.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task TheDetailIsSearchedBesideTheTitleAndTheSummary()
    {
        await using var feature = new RecordingFeature();
        Recording wanted = feature.Held(eventId: 1, name: "Zephyr diary", extended: "◇出演者 みかん博士");
        feature.Held(eventId: 2, name: "Harbour lights");

        (_, JsonElement body) = await feature.GetAsync("/api/recordings?keyword=" + Uri.EscapeDataString("みかん博士"));

        Assert.Equal(wanted.Id.Wire, Only(body).GetProperty("id").GetString());
    }

    [Fact]
    public async Task EveryWordAskedForHasToBeSomewhereInTheRecordingForItToComeBack()
    {
        await using var feature = new RecordingFeature();
        Recording wanted = feature.Held(eventId: 1, name: "Zephyr diary", summary: "A walk along the water");
        feature.Held(eventId: 2, name: "Zephyr diary", summary: "A walk up the hill");

        (_, JsonElement body) = await feature.GetAsync("/api/recordings?keyword=zephyr+water");

        Assert.Equal(wanted.Id.Wire, Only(body).GetProperty("id").GetString());
    }

    [Fact]
    public async Task AKeywordOfASingleLetterIsAnswered()
    {
        await using var feature = new RecordingFeature();
        Recording wanted = feature.Held(eventId: 1, name: "Zephyr diary");
        feature.Held(eventId: 2, name: "Harbour lights");

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings?keyword=z");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(wanted.Id.Wire, Only(body).GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("keyword=")]
    [InlineData("keyword=%20")]
    [InlineData("keyword=%E3%80%80")]
    public async Task AListThatNamesNoKeywordWorthTheNameAnswersWordForWordWhatOneThatNamesNoneAtAllAnswers(
        string query)
    {
        await using var feature = new RecordingFeature();
        feature.Held(eventId: 1, name: "Zephyr diary");
        feature.Held(eventId: 2, name: "Harbour lights");

        Assert.Equal(
            await feature.GetTextAsync("/api/recordings"),
            await feature.GetTextAsync("/api/recordings?" + query));
    }

    [Fact]
    public async Task TheAnswerToAListThatNamesNoKeywordCarriesTheFieldsItCarriedBeforeOneCouldBeAsked()
    {
        await using var feature = new RecordingFeature();
        feature.Held();

        (_, JsonElement body) = await feature.GetAsync("/api/recordings");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(["data", "message", "status"], Named(body));
        Assert.Equal(["currentPage", "items", "lastPage", "next", "perPage", "total"], Named(data));
    }

    [Theory]
    [InlineData(RecordingKeyword.LongestKeyword + 1)]
    [InlineData(RecordingKeyword.LongestKeyword + 200)]
    public async Task AKeywordLongerThanTheCeilingIsRefused(int letters)
    {
        await using var feature = new RecordingFeature();
        feature.Held();

        (HttpStatusCode status, _) = await feature.GetAsync(
            "/api/recordings?keyword=" + new string('a', letters));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task MoreWordsThanTheCeilingAllowsAreRefused()
    {
        await using var feature = new RecordingFeature();
        feature.Held();

        string words = string.Join('+', Enumerable.Range(1, RecordingKeyword.MostWords + 1).Select(word => $"w{word}"));

        (HttpStatusCode status, _) = await feature.GetAsync("/api/recordings?keyword=" + words);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task AsManyWordsAsTheCeilingAllowsAreAnswered()
    {
        await using var feature = new RecordingFeature();
        feature.Held();

        string words = string.Join('+', Enumerable.Range(1, RecordingKeyword.MostWords).Select(word => $"w{word}"));

        (HttpStatusCode status, _) = await feature.GetAsync("/api/recordings?keyword=" + words);

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task TheRefusalDoesNotReadBackWhatWasAskedFor()
    {
        await using var feature = new RecordingFeature();
        feature.Held();

        string asked = new('z', RecordingKeyword.LongestKeyword + 1);
        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings?keyword=" + asked);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.DoesNotContain(asked, body.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private static string[] Named(JsonElement element)
        => [.. element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];

    [Fact(DisplayName = "every row of the library says where its recording stands with the encoder")]
    public async Task EveryRowOfTheLibrarySaysWhereItsRecordingStandsWithTheEncoder()
    {
        await using var feature = new RecordingFeature();
        Recording encoded = EndedWithEncodeChoice(feature, eventId: 1);
        Recording waiting = EndedWithEncodeChoice(feature, eventId: 2);
        Recording untouched = EndedWithEncodeChoice(feature, eventId: 3);
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

    [Fact(DisplayName = "a job that failed shows on the row until something is queued in its place")]
    public async Task AJobThatFailedShowsOnTheRowUntilSomethingIsQueuedInItsPlace()
    {
        await using var feature = new RecordingFeature();
        Recording recording = EndedWithEncodeChoice(feature, eventId: 5);
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

    [Fact(DisplayName = "every row of the library says whether its recording asks for an encode, so one the automatic run passed over reads apart from one it has not reached")]
    public async Task EveryRowOfTheLibrarySaysWhetherItsRecordingAsksForAnEncode()
    {
        await using var feature = new RecordingFeature();
        Recording asking = EndedWithEncodeChoice(feature, eventId: 6);
        Recording passedOver = EndedWithEncodeChoice(feature, eventId: 7, encodeWhenRecorded: false);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");
        Dictionary<string, JsonElement> rows = body
            .GetProperty("data")
            .GetProperty("items")
            .EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("id").GetString()!,
                row => row.GetProperty("encode"),
                StringComparer.Ordinal);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(rows[asking.Id.Wire].GetProperty("whenRecorded").GetBoolean());
        Assert.False(rows[passedOver.Id.Wire].GetProperty("whenRecorded").GetBoolean());
        Assert.Equal("notEncoded", rows[asking.Id.Wire].GetProperty("standing").GetString());
        Assert.Equal("notEncoded", rows[passedOver.Id.Wire].GetProperty("standing").GetString());
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

    [Fact(DisplayName = "every row of the library says how far this viewer watched its recording")]
    public async Task EveryRowOfTheLibrarySaysHowFarThisViewerWatchedItsRecording()
    {
        await using var feature = new RecordingFeature();
        Recording watched = EndedAfterAnHour(feature, eventId: 1);
        Recording unwatched = EndedAfterAnHour(feature, eventId: 2);
        Recording sharedWithSomeoneElse = EndedAfterAnHour(feature, eventId: 3);

        feature.Positions.Positions.Add(Reached(watched, TestAuthenticationHandler.Tester, 612.5));
        feature.Positions.Positions.Add(Reached(sharedWithSomeoneElse, "someone else", 900));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/recordings");
        Dictionary<string, JsonElement> rows = body
            .GetProperty("data")
            .GetProperty("items")
            .EnumerateArray()
            .ToDictionary(row => row.GetProperty("id").GetString()!, StringComparer.Ordinal);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(612.5, rows[watched.Id.Wire].GetProperty("watchedPositionSec").GetDouble());
        Assert.Equal(JsonValueKind.Null, rows[unwatched.Id.Wire].GetProperty("watchedPositionSec").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows[sharedWithSomeoneElse.Id.Wire].GetProperty("watchedPositionSec").ValueKind);
    }

    [Fact(DisplayName = "the places of a whole page are read in one question, however many rows it has")]
    public async Task ThePlacesOfAWholePageAreReadInOneQuestion()
    {
        await using var feature = new RecordingFeature();

        foreach (int eventId in Enumerable.Range(10, 5))
        {
            Recording recording = EndedAfterAnHour(feature, eventId);
            feature.Positions.Positions.Add(Reached(recording, TestAuthenticationHandler.Tester, eventId));
        }

        (HttpStatusCode status, _) = await feature.GetAsync("/api/recordings");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, feature.Positions.ReadsOfMany);
        Assert.Equal(0, feature.Positions.ReadsOfOne);
    }

    [Fact(DisplayName = "a place a player has just sent is the place the library shows")]
    public async Task APlaceAPlayerHasJustSentIsThePlaceTheLibraryShows()
    {
        await using var feature = new RecordingFeature();
        Recording recording = EndedAfterAnHour(feature, eventId: 20);

        using HttpResponseMessage kept = await feature.Client.PutAsJsonAsync(
            new Uri($"/api/videos/{recording.Id.Wire}/position", UriKind.Relative),
            new { positionSec = 61.5 });

        (_, JsonElement body) = await feature.GetAsync("/api/recordings");

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal(
            61.5,
            Assert.Single(body.GetProperty("data").GetProperty("items").EnumerateArray())
                .GetProperty("watchedPositionSec")
                .GetDouble());
    }

    [Fact(DisplayName = "an empty library asks for no places at all")]
    public async Task AnEmptyLibraryAsksForNoPlacesAtAll()
    {
        await using var feature = new RecordingFeature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/recordings");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, feature.Positions.ReadsOfMany);
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
}
