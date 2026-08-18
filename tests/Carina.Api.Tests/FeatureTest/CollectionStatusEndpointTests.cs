using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class CollectionStatusEndpointTests
{
    private static readonly DateTime At = new(2026, 8, 18, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AStreamNeverVisitedIsListedWithNothingRecordedAgainstIt()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049, 1050])]);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/epg/collection-status");
        JsonElement only = Assert.Single(body.GetProperty("data").GetProperty("streams").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(4, only.GetProperty("networkId").GetInt32());
        Assert.Equal(32_736, only.GetProperty("transportStreamId").GetInt32());
        Assert.Equal("neverVisited", only.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, only.GetProperty("lastCompletedAt").ValueKind);
        Assert.Equal([1049, 1050], only.GetProperty("serviceIds").EnumerateArray().Select(id => id.GetInt32()));
    }

    [Fact]
    public async Task AStreamThatWasVisitedCarriesWhatTheLedgerSays()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049])]);

        await feature.Visits.SaveAsync(
            StreamVisit.Record(
                new NetworkId(4),
                new TransportStreamId(32_736),
                VisitOutcome.BasicOnly,
                At,
                TimeSpan.FromSeconds(42)),
            CancellationToken.None);

        (_, JsonElement body) = await feature.GetAsync("/api/epg/collection-status");
        JsonElement only = Assert.Single(body.GetProperty("data").GetProperty("streams").EnumerateArray());

        Assert.Equal("basicOnly", only.GetProperty("outcome").GetString());
        Assert.Equal(42_000, only.GetProperty("lastDurationMilliseconds").GetInt32());
        Assert.Equal(At, only.GetProperty("lastCompletedAt").GetDateTimeOffset().UtcDateTime);
        Assert.NotEqual(JsonValueKind.Null, only.GetProperty("notBefore").ValueKind);
    }

    [Fact]
    public async Task AStandingRescanNoticeIsListedAlongsideTheStreams()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049])]);

        feature.Board().Post([
            new RescanHint(
                new NetworkId(4),
                new TransportStreamId(32_736),
                RescanReason.ServicesAppeared,
                [new ServiceId(1050)]),
        ]);

        (_, JsonElement body) = await feature.GetAsync("/api/epg/collection-status");
        JsonElement only = Assert.Single(body.GetProperty("data").GetProperty("rescans").EnumerateArray());

        Assert.Equal("servicesAppeared", only.GetProperty("reason").GetString());
        Assert.Equal([1050], only.GetProperty("serviceIds").EnumerateArray().Select(id => id.GetInt32()));
    }

    private static BroadcastStream Stream(int network, int stream, IReadOnlyList<int> services)
        => new(
            new NetworkId(network),
            new TransportStreamId(stream),
            TuningParameters.Terrestrial(22),
            [.. services.Select(service => new ServiceId(service))]);
}
