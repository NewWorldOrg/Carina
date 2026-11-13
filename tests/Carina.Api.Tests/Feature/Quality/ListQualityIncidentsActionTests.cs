using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Quality;

namespace Carina.Api.Tests.Feature.Quality;

[Trait("Category", "Feature")]
public sealed class ListQualityIncidentsActionTests
{
    private const int ManyFailuresThatReadTheSame = 2_048;

    private const string FirstSatellite = "adapter0.frontend0";

    private static readonly DateTime Noon = QualityFeature.Noon;

    private static readonly IReadOnlyList<TuneFailureKind> TheFourClasses =
    [
        TuneFailureKind.NoLock,
        TuneFailureKind.NoData,
        TuneFailureKind.IncompletePsi,
        TuneFailureKind.StreamMismatch,
    ];

    private static readonly IReadOnlyList<string> TheTwoStreams = ["broadcast-satellite", "communications-satellite"];

    [Fact(DisplayName = "an anomaly is listed with the classification it was kept under")]
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

    [Fact(DisplayName = "an anomaly another domain owns is listed as a restatement and counted apart")]
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

    [Fact(DisplayName = "BR-QD-020: a level a channel's recordings passed is listed as this domain's own, on the channel, with the level it was held against")]
    public async Task ALevelAChannelsRecordingsPassedIsListedAsThisDomainsOwn()
    {
        await using var feature = new QualityFeature();
        QualityIncident breached = ThresholdBreachWatch.Open(
            QualityIncidentId.New(),
            new ThresholdBreach(
                QualityThresholdKey.PacketsLostUnwatchable,
                QualitySubject.Of(QualitySubjectKind.Channel, "32736-1024"),
                0.005,
                QualityThresholdShapes.AsShipped(QualityThresholdKey.PacketsLostUnwatchable, Noon.AddMinutes(-10))),
            Noon.AddMinutes(-10));

        feature.Incidents.Incidents.Add(breached);
        feature.Restated();

        JsonElement data = (await feature.GetAsync("/api/quality/incidents")).Body.GetProperty("data");
        JsonElement incident = data.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == breached.Id.Value.ToString());

