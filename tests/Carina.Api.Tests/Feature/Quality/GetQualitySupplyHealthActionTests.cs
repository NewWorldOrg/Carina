using System.Net;
using System.Text.Json;

using Carina.Domain.Quality;

namespace Carina.Api.Tests.Feature.Quality;

[Trait("Category", "Feature")]
public sealed class GetQualitySupplyHealthActionTests
{
    private static readonly DateTime Noon = QualityFeature.Noon;

    [Fact(DisplayName = "the supply health says what each of the four supplies stands at")]
    public async Task TheSupplyHealthSaysWhatEachOfTheFourSuppliesStandsAt()
    {
        await using var feature = new QualityFeature();

        feature.Board.Held(SupplyStanding.Of(
            Noon.AddMinutes(-1),
            QualityThresholdShapes.AsShipped(QualityThresholdKey.SupplySilence, Noon.AddMinutes(-1)),
            tunersWereAsked: true,
            [
                new SupplySilenceStanding(SupplySilence.RecordingProgress, 1, 0),
                new SupplySilenceStanding(SupplySilence.RecordingMeasurement, 1, 1),
                new SupplySilenceStanding(SupplySilence.SignalSamples, 0, 0),
                new SupplySilenceStanding(SupplySilence.GuideVisits, 7, 0),
            ]));

        JsonElement data = (await feature.GetAsync("/api/quality/supply-health")).Body.GetProperty("data");

        Assert.Equal(300, data.GetProperty("appliedValue").GetDouble());
        Assert.True(data.GetProperty("tunersWereAsked").GetBoolean());
        Assert.Equal(
            ["good", "unreachable", "nothingToMeasure", "good"],
            data.GetProperty("supplies").EnumerateArray().Select(supply => supply.GetProperty("state").GetString()));
    }

    [Fact(DisplayName = "a driver that could not be asked leaves the samples as could-not-be-read")]
    public async Task ADriverThatCouldNotBeAskedLeavesTheSamplesAsCouldNotBeRead()
    {
        await using var feature = new QualityFeature();

        feature.Board.Held(SupplyStanding.Of(
            Noon.AddMinutes(-1),
            QualityThresholdShapes.AsShipped(QualityThresholdKey.SupplySilence, Noon.AddMinutes(-1)),
            tunersWereAsked: false,
            [new SupplySilenceStanding(SupplySilence.SignalSamples, 0, 0)]));

        JsonElement data = (await feature.GetAsync("/api/quality/supply-health")).Body.GetProperty("data");

        Assert.Equal(
            "unreachable",
            data.GetProperty("supplies")[0].GetProperty("state").GetString());
    }

    [Fact(DisplayName = "a watch that has not read anything yet says so rather than saying all is well")]
    public async Task AWatchThatHasNotReadAnythingYetSaysSoRatherThanSayingAllIsWell()
    {
        await using var feature = new QualityFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/supply-health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.False(body.GetProperty("status").GetBoolean());
    }
}
