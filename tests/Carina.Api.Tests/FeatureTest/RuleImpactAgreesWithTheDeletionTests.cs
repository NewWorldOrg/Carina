using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Rules;

namespace Carina.Api.Tests.FeatureTest;

public sealed class RuleImpactAgreesWithTheDeletionTests
{
    private const int Uncollected = 32_737;

    private const int Unvouched = 1040;

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

    private static readonly Guid TheRule = new("00000001-0000-0000-0000-000000000000");

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
