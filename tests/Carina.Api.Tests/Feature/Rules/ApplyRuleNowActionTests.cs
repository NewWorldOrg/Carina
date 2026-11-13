using System.Net;
using System.Text.Json;

using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Rules;

namespace Carina.Api.Tests.Feature.Rules;

[Trait("Category", "Feature")]
public sealed class ApplyRuleNowActionTests
{
    private static readonly TimeSpan LongEnoughToTellAStallFromAWait = TimeSpan.FromSeconds(20);

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
        Assert.Equal([RecalculationTrigger.RulesChanged], feature.Passes.Asked);
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
    public async Task APassThatCouldNotReadTheRulesIsSaidToBeUnknownRatherThanCountedAsNone()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written();
        feature.Passes.Applied = null;

        (HttpStatusCode status, JsonElement body) = await feature
            .PostAsync($"/api/rules/{written.Id.Value}/apply-now")
            .WaitAsync(LongEnoughToTellAStallFromAWait);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.False(body.GetProperty("status").GetBoolean());
    }

    [Fact]
    public async Task WhatIsAnsweredAsMadeAndWithdrawnIsWhatThePassSaysItMadeAndWithdrew()
    {
        await using var feature = new RuleFeature();
        Rule written = feature.Written();
        Programme taken = feature.Announced(1, "hill walking");
        Programme gone = feature.Announced(2, "hill climbing");
        feature.Passes.Applied = new RuleApplicationRun(
            13,
            2,
            [feature.Booked(taken, written.Id)],
            [],
            [],
            [feature.Booked(gone, written.Id)],
            [],
            []);

        (HttpStatusCode status, JsonElement body) = await feature
            .PostAsync($"/api/rules/{written.Id.Value}/apply-now")
            .WaitAsync(LongEnoughToTellAStallFromAWait);

        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, data.GetProperty("made").GetInt32());
        Assert.Equal(1, data.GetProperty("withdrawn").GetInt32());
        Assert.Equal(2, data.GetProperty("read").GetInt32());
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
