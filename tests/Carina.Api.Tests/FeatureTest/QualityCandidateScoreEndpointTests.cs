using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;

namespace Carina.Api.Tests.FeatureTest;

public sealed class QualityCandidateScoreEndpointTests
{
    private static readonly DateTime Noon = QualityFeature.Noon;

    [Fact(DisplayName = "FR-008: a candidate's written-back score is listed with when it was evaluated")]
    public async Task ACandidatesWrittenBackScoreIsListedWithWhenItWasEvaluated()
    {
        await using var feature = new QualityFeature();
        CandidateChannel candidate = feature.Candidate(101, 27);
        candidate.Select(SelectionSource.Manual, null, Noon.AddDays(-30));
        candidate.Evaluated(CandidateScore.Of(400, 300, 21_500, 0.0002, Noon.AddDays(-7), Noon, Noon));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/candidate-scores");
        JsonElement item = Assert.Single(body.GetProperty("data").GetProperty("items").EnumerateArray());
        JsonElement score = item.GetProperty("score");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(candidate.Id.Value.ToString(), item.GetProperty("candidateId").GetString());
        Assert.Equal(4, item.GetProperty("networkId").GetInt32());
        Assert.Equal(101, item.GetProperty("serviceId").GetInt32());
        Assert.Equal(27, item.GetProperty("target").GetProperty("physicalChannel").GetInt32());
        Assert.True(item.GetProperty("isSelected").GetBoolean());
        Assert.Equal(0.75, score.GetProperty("lockRate").GetDouble());
        Assert.Equal(21_500, score.GetProperty("carrierToNoiseLowestMilliDecibels").GetInt32());
        Assert.Equal(0.0002, score.GetProperty("bitErrorRateHighest").GetDouble());
        Assert.Equal(400, score.GetProperty("samples").GetInt64());
        Assert.Equal(Noon.AddDays(-7), score.GetProperty("measuredFrom").GetDateTimeOffset().UtcDateTime);
        Assert.Equal(Noon, score.GetProperty("measuredUntil").GetDateTimeOffset().UtcDateTime);
        Assert.Equal(Noon, item.GetProperty("evaluatedAt").GetDateTimeOffset().UtcDateTime);
    }

    [Fact(DisplayName = "BR-QD-001: a candidate never scored is listed with no score rather than a score of nothing")]
    public async Task ACandidateNeverScoredIsListedWithNoScoreRatherThanAScoreOfNothing()
    {
        await using var feature = new QualityFeature();
        feature.Candidate(101, 27);

        JsonElement item = Assert.Single(
            (await feature.GetAsync("/api/quality/candidate-scores")).Body.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.True(Absent(item, "score"));
        Assert.True(Absent(item, "evaluatedAt"));
        Assert.False(item.GetProperty("isSelected").GetBoolean());
    }

    private static bool Absent(JsonElement item, string name)
        => !item.TryGetProperty(name, out JsonElement value) || value.ValueKind is JsonValueKind.Null;
}