        Assert.Equal("packetsLostUnwatchable", incident.GetProperty("breached").GetString());
        Assert.Equal("channel", incident.GetProperty("subjectKind").GetString());
        Assert.Equal("32736-1024", incident.GetProperty("subjectKey").GetString());
        Assert.Equal(0.005, incident.GetProperty("observed").GetDouble());
        Assert.Equal(0.001, incident.GetProperty("appliedValue").GetDouble());
        Assert.Equal("quality", incident.GetProperty("owner").GetString());
        Assert.False(incident.GetProperty("restated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, incident.GetProperty("classification").ValueKind);
        Assert.Equal(JsonValueKind.Null, incident.GetProperty("silence").ValueKind);
        Assert.Equal(1, data.GetProperty("owned").GetInt32());
        Assert.Equal(1, data.GetProperty("restated").GetInt32());
    }

    [Fact(DisplayName = "an anomaly that has been told about stays on the list for as long as it stands")]
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

    [Fact(DisplayName = "an anomaly whose condition has cleared leaves the list and is not deleted")]
    public async Task AnAnomalyWhoseConditionHasClearedLeavesTheListAndIsNotDeleted()
    {
        await using var feature = new QualityFeature();
        QualityIncident settled = feature.Quiet();

        settled.Resolve(Noon.AddMinutes(-5));

        JsonElement data = (await feature.GetAsync("/api/quality/incidents")).Body.GetProperty("data");

        Assert.Empty(data.GetProperty("items").EnumerateArray());
        Assert.Single(feature.Incidents.Incidents);
    }

    [Fact(DisplayName = "an anomaly has nothing left to acknowledge it by")]
    public async Task AnAnomalyHasNothingLeftToAcknowledgeItBy()
    {
        await using var feature = new QualityFeature();
        QualityIncident standing = feature.Quiet();

        (HttpStatusCode status, _) = await feature.PostAsync($"/api/quality/incidents/{standing.Id.Value}/acknowledge");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(QualityIncidentState.Notified, standing.State);
        Assert.Empty(feature.Events.Signalled);
    }

    [Fact(DisplayName = "thousands of failures that read the same keep the four classes they were refused in")]
    public async Task ThousandsOfFailuresThatReadTheSameKeepTheFourClassesTheyWereRefusedIn()
    {
        await using QualityFeature feature = new();
        Refused(feature);

        IReadOnlyList<string> classes = await ClassesAsync(feature);

        Assert.Equal(ManyFailuresThatReadTheSame, classes.Count);
        Assert.Equal(
            [.. TheFourClasses.Select(kind => kind.ToString()).Order(StringComparer.Ordinal)],
            classes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.All(
            TheFourClasses,
            kind => Assert.Equal(
                ManyFailuresThatReadTheSame / TheFourClasses.Count,
                classes.Count(held => string.Equals(held, kind.ToString(), StringComparison.Ordinal))));
    }

    [Fact(DisplayName = "the streams those failures were refused on stay tellable apart from one another")]
    public async Task TheStreamsThoseFailuresWereRefusedOnStayTellableApartFromOneAnother()
    {
        await using QualityFeature feature = new();
        Refused(feature);

        IReadOnlyList<(string Stream, string Class)> refused = await RefusalsAsync(feature);

        Assert.All(
            TheTwoStreams.SelectMany(_ => TheFourClasses, (stream, kind) => (Stream: stream, Class: kind.ToString())),
            pair => Assert.Equal(
                ManyFailuresThatReadTheSame / (TheFourClasses.Count * TheTwoStreams.Count),
                refused.Count(held => held == pair)));
    }

    [Fact(DisplayName = "none of those failures is counted among this domain's own")]
    public async Task NoneOfThoseFailuresIsCountedAmongThisDomainsOwn()
    {
        await using QualityFeature feature = new();
        Refused(feature);

        JsonElement data = (await feature.GetAsync("/api/quality/incidents")).Body.GetProperty("data");

        Assert.Equal(0, data.GetProperty("owned").GetInt32());
        Assert.Equal(ManyFailuresThatReadTheSame, data.GetProperty("restated").GetInt32());
        Assert.All(
            data.GetProperty("items").EnumerateArray(),
            item => Assert.True(item.GetProperty("restated").GetBoolean()));
    }

    [Fact(DisplayName = "BR-QD-018: the incidents list restates a tuner that is failing to tune under that classification")]
    public async Task TheIncidentsListRestatesATunerThatIsFailingToTune()
    {
        await using QualityFeature feature = new();
        feature.Troubled(FirstSatellite, TunerTroubleKind.TuneFailing);

        JsonElement item = Assert.Single(
            (await feature.GetAsync("/api/quality/incidents")).Body.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal("TuneFailing", item.GetProperty("classification").GetString());
        Assert.True(item.GetProperty("restated").GetBoolean());
        Assert.Equal(FirstSatellite, item.GetProperty("subjectKey").GetString());
    }

    private static void Refused(QualityFeature feature)
    {
        for (int at = 0; at < ManyFailuresThatReadTheSame; at++)
        {
            TuneFailureKind refusedIn = TheFourClasses[at % TheFourClasses.Count];
            string stream = TheTwoStreams[at / TheFourClasses.Count % TheTwoStreams.Count];
            DateTime detectedAt = Noon.AddMinutes(-1);

            feature.Incidents.Incidents.Add(QualityIncident.Detect(
                QualityIncidentId.New(),
                detectedAt,
                QualityThresholdKey.LockRate,
                QualitySubject.Of(QualitySubjectKind.TransportStream, stream),
                0,
                QualityThresholdShapes.AsShipped(QualityThresholdKey.LockRate, detectedAt),
                QualityIncidentOwner.Tuner,
                refusedIn.ToString()));
        }
    }

    private static async Task<IReadOnlyList<string>> ClassesAsync(QualityFeature feature)
    {
        JsonElement items = (await feature.GetAsync("/api/quality/incidents")).Body
            .GetProperty("data")
            .GetProperty("items");

        return [.. items.EnumerateArray().Select(item => item.GetProperty("classification").GetString()!)];
    }

    private static async Task<IReadOnlyList<(string Stream, string Class)>> RefusalsAsync(QualityFeature feature)
    {
        JsonElement items = (await feature.GetAsync("/api/quality/incidents")).Body
            .GetProperty("data")
            .GetProperty("items");

        return
        [
            .. items.EnumerateArray().Select(item => (
                Stream: item.GetProperty("subjectKey").GetString()!,
                Class: item.GetProperty("classification").GetString()!)),
        ];
    }
}
