using System.Net;
using System.Text.Json;

using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Rules;

namespace Carina.Api.Tests.FeatureTest;

public sealed class RuleEndpointTests
{
    private static readonly TimeSpan LongEnoughToTellAStallFromAWait = TimeSpan.FromSeconds(20);

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
    }

    [Fact]
    public async Task TheRulesAreListedInTheOrderTheyTakeProgrammes()
    {
        await using var feature = new RuleFeature();
        feature.Written(name: "behind", priority: 10, identifier: 1);
        feature.Written(name: "ahead", priority: 90, identifier: 2);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/rules");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["ahead", "behind"],
            body.GetProperty("data").GetProperty("rules").EnumerateArray()
                .Select(rule => rule.GetProperty("name").GetString() ?? string.Empty)
                .ToArray());
        Assert.Equal(2, body.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ARuleIsReadBackByItsOwnName()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written(name: "hills");

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/rules/{written.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("hills", body.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ARuleThatIsNotThereIsAnsweredAsNotThere()
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, _) = await feature.GetAsync($"/api/rules/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task ARuleIsRewrittenAndTheBellIsRung()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written(name: "hills", query: "keyword=hill");

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            $"/api/rules/{written.Id.Value}",
            new { name = "rivers", query = "keyword=river", priority = 20 });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("rivers", body.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("keyword=river", body.GetProperty("data").GetProperty("query").GetString());
        Assert.Equal("rivers", feature.Rules.Rules[0].Name);
        Assert.Equal([RecalculationTrigger.RulesChanged], feature.Notices.Nudged);
    }

    [Fact]
    public async Task ARewriteThatNarrowsNothingLeavesTheRuleAsItWas()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written(name: "hills", query: "keyword=hill");

        (HttpStatusCode status, _) = await feature.PutAsync(
            $"/api/rules/{written.Id.Value}",
            new { name = "everything", query = "sort=Name" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("hills", feature.Rules.Rules[0].Name);
        Assert.Empty(feature.Notices.Nudged);
    }

    [Fact]
    public async Task SwitchingARuleOffPullsWhatItMadeBackAndSaysHowMuchCameBack()
    {
        await using var feature = new RuleFeature();
        feature.Collected();
        Rule written = feature.Written();
        feature.Booked(feature.Announced(1), written.Id);

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            $"/api/rules/{written.Id.Value}/enabled",
            new { enabled = false });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("data").GetProperty("rule").GetProperty("enabled").GetBoolean());
        Assert.Equal(1, body.GetProperty("data").GetProperty("withdrawn").GetInt32());
        Assert.Empty(feature.Reservations.Held);
    }

    [Fact]
    public async Task SwitchingARuleOnLeavesTheReservationsAloneAndSaysNothingCameBack()
    {
        await using var feature = new RuleFeature();
        feature.Collected();
        Rule written = feature.Written(enabled: false);
        feature.Booked(feature.Announced(1), written.Id);

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            $"/api/rules/{written.Id.Value}/enabled",
            new { enabled = true });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("data").GetProperty("rule").GetProperty("enabled").GetBoolean());
        Assert.Equal(0, body.GetProperty("data").GetProperty("withdrawn").GetInt32());
        Assert.Single(feature.Reservations.Held);
    }

    [Fact]
    public async Task ASwitchThatSaysNeitherOnNorOffIsRefused()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written();

        (HttpStatusCode status, _) = await feature.PatchAsync(
            $"/api/rules/{written.Id.Value}/enabled",
            new { });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(feature.Rules.Rules[0].Enabled);
    }

    [Fact]
    public async Task DeletingARuleTakesTheReservationsItMadeWithIt()
    {
        await using var feature = new RuleFeature();
        feature.Collected();
        Rule written = feature.Written();
        feature.Booked(feature.Announced(1), written.Id);
        Reservation byHand = feature.Booked(feature.Announced(2, "river fishing"));

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/rules/{written.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("withdrawn").GetInt32());
        Assert.Equal(0, body.GetProperty("data").GetProperty("swept").GetInt32());
        Assert.Empty(feature.Rules.Rules);
        Assert.Equal([byHand.Id], [.. feature.Reservations.Held.Select(held => held.Id)]);
    }

    [Fact]
    public async Task DeletingARuleNoCollectionVouchesForStillTakesItsReservationsWithIt()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written();
        feature.Booked(feature.Announced(1), written.Id);

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/rules/{written.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, body.GetProperty("data").GetProperty("withdrawn").GetInt32());
        Assert.Equal(1, body.GetProperty("data").GetProperty("swept").GetInt32());
        Assert.Empty(feature.Reservations.Held);
    }

    [Fact]
    public async Task DeletingARuleThatIsNotThereIsAnsweredAsNotThere()
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, _) = await feature.DeleteAsync($"/api/rules/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task APreviewSaysWhatADraftWouldTakeWithoutWritingAnything()
    {
        await using var feature = new RuleFeature();
        feature.Announced(1, "hill walking");
        feature.Announced(2, "river fishing");

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/rules/preview",
            new { query = "keyword=hill" });

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement data = body.GetProperty("data");
        Assert.Equal(1, data.GetProperty("matched").GetInt32());
        Assert.Equal(1, data.GetProperty("making").GetInt32());
        Assert.Equal(
            ["hill walking"],
            data.GetProperty("takes").EnumerateArray()
                .Select(take => take.GetProperty("name").GetString() ?? string.Empty)
                .ToArray());
        Assert.Empty(feature.Rules.Rules);
        Assert.Empty(feature.Reservations.Held);
    }

    [Fact]
    public async Task APreviewCountsWhatIsCarriedAsAShadowOutRatherThanTakingIt()
    {
        await using var feature = new RuleFeature();
        DateTime opens = RuleFeature.Noon.AddHours(3);
        feature.Announced(1, "hill walking", startsAt: opens);
        feature.Announced(2, "hill walking", RuleFeature.Alongside, opens, shadow: true);

        (_, JsonElement body) = await feature.PostAsync("/api/rules/preview", new { query = "keyword=hill" });

        Assert.Equal(1, body.GetProperty("data").GetProperty("matched").GetInt32());
        Assert.Equal(1, body.GetProperty("data").GetProperty("excludedAsShadows").GetInt32());
    }

    [Fact]
    public async Task APreviewWithNothingCarriedAsAShadowCountsNoneOut()
    {
        await using var feature = new RuleFeature();
        DateTime opens = RuleFeature.Noon.AddHours(3);
        feature.Announced(1, "hill walking", startsAt: opens);
        feature.Announced(2, "hill climbing", RuleFeature.Alongside, opens);

        (_, JsonElement body) = await feature.PostAsync("/api/rules/preview", new { query = "keyword=hill" });

        Assert.Equal(2, body.GetProperty("data").GetProperty("matched").GetInt32());
        Assert.Equal(0, body.GetProperty("data").GetProperty("excludedAsShadows").GetInt32());
    }

    [Fact]
    public async Task AnUnsavedDraftClashesWithWhatAlreadyStandsWhenThereIsOneSeat()
    {
        await using var feature = new RuleFeature(seats: 1);
        DateTime opens = RuleFeature.Noon.AddHours(3);
        feature.Announced(1, "hill walking", startsAt: opens);
        feature.Booked(feature.Announced(2, "a booking", RuleFeature.Alongside, opens));

        (_, JsonElement body) = await feature.PostAsync("/api/rules/preview", new { query = "keyword=hill" });

        Assert.Equal(1, body.GetProperty("data").GetProperty("making").GetInt32());
        Assert.Equal(1, body.GetProperty("data").GetProperty("contendedAltogether").GetInt32());
    }

    [Fact]
    public async Task TheSameDraftClashesWithNothingWhenThereIsASeatForItAsWell()
    {
        await using var feature = new RuleFeature(seats: 2);
        DateTime opens = RuleFeature.Noon.AddHours(3);
        feature.Announced(1, "hill walking", startsAt: opens);
        feature.Booked(feature.Announced(2, "a booking", RuleFeature.Alongside, opens));

        (_, JsonElement body) = await feature.PostAsync("/api/rules/preview", new { query = "keyword=hill" });

        Assert.Equal(1, body.GetProperty("data").GetProperty("making").GetInt32());
        Assert.Equal(0, body.GetProperty("data").GetProperty("contendedAltogether").GetInt32());
    }

    [Fact]
    public async Task APreviewOfADraftThatNarrowsNothingIsRefused()
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, _) = await feature.PostAsync("/api/rules/preview", new { query = "sort=Name" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task TheImpactOfADraftSaysWhatIsMadeWhatComesBackAndWhatChangesHands()
    {
        await using var feature = new RuleFeature();
        feature.Collected();
        Rule written = feature.Written(query: "keyword=hill");
        Rule beside = feature.Written(query: "keyword=river", identifier: 2);
        feature.Booked(feature.Announced(1, "hill walking"), written.Id);
        feature.Booked(feature.Announced(2, "river fishing"), beside.Id);
        feature.Announced(3, "river rafting");

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/rules/impact",
            new { ruleId = written.Id.Value, query = "keyword=river", priority = 90 });

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement data = body.GetProperty("data");
        Assert.Equal(1, data.GetProperty("making").GetInt32());
        Assert.Equal(1, data.GetProperty("withdrawing").GetInt32());
        Assert.Equal(1, data.GetProperty("changingHands").GetInt32());
    }

    [Fact]
    public async Task TheImpactOfADraftThatChangesNothingIsAllZeroes()
    {
        await using var feature = new RuleFeature();
        feature.Collected();
        Rule written = feature.Written(query: "keyword=hill");
        feature.Booked(feature.Announced(1, "hill walking"), written.Id);

        (_, JsonElement body) = await feature.PostAsync(
            "/api/rules/impact",
            new { ruleId = written.Id.Value, query = "keyword=hill" });

        JsonElement data = body.GetProperty("data");
        Assert.Equal(0, data.GetProperty("making").GetInt32());
        Assert.Equal(0, data.GetProperty("withdrawing").GetInt32());
        Assert.Equal(0, data.GetProperty("changingHands").GetInt32());
    }

    [Fact]
    public async Task TheImpactOfADraftNamingARuleThatIsNotThereIsAnsweredAsNotThere()
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, _) = await feature.PostAsync(
            "/api/rules/impact",
            new { ruleId = Guid.NewGuid(), query = "keyword=hill" });

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task AskingForARuleToBeAppliedNowRunsAPassAndSaysWhatItDid()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"/api/rules/{written.Id.Value}/apply-now");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(11, body.GetProperty("data").GetProperty("revision").GetInt64());
        Assert.Equal(1, feature.Passes.Ran);
        Assert.Contains(RecalculationTrigger.RulesChanged, feature.Notices.Nudged);
    }

    [Fact]
    public async Task ASecondAskingWhileTheFirstIsWalkingIsRefusedWithTheOneThatIsWalking()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written();
        feature.Passes.Held = new TaskCompletionSource();

        Task<(HttpStatusCode Status, JsonElement Body)> first =
            feature.PostAsync($"/api/rules/{written.Id.Value}/apply-now");
        await feature.Passes.Entered.WaitAsync(LongEnoughToTellAStallFromAWait);

        (HttpStatusCode status, JsonElement body) = await feature
            .PostAsync($"/api/rules/{written.Id.Value}/apply-now")
            .WaitAsync(LongEnoughToTellAStallFromAWait);

        feature.Passes.Held.SetResult();

        (_, JsonElement walked) = await first.WaitAsync(LongEnoughToTellAStallFromAWait);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("oneIsAlreadyRunning", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Equal(
            walked.GetProperty("data").GetProperty("applyId").GetGuid(),
            body.GetProperty("data").GetProperty("runningApplyId").GetGuid());
        Assert.Equal(1, feature.Passes.Ran);
    }

    [Fact]
    public async Task AskingAgainTooSoonIsRefusedWithTheMomentItMayBeAskedForAgain()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written();

        await feature.PostAsync($"/api/rules/{written.Id.Value}/apply-now")
            .WaitAsync(LongEnoughToTellAStallFromAWait);

        (HttpStatusCode status, JsonElement body) = await feature
            .PostAsync($"/api/rules/{written.Id.Value}/apply-now")
            .WaitAsync(LongEnoughToTellAStallFromAWait);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("tooSoonAfterTheLastOne", body.GetProperty("data").GetProperty("refusal").GetString());
        Assert.Equal(
            RuleFeature.Noon + RuleApplySettings.DefaultBetweenApplications,
            body.GetProperty("data").GetProperty("notBefore").GetDateTimeOffset().UtcDateTime);
        Assert.Equal(1, feature.Passes.Ran);
    }

    [Fact]
    public async Task AStandingRecalculationHoldingTheFloorIsAnsweredAsSuch()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written();
        feature.Passes.Answers = RecalculationPass.Refused(RecalculationRefusal.OneIsAlreadyRunning);

        (HttpStatusCode status, JsonElement body) = await feature
            .PostAsync($"/api/rules/{written.Id.Value}/apply-now")
            .WaitAsync(LongEnoughToTellAStallFromAWait);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(
            "aRecalculationIsAlreadyRunning",
            body.GetProperty("data").GetProperty("refusal").GetString());
    }

    [Fact]
    public async Task AskingForARuleThatIsNotThereToBeAppliedIsAnsweredAsNotThere()
    {
        await using var feature = new RuleFeature();

        (HttpStatusCode status, _) = await feature
            .PostAsync($"/api/rules/{Guid.NewGuid()}/apply-now")
            .WaitAsync(LongEnoughToTellAStallFromAWait);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, feature.Passes.Ran);
    }
}
