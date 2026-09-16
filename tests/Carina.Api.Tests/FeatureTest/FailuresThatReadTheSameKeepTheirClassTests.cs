using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Quality;

namespace Carina.Api.Tests.FeatureTest;

public sealed class FailuresThatReadTheSameKeepTheirClassTests
{
    private const int ManyFailuresThatReadTheSame = 2_048;

    private static readonly IReadOnlyList<TuneFailureKind> TheFourClasses =
    [
        TuneFailureKind.NoLock,
        TuneFailureKind.NoData,
        TuneFailureKind.IncompletePsi,
        TuneFailureKind.StreamMismatch,
    ];

    private static readonly IReadOnlyList<string> TheTwoStreams = ["broadcast-satellite", "communications-satellite"];

    [Fact(DisplayName = "BR-QD-008: thousands of failures that read the same keep the four classes they were refused in")]
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

    [Fact(DisplayName = "BR-QD-008: the streams those failures were refused on stay tellable apart from one another")]
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

    [Fact(DisplayName = "BR-QD-002: none of those failures is counted among this domain's own")]
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

    private static void Refused(QualityFeature feature)
    {
        for (int at = 0; at < ManyFailuresThatReadTheSame; at++)
        {
            TuneFailureKind refusedIn = TheFourClasses[at % TheFourClasses.Count];
            string stream = TheTwoStreams[at / TheFourClasses.Count % TheTwoStreams.Count];
            DateTime detectedAt = QualityFeature.Noon.AddMinutes(-1);

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
