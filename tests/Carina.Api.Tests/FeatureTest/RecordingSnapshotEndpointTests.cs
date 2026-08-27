using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class RecordingSnapshotEndpointTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const string Cast = "Cast\nA. Mirek, B. Sandoval\nProduction\nThe second half is a repeat.";

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
}
