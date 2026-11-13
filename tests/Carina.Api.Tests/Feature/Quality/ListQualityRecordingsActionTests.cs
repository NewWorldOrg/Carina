using System.Net;
using System.Text.Json;

using Carina.Api.Common;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.Feature.Quality;

[Trait("Category", "Feature")]
public sealed class ListQualityRecordingsActionTests
{
    [Fact(DisplayName = "an ordering outside the list is refused")]
    public async Task AnOrderingOutsideTheListIsRefused()
    {
        await using var feature = new QualityFeature();

        Assert.Equal(HttpStatusCode.BadRequest, (await feature.GetAsync("/api/quality/recordings?sort=99")).Status);
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
        Assert.Equal(bad.Recording.Wire, data.GetProperty("items")[0].GetProperty("id").GetString());
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

    [Fact(DisplayName = "the recordings list spells a recording id the way the recording endpoints read it")]
    public async Task TheRecordingsListSpellsARecordingIdAsTheRecordingEndpointsReadIt()
    {
        await using var feature = new QualityFeature();
        QualityLedgerRow bad = feature.Recorded(dropped: 2_000, total: 1_000_000);

        JsonElement item = Assert.Single(
            (await feature.GetAsync("/api/quality/recordings")).Body.GetProperty("data").GetProperty("items").EnumerateArray());

        RecordingId? read = RecordingIdText.Read(item.GetProperty("id").GetString());

        Assert.Equal(bad.Recording, read);
    }

    [Fact(DisplayName = "a recording clean on every measure but holding a gap is listed at the warning level")]
    public async Task ARecordingHoldingAGapIsListedAtTheWarningLevel()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(dropped: 0, total: 1_000_000);
        QualityLedgerRow gapped = feature.Recorded(dropped: 0, total: 1_000_000, gaps: 2, missedMs: 3_800);

        JsonElement data = (await feature.GetAsync("/api/quality/recordings")).Body.GetProperty("data");
        JsonElement item = Assert.Single(data.GetProperty("items").EnumerateArray());

        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal(gapped.Recording.Wire, item.GetProperty("id").GetString());
        Assert.Equal("warning", item.GetProperty("standing").GetString());
        Assert.All(
            item.GetProperty("verdicts").EnumerateArray(),
            verdict => Assert.Equal("good", verdict.GetProperty("standing").GetString()));
        Assert.Equal("warning", item.GetProperty("gap").GetProperty("standing").GetString());
        Assert.Equal(2, item.GetProperty("gap").GetProperty("count").GetInt32());
        Assert.Equal(3_800, item.GetProperty("gap").GetProperty("missedMs").GetInt64());
    }

    [Fact(DisplayName = "a recording listed for what it lost says it holds no gap")]
    public async Task ARecordingListedForWhatItLostSaysItHoldsNoGap()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(dropped: 300, total: 1_000_000);

        JsonElement gap = (await feature.GetAsync("/api/quality/recordings")).Body
            .GetProperty("data")
            .GetProperty("items")[0]
            .GetProperty("gap");

        Assert.Equal("good", gap.GetProperty("standing").GetString());
        Assert.Equal(0, gap.GetProperty("count").GetInt32());
        Assert.Equal(0, gap.GetProperty("missedMs").GetInt64());
    }

    [Fact(DisplayName = "a recording nothing counted is not listed for the gap it holds")]
    public async Task ARecordingNothingCountedIsNotListedForTheGapItHolds()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(dropped: null, total: null, scrambled: null, gaps: 1, missedMs: 1_200);

        JsonElement data = (await feature.GetAsync("/api/quality/recordings")).Body.GetProperty("data");

        Assert.Equal(0, data.GetProperty("total").GetInt32());
        Assert.Equal(1, Measure(data, "packetsLost", "whole").GetProperty("unmeasured").GetInt32());
    }

    [Fact(DisplayName = "a gap is listed whichever measure the list was asked by")]
    public async Task AGapIsListedWhicheverMeasureTheListWasAskedBy()
    {
        await using var feature = new QualityFeature();
        feature.Recorded(dropped: 0, total: 1_000_000, gaps: 1, missedMs: 500);

        JsonElement item = Assert.Single(
            (await feature.GetAsync("/api/quality/recordings?metric=overflows")).Body
                .GetProperty("data")
                .GetProperty("items")
                .EnumerateArray());

        Assert.Equal(1, item.GetProperty("verdicts").GetArrayLength());
        Assert.Equal("warning", item.GetProperty("gap").GetProperty("standing").GetString());
    }

    [Fact(DisplayName = "a carried shelf reads unmeasured on the recordings list")]
    public async Task ACarriedShelfReadsUnmeasuredOnTheRecordingsList()
    {
        await using var feature = new QualityFeature();
        string period = CarriedShelf.Shelved(feature);

        JsonElement beyond = await CarriedShelf.DataAsync(feature, $"/api/quality/recordings?{period}&sort=worst");

        Assert.Equal(0, beyond.GetProperty("total").GetInt32());
        Assert.Equal(0, beyond.GetProperty("items").GetArrayLength());
        CarriedShelf.ReadsUnmeasuredThroughout(beyond.GetProperty("whole"), CarriedShelf.Shelf);
    }

    private static JsonElement Measure(JsonElement data, string metric, string under = "measures")
        => data.GetProperty(under).EnumerateArray()
            .Single(read => read.GetProperty("metric").GetString() == metric)
            .GetProperty("reading");
}
