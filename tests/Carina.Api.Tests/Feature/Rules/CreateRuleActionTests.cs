using System.Net;
using System.Text.Json;

using Carina.Domain.Reservations;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Rules;

[Trait("Category", "Feature")]
public sealed class CreateRuleActionTests
{
    [Fact]
    public async Task ARuleThatNarrowsSomethingIsWrittenAndAnsweredWhereItLives()
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/rules",
            new { name = "hills", query = "keyword=hill", priority = 40 });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.True(body.GetProperty("status").GetBoolean());
        Assert.Equal("hills", body.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(40, body.GetProperty("data").GetProperty("priority").GetInt32());
        Assert.True(body.GetProperty("data").GetProperty("enabled").GetBoolean());
        Assert.Single(feature.Rules.Rules);
        Assert.Equal([RecalculationTrigger.RulesChanged], feature.Notices.Nudged);
        Assert.Equal([AppEventName.Rules], feature.Events.Signalled);
    }

    [Fact(DisplayName = "a rule that says nothing about encoding is written asking for one")]
    public async Task ARuleThatSaysNothingAboutEncodingIsWrittenAskingForOne()
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/rules",
            new { name = "hills", query = "keyword=hill" });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.True(body.GetProperty("data").GetProperty("encodeWhenRecorded").GetBoolean());
        Assert.True(feature.Rules.Rules[0].EncodeWhenRecorded);
    }

    [Fact(DisplayName = "a rule can be written asking for no encode, and answers that back")]
    public async Task ARuleCanBeWrittenAskingForNoEncode()
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/rules",
            new { name = "hills", query = "keyword=hill", encodeWhenRecorded = false });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.False(body.GetProperty("data").GetProperty("encodeWhenRecorded").GetBoolean());
        Assert.False(feature.Rules.Rules[0].EncodeWhenRecorded);
    }

    [Fact]
    public async Task ARuleWhoseConditionsAreAllEmptyIsRefusedRatherThanWritten()
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/rules",
            new { name = "everything", query = "sort=Name" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Contains("narrow", body.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(feature.Rules.Rules);
        Assert.Empty(feature.Notices.Nudged);
        Assert.Empty(feature.Events.Signalled);
    }

    [Theory]
    [InlineData("page=2")]
    [InlineData("perPage=10")]
    [InlineData("fields=Title")]
    [InlineData("descending=true")]
    public async Task AQueryThatOnlySaysHowToListIsNotAConditionEither(string query)
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, _) = await feature.PostAsync("/api/rules", new { name = "listing", query });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty(feature.Rules.Rules);
    }

    [Theory]
    [InlineData("keyword=hill")]
    [InlineData("genre=7")]
    [InlineData("channel=32736-1024")]
    [InlineData("type=IsdbT")]
    [InlineData("exclude=river&keyword=hill")]
    [InlineData("mark=New")]
    [InlineData("excludeMark=Rerun")]
    public async Task AQueryThatNarrowsSomethingIsWrittenSoTheRefusalIsNotSimplyTurningEverythingAway(string query)
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, _) = await feature.PostAsync("/api/rules", new { name = "narrowing", query });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Single(feature.Rules.Rules);
    }

    [Theory]
    [InlineData("", "keyword=hill")]
    [InlineData("named", "")]
    [InlineData("named", "?keyword=hill")]
    [InlineData("named", "keyword=hill#top")]
    public async Task ARuleThatIsNotWrittenAsARuleIsRefused(string name, string query)
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, _) = await feature.PostAsync("/api/rules", new { name, query });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty(feature.Rules.Rules);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task APriorityOutsideTheRangeIsRefused(int priority)
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, _) = await feature.PostAsync(
            "/api/rules",
            new { name = "graded", query = "keyword=hill", priority });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty(feature.Rules.Rules);
    }

    [Fact]
    public async Task ARuleAskedForOffMakesNothingAndRingsNoBell()
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/rules",
            new { name = "waiting", query = "keyword=hill", enabled = false });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.False(body.GetProperty("data").GetProperty("enabled").GetBoolean());
        Assert.Empty(feature.Notices.Nudged);
        Assert.Equal([AppEventName.Rules], feature.Events.Signalled);
    }

    [Theory]
    [InlineData("mark=Newer")]
    [InlineData("excludeMark=")]
    [InlineData("keyword=hill&exclude=再")]
    public async Task AMarkTheGuideDoesNotKnowOrALetterLeftOutOnItsOwnIsRefused(string query)
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, _) = await feature.PostAsync("/api/rules", new { name = "marked", query });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty(feature.Rules.Rules);
    }
}
