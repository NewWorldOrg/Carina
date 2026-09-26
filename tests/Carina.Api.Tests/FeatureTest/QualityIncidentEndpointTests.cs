using System.Net;
using System.Text.Json;

using Carina.Domain.Quality;

namespace Carina.Api.Tests.FeatureTest;

public sealed class QualityIncidentEndpointTests
{
    private static readonly DateTime Noon = QualityFeature.Noon;

    [Fact(DisplayName = "BR-QD-008: an anomaly is listed with the classification it was kept under")]
    public async Task AnAnomalyIsListedWithTheClassificationItWasKeptUnder()
    {
        await using var feature = new QualityFeature();
        feature.Quiet();

        JsonElement data = (await feature.GetAsync("/api/quality/incidents")).Body.GetProperty("data");
        JsonElement incident = data.GetProperty("items")[0];

        Assert.Equal("supplySilence", incident.GetProperty("breached").GetString());
        Assert.Equal("signalSamples", incident.GetProperty("silence").GetString());
        Assert.Equal("tuner", incident.GetProperty("subjectKind").GetString());
        Assert.Equal("adapter3.frontend0", incident.GetProperty("subjectKey").GetString());
        Assert.Equal(300, incident.GetProperty("appliedValue").GetDouble());
        Assert.True(incident.GetProperty("appliedProvisional").GetBoolean());
        Assert.False(incident.GetProperty("restated").GetBoolean());
    }

    [Fact(DisplayName = "BR-QD-002: an anomaly another domain owns is listed as a restatement and counted apart")]
    public async Task AnAnomalyAnotherDomainOwnsIsListedAsARestatementAndCountedApart()
    {
        await using var feature = new QualityFeature();
        feature.Quiet();
        feature.Restated();

        JsonElement data = (await feature.GetAsync("/api/quality/incidents")).Body.GetProperty("data");

        Assert.Equal(1, data.GetProperty("owned").GetInt32());
        Assert.Equal(1, data.GetProperty("restated").GetInt32());
        Assert.Contains(
            data.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("restated").GetBoolean()
                    && item.GetProperty("classification").GetString() == "NoLock");
    }

    [Fact(DisplayName = "BR-QS-002: an anomaly that has been told about stays on the list for as long as it stands")]
    public async Task AnAnomalyThatHasBeenToldAboutStaysOnTheListForAsLongAsItStands()
    {
        await using var feature = new QualityFeature();
        QualityIncident standing = feature.Quiet();

        JsonElement listed = Assert.Single(
            (await feature.GetAsync("/api/quality/incidents")).Body.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal(standing.Id.Value.ToString(), listed.GetProperty("id").GetString());
        Assert.Equal("notified", listed.GetProperty("state").GetString());
        Assert.False(listed.TryGetProperty("acknowledgedAt", out _));
        Assert.False(listed.TryGetProperty("acknowledgedBy", out _));
    }

    [Fact(DisplayName = "BR-QS-002: an anomaly whose condition has cleared leaves the list and is not deleted")]
    public async Task AnAnomalyWhoseConditionHasClearedLeavesTheListAndIsNotDeleted()
    {
        await using var feature = new QualityFeature();
        QualityIncident settled = feature.Quiet();

        settled.Resolve(Noon.AddMinutes(-5));

        JsonElement data = (await feature.GetAsync("/api/quality/incidents")).Body.GetProperty("data");

        Assert.Empty(data.GetProperty("items").EnumerateArray());
        Assert.Single(feature.Incidents.Incidents);
    }

    [Fact(DisplayName = "BR-QS-002: an anomaly has nothing left to acknowledge it by")]
    public async Task AnAnomalyHasNothingLeftToAcknowledgeItBy()
    {
        await using var feature = new QualityFeature();
        QualityIncident standing = feature.Quiet();

        (HttpStatusCode status, _) = await feature.PostAsync($"/api/quality/incidents/{standing.Id.Value}/acknowledge");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(QualityIncidentState.Notified, standing.State);
        Assert.Empty(feature.Events.Signalled);
    }

    [Fact(DisplayName = "BR-QD-007: the supply health says what each of the four supplies stands at")]
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

    [Fact(DisplayName = "BR-QD-007: a driver that could not be asked leaves the samples as could-not-be-read")]
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

    [Fact(DisplayName = "BR-QD-007: a watch that has not read anything yet says so rather than saying all is well")]
    public async Task AWatchThatHasNotReadAnythingYetSaysSoRatherThanSayingAllIsWell()
    {
        await using var feature = new QualityFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/quality/supply-health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.False(body.GetProperty("status").GetBoolean());
    }
}
