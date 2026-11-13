using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Rules;

namespace Carina.Api.Tests.Feature.Rules;

[Trait("Category", "Feature")]
public sealed class ImpactOfRulesActionTests
{
    private static readonly Guid TheRule = new("00000001-0000-0000-0000-000000000000");

    private const int Uncollected = 32_737;

    private const int Unvouched = 1040;

    [Fact]
    public async Task TheImpactIsRefusedRatherThanGuessedWhenTheTunersCannotBeCounted()
    {
        await using var feature = new RuleFeature();
        feature.Announced(1, "hill walking");
        feature.Seating.Capacity = null;

        (HttpStatusCode status, _) = await feature.PostAsync(
            "/api/rules/impact",
            new { query = "keyword=hill" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
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
        Assert.Empty(feature.Events.Signalled);
    }

    [Fact]
    public async Task TheImpactOfADraftThatChangesNothingChangesNothingBySaving()
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
        Assert.Equal(1, data.GetProperty("sweeping").GetInt32());
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
    public async Task WhatTheImpactSaysDeletingWouldSweepIsTheNumberDeletingAnswersWith()
    {
        await using RuleFeature feature = Mixed();

        (HttpStatusCode asked, JsonElement impact) = await feature.PostAsync(
            "/api/rules/impact",
            new { ruleId = TheRule, query = "keyword=heather" });

        (HttpStatusCode deleted, JsonElement retirement) = await feature.DeleteAsync($"/api/rules/{TheRule}");

        Assert.Equal(HttpStatusCode.OK, asked);
        Assert.Equal(HttpStatusCode.OK, deleted);
        Assert.Equal(
            retirement.GetProperty("data").GetProperty("withdrawn").GetInt32()
                + retirement.GetProperty("data").GetProperty("swept").GetInt32(),
            impact.GetProperty("data").GetProperty("sweeping").GetInt32());
    }

    [Fact]
    public async Task SavingTakesFewerThanDeletingDoesWhenTheCollectionVouchesForOnlySomeOfThem()
    {
        await using RuleFeature feature = Mixed();

        (_, JsonElement impact) = await feature.PostAsync(
            "/api/rules/impact",
            new { ruleId = TheRule, query = "keyword=heather" });

        JsonElement data = impact.GetProperty("data");

        Assert.Equal(1, data.GetProperty("withdrawing").GetInt32());
        Assert.Equal(2, data.GetProperty("sweeping").GetInt32());
    }

    private static RuleFeature Mixed()
    {
        var feature = new RuleFeature();

        feature.Streams.Carried.Add(new BroadcastStream(
            new NetworkId(RuleFeature.Network),
            new TransportStreamId(Uncollected),
            TuningParameters.Terrestrial(29),
            [new ServiceId(Unvouched)]));

        feature.Collected();
        feature.Collected(Uncollected, VisitOutcome.Incomplete);
        feature.Tuning.Answer(Unvouched, TuningParameters.Terrestrial(29));

        Rule written = feature.Written(query: "keyword=hill");

        feature.Booked(feature.Announced(1, "hill walking"), written.Id);
        feature.Booked(
            feature.Announced(2, "hill climbing", serviceId: Unvouched, transportStreamId: Uncollected),
            written.Id);

        return feature;
    }
}
