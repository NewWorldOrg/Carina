using System.Net;
using System.Text.Json;

using Carina.Domain.Quality;

namespace Carina.Api.Tests.FeatureTest;

public sealed class QualityEndpointTests
{
    [Fact(DisplayName = "a period nothing measured answers unmeasured rather than a clean share")]
    public async Task APeriodNothingMeasuredAnswersUnmeasured()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(dropped: null, total: null, scrambled: null);
        feature.Recorded(dropped: null, total: null, scrambled: null);
        feature.Recorded(dropped: null, total: null, scrambled: null);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/summary");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement data = body.GetProperty("data");
        JsonElement lost = Measure(data, "packetsLost");

        Assert.Equal(3, data.GetProperty("recordings").GetInt32());
        Assert.Equal("unmeasured", lost.GetProperty("state").GetString());
        Assert.Equal(3, lost.GetProperty("unmeasured").GetInt32());
        Assert.Equal(0, lost.GetProperty("measured").GetInt32());
        Assert.Equal(JsonValueKind.Null, lost.GetProperty("average").ValueKind);
    }

    [Fact(DisplayName = "an unmeasured recording stays out of the share and is counted beside it")]
    public async Task AnUnmeasuredRecordingStaysOutOfTheShare()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(dropped: 0, total: 1_000_000);
        feature.Recorded(dropped: null, total: null, scrambled: null);

        JsonElement lost = Measure((await feature.GetAsync("/api/quality/summary")).Body.GetProperty("data"), "packetsLost");

        Assert.Equal("good", lost.GetProperty("state").GetString());
        Assert.Equal(2, lost.GetProperty("subjects").GetInt32());
        Assert.Equal(1, lost.GetProperty("measured").GetInt32());
        Assert.Equal(1, lost.GetProperty("unmeasured").GetInt32());
        Assert.Equal(0, lost.GetProperty("average").GetDouble());
    }

    [Fact(DisplayName = "a period holding no recording answers nothingToMeasure")]
    public async Task APeriodHoldingNoRecordingAnswersNothingToMeasure()
    {
        await using var feature = new QualityFeature();

        JsonElement data = (await feature.GetAsync("/api/quality/summary")).Body.GetProperty("data");

        Assert.Equal(0, data.GetProperty("recordings").GetInt32());
        Assert.Equal("nothingToMeasure", Measure(data, "packetsLost").GetProperty("state").GetString());
    }

    [Fact(DisplayName = "a period beyond the threshold still says how many were measured")]
    public async Task APeriodBeyondTheThresholdStillSaysHowManyWereMeasured()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(dropped: 0, total: 1_000_000);
        feature.Recorded(dropped: 2_000, total: 1_000_000);
        feature.Recorded(dropped: null, total: null, scrambled: null);

        JsonElement lost = Measure((await feature.GetAsync("/api/quality/summary")).Body.GetProperty("data"), "packetsLost");

        Assert.Equal("atOrAboveWarning", lost.GetProperty("state").GetString());
        Assert.Equal(2, lost.GetProperty("measured").GetInt32());
        Assert.Equal(1, lost.GetProperty("beyondThreshold").GetInt32());
        Assert.Equal(1, lost.GetProperty("unmeasured").GetInt32());
        Assert.Equal(1, lost.GetProperty("mayNotBeWatchable").GetInt32());
        Assert.Equal(1, lost.GetProperty("good").GetInt32());
    }

    [Fact(DisplayName = "nothing has ever sampled a signal, so the signal reads unmeasured and never good")]
    public async Task TheSignalReadsUnmeasuredAndNeverGood()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(tuner: "adapter3.frontend0");
        feature.Recorded(tuner: "adapter3.frontend0");

        JsonElement signal = (await feature.GetAsync("/api/quality/summary")).Body
            .GetProperty("data")
            .GetProperty("signal");

        Assert.Equal(3, signal.GetArrayLength());
        Assert.All(signal.EnumerateArray(), facet =>
        {
            Assert.Equal("unmeasured", facet.GetProperty("reading").GetProperty("state").GetString());
            Assert.Equal(1, facet.GetProperty("reading").GetProperty("subjects").GetInt32());
            Assert.Equal(0, facet.GetProperty("reading").GetProperty("measured").GetInt32());
            Assert.Equal(JsonValueKind.Null, facet.GetProperty("lastTakenAt").ValueKind);
        });
        Assert.Equal(
            ["lockRate", "carrierToNoiseFloor", "bitErrorRateCeiling"],
            signal.EnumerateArray().Select(facet => facet.GetProperty("metric").GetString()));
    }

    [Fact]
    public async Task ASignalNoTunerWasSeenForReadsAsNothingToMeasure()
    {
        await using var feature = new QualityFeature();

        JsonElement signal = (await feature.GetAsync("/api/quality/summary")).Body
            .GetProperty("data")
            .GetProperty("signal");

        Assert.All(signal.EnumerateArray(), facet =>
            Assert.Equal("nothingToMeasure", facet.GetProperty("reading").GetProperty("state").GetString()));
    }

    [Fact(DisplayName = "every level the answer was judged against is still provisional")]
    public async Task EveryLevelTheAnswerWasJudgedAgainstIsStillProvisional()
    {
        await using var feature = new QualityFeature();
        feature.Recorded();

        Assert.True((await feature.GetAsync("/api/quality/summary")).Body
            .GetProperty("data")
            .GetProperty("provisional")
            .GetBoolean());
    }

    [Fact]
    public async Task TheChannelsComeBackWorstFirstAndCarryEveryMeasure()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(service: 1_024, dropped: 0, total: 1_000_000);
        feature.Recorded(service: 1_032, dropped: 2_000, total: 1_000_000);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/channels");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement items = body.GetProperty("data").GetProperty("items");

        Assert.Equal([1_032, 1_024], items.EnumerateArray().Select(item => item.GetProperty("serviceId").GetInt32()));
        Assert.Equal(32_736, items[0].GetProperty("networkId").GetInt32());
        Assert.Equal("isdbT", items[0].GetProperty("kind").GetString());
        Assert.Equal(3, items[0].GetProperty("measures").GetArrayLength());
        Assert.Equal(2, body.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact(DisplayName = "only the measures this domain names are answered")]
    public async Task OnlyTheMeasuresThisDomainNamesAreAnswered()
    {
        await using var feature = new QualityFeature();
        feature.Recorded();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/channels?metric=overflows");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["overflows"],
            body.GetProperty("data").GetProperty("metrics").EnumerateArray().Select(metric => metric.GetString()));

        Assert.Equal(HttpStatusCode.BadRequest, (await feature.GetAsync("/api/quality/channels?metric=99")).Status);
    }

    [Fact(DisplayName = "an ordering outside the list is refused")]
    public async Task AnOrderingOutsideTheListIsRefused()
    {
        await using var feature = new QualityFeature();

        Assert.Equal(HttpStatusCode.BadRequest, (await feature.GetAsync("/api/quality/channels?sort=99")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await feature.GetAsync("/api/quality/recordings?sort=99")).Status);
    }

    [Fact(DisplayName = "a page size above the ceiling comes back cut down to it")]
    public async Task APageSizeAboveTheCeilingComesBackCutDownToIt()
    {
        await using var feature = new QualityFeature();

        JsonElement data = (await feature.GetAsync($"/api/quality/channels?perPage={QualityGroupQuery.MostPerPage + 1}"))
            .Body.GetProperty("data");

        Assert.Equal(QualityGroupQuery.MostPerPage, data.GetProperty("perPage").GetInt32());
    }

    [Fact(DisplayName = "a period wider than the longest span is refused")]
    public async Task APeriodWiderThanTheLongestSpanIsRefused()
    {
        await using var feature = new QualityFeature();
        DateTime tooFarBack = QualityFeature.Noon - QualityPeriod.LongestSpan - TimeSpan.FromDays(1);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await feature.GetAsync($"/api/quality/summary?from={tooFarBack:O}&until={QualityFeature.Noon:O}")).Status);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await feature.GetAsync($"/api/quality/channels?from={QualityFeature.Noon:O}&until={QualityFeature.Noon:O}")).Status);
    }

    [Fact]
    public async Task APeriodNobodyNamedReachesBackOneDay()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(startedAt: QualityFeature.Noon.AddDays(-3));
        feature.Recorded(startedAt: QualityFeature.Noon.AddHours(-3));

        Assert.Equal(
            1,
            (await feature.GetAsync("/api/quality/summary")).Body.GetProperty("data").GetProperty("recordings").GetInt32());
    }

    [Fact]
    public async Task TheTunersCarryTheirOwnMeasuresAndASignalNothingHasSampled()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(tuner: "adapter3.frontend0", dropped: 2_000, total: 1_000_000);
        feature.Recorded(tuner: "adapter3.frontend1", dropped: 0, total: 1_000_000);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/tuners");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement items = body.GetProperty("data").GetProperty("items");

        Assert.Equal(
            ["adapter3.frontend0", "adapter3.frontend1"],
            items.EnumerateArray().Select(item => item.GetProperty("deviceId").GetString()));
        Assert.Equal(3, items[0].GetProperty("signal").GetArrayLength());
        Assert.All(items.EnumerateArray(), item => Assert.All(
            item.GetProperty("signal").EnumerateArray(),
            facet => Assert.Equal("unmeasured", facet.GetProperty("reading").GetProperty("state").GetString())));
    }

    [Fact]
    public async Task ARecordingTheLedgerNamesNoTunerForIsStillCounted()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(tuner: null, dropped: null, total: null, scrambled: null);

        JsonElement items = (await feature.GetAsync("/api/quality/tuners")).Body.GetProperty("data").GetProperty("items");

        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("deviceId").ValueKind);
    }

    [Fact(DisplayName = "the recordings list holds the ones beyond the level and counts the unmeasured beside it")]
    public async Task TheRecordingsListHoldsTheOnesBeyondTheLevel()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(dropped: 0, total: 1_000_000);
        QualityLedgerRow bad = feature.Recorded(dropped: 2_000, total: 1_000_000);
        feature.Recorded(dropped: null, total: null, scrambled: null);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/recordings");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement data = body.GetProperty("data");

        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal(bad.Recording.Value.ToString(), data.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal("mayNotBeWatchable", data.GetProperty("items")[0].GetProperty("standing").GetString());
        Assert.Equal(1, Measure(data, "packetsLost", "whole").GetProperty("unmeasured").GetInt32());
        Assert.Equal(3, Measure(data, "packetsLost", "whole").GetProperty("subjects").GetInt32());
    }

    [Fact(DisplayName = "a recording keeps the level it was judged against and the one it passed")]
    public async Task ARecordingKeepsTheLevelItWasJudgedAgainst()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(dropped: 300, total: 1_000_000);

        JsonElement item = (await feature.GetAsync("/api/quality/recordings")).Body
            .GetProperty("data")
            .GetProperty("items")[0];

        JsonElement verdict = item.GetProperty("verdicts").EnumerateArray()
            .Single(read => read.GetProperty("metric").GetString() == "packetsLost");

        Assert.Equal("warning", item.GetProperty("standing").GetString());
        Assert.Equal("packetsLostWarning", verdict.GetProperty("applied").GetString());
        Assert.Equal("packetsLostWarning", verdict.GetProperty("breached").GetString());
        Assert.Equal(0.0002, verdict.GetProperty("appliedValue").GetDouble());
        Assert.True(verdict.GetProperty("provisional").GetBoolean());
        Assert.Equal(300, item.GetProperty("droppedPackets").GetInt64());
        Assert.Equal(1_000_000, item.GetProperty("totalPackets").GetInt64());
    }

    private static JsonElement Measure(JsonElement data, string metric, string under = "measures")
        => data.GetProperty(under).EnumerateArray()
            .Single(read => read.GetProperty("metric").GetString() == metric)
            .GetProperty("reading");
}
