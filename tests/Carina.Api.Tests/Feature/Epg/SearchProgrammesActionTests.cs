using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Programmes;
using Carina.TestSupport;

namespace Carina.Api.Tests.Feature.Epg;

[Trait("Category", "Feature")]
public sealed class SearchProgrammesActionTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AKeywordBringsBackWhatCarriesIt()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(Programme(1, "ニュース7"));
        feature.Programmes.Programmes.Add(Programme(2, "天気予報"));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/programs/search?keyword=ニュース");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("ニュース7", data.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ASpanThatReachesBackBringsTheArchiveWithItMarkedAsACopy()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(Programme(1, "ニュース7"));
        feature.Archived.Programmes.Add(Archived(2, "ニュース特集"));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            "/api/programs/search?keyword=ニュース&from=2026-08-14T00:00:00Z&to=2026-08-19T00:00:00Z");
        JsonElement data = body.GetProperty("data");
        var copied = data.GetProperty("items").EnumerateArray().ToDictionary(
            item => item.GetProperty("name").GetString()!,
            item => item.GetProperty("isArchived").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, data.GetProperty("total").GetInt32());
        Assert.True(copied["ニュース特集"]);
        Assert.False(copied["ニュース7"]);
    }

    [Fact]
    public async Task ASearchThatNamesNoSpanLeavesTheArchiveOut()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(Programme(1, "ニュース7"));
        feature.Archived.Programmes.Add(Kept(2, "ニュース特集", At.AddDays(2)));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/programs/search?keyword=ニュース");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("ニュース7", data.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ASearchThatNamesNoSpanLeavesOutWhatHasFinishedBroadcasting()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(Ran(1, "ニュース昨日", At.AddDays(-1)));
        feature.Programmes.Programmes.Add(Ran(2, "ニュース明日", At.AddDays(1)));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/programs/search?keyword=ニュース");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("ニュース明日", data.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ASpanThatReachesBackBringsBackWhatHasFinishedBroadcasting()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(Ran(1, "ニュース昨日", At.AddDays(-1)));
        feature.Programmes.Programmes.Add(Ran(2, "ニュース明日", At.AddDays(1)));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            "/api/programs/search?keyword=ニュース&from=2026-08-16T00:00:00Z&to=2026-08-20T00:00:00Z");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, body.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ASearchThatNamesNoSpanKeepsWhatIsOnTheAirRightNow()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(Ran(1, "ニュース進行中", At.AddMinutes(-10)));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/programs/search?keyword=ニュース");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AKeywordOfOneLetterIsRefusedBeforeItReachesTheStore()
    {
        await using EpgFeature feature = Feature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/search?keyword=あ");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task ASearchNobodyNarrowedIsRefused()
    {
        await using EpgFeature feature = Feature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/search");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task SortingAndPagingOnTheirOwnDoNotMakeASearch()
    {
        await using EpgFeature feature = Feature();

        (HttpStatusCode status, _) = await feature.GetAsync(
            "/api/programs/search?sort=name&descending=true&page=2&perPage=100");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task NamingWhereToLookWithoutAWordToLookForIsRefused()
    {
        await using EpgFeature feature = Feature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/search?fields=title");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task AGenreOnItsOwnBringsBackWhatIsFiledUnderIt()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(Filed(1, "紀行その一", 8));
        feature.Programmes.Programmes.Add(Filed(2, "紀行その二", 6));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/programs/search?genre=8");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("紀行その一", data.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AChannelOnItsOwnBringsBackWhatThatServiceBroadcast()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(On(1049, 1, "紀行その一"));
        feature.Programmes.Programmes.Add(On(1032, 2, "紀行その二"));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/programs/search?channel=4-1049");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal(1049, data.GetProperty("items")[0].GetProperty("serviceId").GetInt32());
    }

    [Fact]
    public async Task ABroadcastTypeOnItsOwnBringsBackWhatTheServicesItCarriesBroadcast()
    {
        await using EpgFeature feature = Feature([Terrestrial(4, 1049), Satellite(4, 1032)]);

        feature.Programmes.Programmes.Add(On(1049, 1, "紀行その一"));
        feature.Programmes.Programmes.Add(On(1032, 2, "紀行その二"));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/programs/search?type=isdbT");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("紀行その一", data.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AnExcludedWordOnItsOwnLeavesOutWhatCarriesIt()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(Programme(1, "絶景紀行"));
        feature.Programmes.Programmes.Add(Programme(2, "絶景紀行 再放送"));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/programs/search?exclude=再放送");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("絶景紀行", data.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ASpanOnItsOwnBringsBackWhatFallsInsideIt()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(Programme(1, "紀行その一"));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            "/api/programs/search?from=2026-08-18T00:00:00Z&to=2026-08-19T00:00:00Z");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AConditionBesideTheKeywordDoesNotBuyAKeywordOfOneLetterItsWayIn()
    {
        await using EpgFeature feature = Feature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/search?keyword=あ&genre=8");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task APageSizeBeyondTheCeilingComesBackAtTheCeiling()
    {
        await using EpgFeature feature = Feature();

        (_, JsonElement body) = await feature.GetAsync("/api/programs/search?keyword=news&perPage=100000");

        Assert.Equal(
            ProgrammeSearch.MostPerPage,
            body.GetProperty("data").GetProperty("perPage").GetInt32());
    }

    [Fact]
    public async Task ASpanLongerThanTheCeilingIsRefused()
    {
        await using EpgFeature feature = Feature();

        (HttpStatusCode status, _) = await feature.GetAsync(
            "/api/programs/search?keyword=news&from=2026-01-01T00:00:00Z&to=2026-06-01T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task ASortNobodyDefinedIsRefusedRatherThanPassedToTheStore()
    {
        await using EpgFeature feature = Feature();

        (HttpStatusCode status, _) = await feature.GetAsync(
            "/api/programs/search?keyword=news&sort=name;DROP TABLE programme");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task ASortSpelledAsANumberOutsideTheListIsRefusedToo()
    {
        await using EpgFeature feature = Feature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/search?keyword=news&sort=7");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task EveryWordOfTheKeywordHasToAppear()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(Programme(1, "夏の絶景"));
        feature.Programmes.Programmes.Add(Programme(2, "夏の思い出"));

        (_, JsonElement body) = await feature.GetAsync("/api/programs/search?keyword=夏+絶景");

        Assert.Equal(1, body.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AnExcludedWordDropsWhatCarriesIt()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(Programme(1, "絶景紀行"));
        feature.Programmes.Programmes.Add(Programme(2, "絶景紀行 再放送"));

        (_, JsonElement body) = await feature.GetAsync("/api/programs/search?keyword=絶景&exclude=再放送");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("絶景紀行", data.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AnExcludedWordOfOneLetterIsRefusedBeforeItReachesTheStore()
    {
        await using EpgFeature feature = Feature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/search?keyword=絶景&exclude=再");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task NamingOnlyTheTitleLeavesTheSummaryOutOfIt()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(Programme(1, "大河ドラマ", "絶景をめぐる"));
        feature.Programmes.Programmes.Add(Programme(2, "絶景紀行"));

        (_, JsonElement body) = await feature.GetAsync("/api/programs/search?keyword=絶景&fields=title");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("絶景紀行", data.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task BothFieldsCanBeNamedAtOnce()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(Programme(1, "大河ドラマ", "絶景をめぐる"));
        feature.Programmes.Programmes.Add(Programme(2, "絶景紀行"));

        (_, JsonElement body) = await feature.GetAsync(
            "/api/programs/search?keyword=絶景&fields=title&fields=description");

        Assert.Equal(2, body.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AFieldNobodyDefinedIsRefusedRatherThanPassedToTheStore()
    {
        await using EpgFeature feature = Feature();

        (HttpStatusCode status, _) = await feature.GetAsync(
            "/api/programs/search?keyword=news&fields=summary;DROP TABLE programme");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task AGenreNarrowsTheAnswerToWhatIsFiledUnderIt()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(Filed(1, "紀行その一", 8));
        feature.Programmes.Programmes.Add(Filed(2, "紀行その二", 6));

        (_, JsonElement body) = await feature.GetAsync("/api/programs/search?keyword=紀行&genre=8");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("紀行その一", data.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AnyOfTheGenresAskedForIsEnough()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(Filed(1, "紀行その一", 8));
        feature.Programmes.Programmes.Add(Filed(2, "紀行その二", 6));
        feature.Programmes.Programmes.Add(Filed(3, "紀行その三", 4));

        (_, JsonElement body) = await feature.GetAsync("/api/programs/search?keyword=紀行&genre=8&genre=6");

        Assert.Equal(2, body.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AGenreOutsideTheFourBitsTheStandardGivesItIsRefused()
    {
        await using EpgFeature feature = Feature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/search?keyword=news&genre=99");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task AGenreThatIsNotANumberIsRefusedRatherThanPassedToTheStore()
    {
        await using EpgFeature feature = Feature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/search?keyword=news&genre=kind");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task ABroadcastTypeNarrowsToTheServicesItCarries()
    {
        await using EpgFeature feature = Feature([Terrestrial(4, 1049), Satellite(4, 1032)]);

        feature.Programmes.Programmes.Add(On(1049, 1, "紀行その一"));
        feature.Programmes.Programmes.Add(On(1032, 2, "紀行その二"));

        (_, JsonElement body) = await feature.GetAsync("/api/programs/search?keyword=紀行&type=isdbT");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("紀行その一", data.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ABroadcastTypeThatCarriesNoServiceFindsNothingRatherThanEverything()
    {
        await using EpgFeature feature = Feature([Terrestrial(4, 1049)]);

        feature.Programmes.Programmes.Add(On(1049, 1, "紀行その一"));

        (_, JsonElement body) = await feature.GetAsync("/api/programs/search?keyword=紀行&type=isdbSCs110");

        Assert.Equal(0, body.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ABroadcastTypeNobodyDefinedIsRefused()
    {
        await using EpgFeature feature = Feature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/search?keyword=news&type=vhf");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task AChannelNarrowsToWhatThatServiceBroadcast()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(On(1049, 1, "紀行その一"));
        feature.Programmes.Programmes.Add(On(1032, 2, "紀行その二"));

        (_, JsonElement body) = await feature.GetAsync("/api/programs/search?keyword=紀行&channel=4-1049");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal(1049, data.GetProperty("items")[0].GetProperty("serviceId").GetInt32());
    }

    [Fact]
    public async Task SeveralChannelsAreAskedForTogether()
    {
        await using EpgFeature feature = Feature();

        feature.Programmes.Programmes.Add(On(1049, 1, "紀行その一"));
        feature.Programmes.Programmes.Add(On(1032, 2, "紀行その二"));
        feature.Programmes.Programmes.Add(On(1040, 3, "紀行その三"));

        (_, JsonElement body) = await feature.GetAsync(
            "/api/programs/search?keyword=紀行&channel=4-1049&channel=4-1040");

        Assert.Equal(2, body.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AChannelThatIsNotTwoNumbersIsRefusedRatherThanLookedUp()
    {
        await using EpgFeature feature = Feature();

        (HttpStatusCode status, _) = await feature.GetAsync(
            "/api/programs/search?keyword=news&channel=not-a-channel");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task AChannelOutsideTheRangeAnIdentifierHasIsRefused()
    {
        await using EpgFeature feature = Feature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/search?keyword=news&channel=4-99999");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task MoreChannelsThanTheCeilingIsRefused()
    {
        await using EpgFeature feature = Feature();
        string asking = string.Join(
            '&',
            Enumerable.Range(0, ProgrammeSearch.MostChannels + 1).Select(carried => $"channel=4-{carried}"));

        (HttpStatusCode status, _) = await feature.GetAsync($"/api/programs/search?keyword=news&{asking}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Theory]
    [InlineData("sort=", HttpStatusCode.OK)]
    [InlineData("descending=", HttpStatusCode.OK)]
    [InlineData("type=", HttpStatusCode.OK)]
    [InlineData("page=", HttpStatusCode.OK)]
    [InlineData("perPage=", HttpStatusCode.OK)]
    [InlineData("from=", HttpStatusCode.OK)]
    [InlineData("to=", HttpStatusCode.OK)]
    [InlineData("exclude=", HttpStatusCode.OK)]
    [InlineData("fields=", HttpStatusCode.BadRequest)]
    [InlineData("genre=", HttpStatusCode.BadRequest)]
    [InlineData("channel=", HttpStatusCode.BadRequest)]
    public async Task AWordGivenWithNoValueAtAllIsReadAsAWordNobodyGave(string word, HttpStatusCode expected)
    {
        await using var feature = new EpgFeature(null, clock: new WoundClock(At));

        (HttpStatusCode status, _) = await feature.GetAsync($"/api/programs/search?keyword=news&{word}");

        Assert.Equal(expected, status);
    }

    [Fact]
    public async Task AWordGivenTwiceWhereOnlyOneIsAllowedIsReadFromTheFirstOfThem()
    {
        await using var feature = new EpgFeature(null, clock: new WoundClock(At));

        feature.Programmes.Programmes.Add(Programme(1, "first news"));
        feature.Programmes.Programmes.Add(Programme(2, "other news"));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            "/api/programs/search?keyword=first&keyword=other");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal(
            "first news",
            body.GetProperty("data").GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task TheDocumentNamesEveryWordOfTheSearchVocabularyAndNothingBeside()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonArray declared = document["paths"]!["/api/programs/search"]!["get"]!["parameters"]!.AsArray();

        Assert.NotEmpty(ProgrammeSearchQuery.Vocabulary);
        Assert.Equal(
            [.. ProgrammeSearchQuery.Vocabulary.Select(term => term.Name).Order(StringComparer.Ordinal)],
            [.. declared.Select(parameter => parameter!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal)]);
        Assert.All(declared, parameter => Assert.Equal("query", parameter!["in"]!.GetValue<string>()));
    }

    [Fact]
    public async Task ASearchFindsTheReplacementAndNotTheScheduleItReplaced()
    {
        await using EpgFeature feature = ReplacedGuide.Replaced();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/programs/search?keyword=番組&{ReplacedGuide.TheDay}");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        Assert.Equal(
            ["前の番組", "差し替えの番組", "次の番組"],
            data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString() ?? string.Empty));
    }

    [Fact]
    public async Task AHistorySearchFindsTheKeptReplacementAndNotTheScheduleItReplaced()
    {
        await using EpgFeature feature = ReplacedGuide.Replaced();
        feature.Archived.Programmes.AddRange(feature.Programmes.Programmes.Select(ReplacedGuide.Kept));
        feature.Programmes.Programmes.Clear();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/programs/search?keyword=番組&{ReplacedGuide.TheDay}");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        Assert.Equal(
            ["前の番組", "差し替えの番組", "次の番組"],
            data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString() ?? string.Empty));
    }

    [Fact]
    public async Task ASearchWithCombinedConditionsFindsExactlyWhatMatches()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(CollectedGuide.Channel, ChannelScript.Carrying(CollectedGuide.Guide().ToBytes()));

        await using var feature = new EpgFeature([CollectedGuide.OnAir()], driver, clock: new WoundClock(CollectedGuide.Airs));

        await CollectedGuide.CollectAsync(feature);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            "/api/programs/search?keyword=Bulletin&from=2026-08-31T00:00:00Z&to=2026-09-02T00:00:00Z&perPage=10");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal($"{CollectedGuide.Network}-{CollectedGuide.Television}-1", data.GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task APortableSimulcastIsNotAmongTheMatchesOfASearchNarrowedToItsType()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(CollectedGuide.Channel, ChannelScript.Carrying(CollectedGuide.GuideCarryingAPortableSimulcast().ToBytes()));

        await using var feature = new EpgFeature([CollectedGuide.EverythingTheStreamCarries()], driver, clock: new WoundClock(CollectedGuide.Airs));

        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.Television, ServiceCategory.Television));
        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.SecondTelevision, ServiceCategory.Television));
        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.OneSegSimulcast, ServiceCategory.OneSeg));
        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.DataService, ServiceCategory.Data));

        await CollectedGuide.CollectAsync(feature);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/programs/search?type=isdbT&keyword=Evening%20Bulletin&{CollectedGuide.TheDay}");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [$"{CollectedGuide.Network}-{CollectedGuide.Television}-1"],
            data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task NeitherAPortableSimulcastNorACarouselIsAmongTheMatchesOfASearchThatNamesNoType()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(CollectedGuide.Channel, ChannelScript.Carrying(CollectedGuide.GuideCarryingAPortableSimulcast().ToBytes()));

        await using var feature = new EpgFeature([CollectedGuide.EverythingTheStreamCarries()], driver, clock: new WoundClock(CollectedGuide.Airs));

        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.Television, ServiceCategory.Television));
        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.SecondTelevision, ServiceCategory.Television));
        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.OneSegSimulcast, ServiceCategory.OneSeg));
        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.DataService, ServiceCategory.Data));

        await CollectedGuide.CollectAsync(feature);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/programs/search?keyword=Evening&{CollectedGuide.TheDay}");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [$"{CollectedGuide.Network}-{CollectedGuide.Television}-1"],
            data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task AServiceTheCatalogueHasNoOpinionOnIsStillAmongTheMatches()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(CollectedGuide.Channel, ChannelScript.Carrying(CollectedGuide.GuideCarryingAPortableSimulcast().ToBytes()));

        await using var feature = new EpgFeature([CollectedGuide.EverythingTheStreamCarries()], driver, clock: new WoundClock(CollectedGuide.Airs));

        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.Television, ServiceCategory.Television));
        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.SecondTelevision, ServiceCategory.Television));

        await CollectedGuide.CollectAsync(feature);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/programs/search?keyword=Evening&{CollectedGuide.TheDay}");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [$"{CollectedGuide.Network}-{CollectedGuide.Television}-1", $"{CollectedGuide.Network}-{CollectedGuide.OneSegSimulcast}-3", $"{CollectedGuide.Network}-{CollectedGuide.DataService}-5"],
            data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task AnArchivedProgrammeOnAWithheldServiceIsNotAmongTheMatches()
    {
        await using var feature = new EpgFeature(
            [CollectedGuide.EverythingTheStreamCarries()],
            new ScriptedDriverClient(),
            clock: new WoundClock(CollectedGuide.Airs));

        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.Television, ServiceCategory.Television));
        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.OneSegSimulcast, ServiceCategory.OneSeg));
        feature.Archived.Programmes.Add(CollectedGuide.Archived(CollectedGuide.Television, 21, "Archived Bulletin"));
        feature.Archived.Programmes.Add(CollectedGuide.Archived(CollectedGuide.OneSegSimulcast, 23, "Archived Bulletin"));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/programs/search?keyword=Archived%20Bulletin&{CollectedGuide.TheDay}");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [$"{CollectedGuide.Network}-{CollectedGuide.Television}-21"],
            data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task AnArchivedProgrammeOnAServiceTheCatalogueHasForgottenIsStillAmongTheMatches()
    {
        await using var feature = new EpgFeature(
            [CollectedGuide.EverythingTheStreamCarries()],
            new ScriptedDriverClient(),
            clock: new WoundClock(CollectedGuide.Airs));

        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.Television, ServiceCategory.Television));
        feature.Archived.Programmes.Add(CollectedGuide.Archived(CollectedGuide.Television, 21, "Archived Bulletin"));
        feature.Archived.Programmes.Add(CollectedGuide.Archived(CollectedGuide.WithdrawnService, 25, "Archived Bulletin"));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/programs/search?keyword=Archived%20Bulletin&{CollectedGuide.TheDay}");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [$"{CollectedGuide.Network}-{CollectedGuide.Television}-21", $"{CollectedGuide.Network}-{CollectedGuide.WithdrawnService}-25"],
            data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
    }

    private static EpgFeature Feature(IReadOnlyList<BroadcastStream>? streams = null)
        => new(streams, clock: new WoundClock(At));

    private static BroadcastStream Terrestrial(int network, int service)
        => new(
            new NetworkId(network),
            new TransportStreamId(32_736),
            TuningParameters.Terrestrial(22),
            [new ServiceId(service)]);

    private static BroadcastStream Satellite(int network, int service)
        => new(
            new NetworkId(network),
            new TransportStreamId(32_737),
            TuningParameters.Bs(5, new TransportStreamId(32_737)),
            [new ServiceId(service)]);

    private static Programme Programme(int carried, string name, string summary = "")
        => On(1049, carried, name, summary);

    private static ArchivedProgramme Archived(int carried, string name)
        => Kept(carried, name, At.AddDays(-3));

    private static ArchivedProgramme Kept(int carried, string name, DateTime began)
        => ArchivedProgramme.Rehydrate(
            new NetworkId(4),
            new ServiceId(1049),
            new EventId(carried),
            began,
            began.AddMinutes(30),
            name,
            string.Empty,
            false,
            [],
            [],
            At);

    private static Programme Ran(int carried, string name, DateTime began)
        => Domain.Programmes.Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(4), new ServiceId(1049), new EventId(carried)),
                new TransportStreamId(32_736),
                began,
                began.AddMinutes(30),
                name,
                string.Empty,
                false),
            At);

    private static Programme Filed(int carried, string name, int genre)
        => Held(
            1049,
            carried,
            name,
            string.Empty,
            [new ProgrammeGenre(genre, 0)]);

    private static Programme On(int service, int carried, string name, string summary = "")
        => Held(service, carried, name, summary, []);

    private static Programme Held(
        int service,
        int carried,
        string name,
        string summary,
        IReadOnlyList<ProgrammeGenre> genres)
        => Domain.Programmes.Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(4), new ServiceId(service), new EventId(carried)),
                new TransportStreamId(32_736),
                At,
                At.AddMinutes(30),
                name,
                summary,
                false)
            {
                Genres = genres,
            },
            At);
}
