using System.Net;
using System.Text.Json;

using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class RecordingSearchEndpointTests
{
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
        Assert.Equal(["currentPage", "items", "lastPage", "perPage", "total"], Named(data));
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

    private static JsonElement Only(JsonElement body)
    {
        JsonElement items = body.GetProperty("data").GetProperty("items");

        Assert.Equal(1, items.GetArrayLength());

        return items[0];
    }
}
