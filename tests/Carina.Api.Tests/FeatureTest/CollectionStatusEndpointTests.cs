using System.Net;
using System.Text.Json;

using Carina.Api.Tests.Unit;
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

    [Fact]
    public async Task AStreamNoTuningEverReachedIsListedWithoutATransportStreamId()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049])]);

        feature.Streams.Unreachable.Add(new IntendedStream(
            new NetworkId(5),
            null,
            TuningParameters.Terrestrial(30),
            [new ServiceId(2049), new ServiceId(2050)],
            new StreamReach(RotationState.BackingOff, 2, At.AddHours(4), null)));

        (_, JsonElement body) = await feature.GetAsync("/api/epg/collection-status");
        JsonElement[] streams = [.. body.GetProperty("data").GetProperty("streams").EnumerateArray()];
        JsonElement unreached = streams[1];

        Assert.Equal(2, streams.Length);
        Assert.Equal(5, unreached.GetProperty("networkId").GetInt32());
        Assert.Equal(JsonValueKind.Null, unreached.GetProperty("transportStreamId").ValueKind);
        Assert.Equal("neverVisited", unreached.GetProperty("outcome").GetString());
        Assert.Equal(30, unreached.GetProperty("tuning").GetProperty("physicalChannel").GetInt32());
        Assert.Equal([2049, 2050], unreached.GetProperty("serviceIds").EnumerateArray().Select(id => id.GetInt32()));
    }

    [Fact]
    public async Task TheSharedBackOffIsToldApartFromTheCollectorsOwn()
    {
        await using var feature = new EpgFeature([]);

        feature.Streams.Unreachable.Add(new IntendedStream(
            new NetworkId(5),
            null,
            TuningParameters.Terrestrial(30),
            [new ServiceId(2049)],
            new StreamReach(RotationState.BackingOff, 2, At.AddHours(4), null)));

        (_, JsonElement body) = await feature.GetAsync("/api/epg/collection-status");
        JsonElement only = Assert.Single(body.GetProperty("data").GetProperty("streams").EnumerateArray());

        Assert.Equal(JsonValueKind.Null, only.GetProperty("notBefore").ValueKind);
        Assert.Equal("backingOff", only.GetProperty("rotation").GetProperty("state").GetString());
        Assert.Equal(2, only.GetProperty("rotation").GetProperty("consecutiveFailures").GetInt32());
        Assert.Equal(
            At.AddHours(4),
            only.GetProperty("rotation").GetProperty("nextAttemptAt").GetDateTimeOffset().UtcDateTime);
    }

    [Fact]
    public async Task AStreamStillWalkingTheRotationSaysSoWhileItsOwnBackOffStands()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049])]);

        await feature.Visits.SaveAsync(
            StreamVisit.Record(
                new NetworkId(4),
                new TransportStreamId(32_736),
                VisitOutcome.Incomplete,
                At,
                TimeSpan.FromSeconds(182)),
            CancellationToken.None);

        (_, JsonElement body) = await feature.GetAsync("/api/epg/collection-status");
        JsonElement only = Assert.Single(body.GetProperty("data").GetProperty("streams").EnumerateArray());

        Assert.Equal("active", only.GetProperty("rotation").GetProperty("state").GetString());
        Assert.Equal(0, only.GetProperty("rotation").GetProperty("consecutiveFailures").GetInt32());
        Assert.Equal(1, only.GetProperty("consecutiveIncomplete").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, only.GetProperty("notBefore").ValueKind);
    }

    [Fact]
    public async Task CoverageIsAFactOfItsOwnApartFromTheCompletionStreak()
    {
        await using var feature = new EpgFeature(
            [Stream(4, 32_736, [1049, 1050])],
            collection: Wanting,
            clock: new FixedTimeProvider(At));

        feature.Programmes.Programmes.Add(ProgrammeStartingAt(4, 1049, At.AddDays(8)));
        feature.Programmes.Programmes.Add(ProgrammeStartingAt(4, 1050, At.AddHours(20)));

        await feature.Visits.SaveAsync(
            StreamVisit.Record(
                new NetworkId(4),
                new TransportStreamId(32_736),
                VisitOutcome.Incomplete,
                At,
                TimeSpan.FromSeconds(182)),
            CancellationToken.None);

        (_, JsonElement body) = await feature.GetAsync("/api/epg/collection-status");
        JsonElement only = Assert.Single(body.GetProperty("data").GetProperty("streams").EnumerateArray());
        JsonElement[] coverage = [.. only.GetProperty("coverage").EnumerateArray()];

        Assert.Equal(72, body.GetProperty("data").GetProperty("wantedCoverageHours").GetInt32());
        Assert.Equal(1, only.GetProperty("consecutiveIncomplete").GetInt32());
        Assert.Equal([1049, 1050], coverage.Select(service => service.GetProperty("serviceId").GetInt32()));
        Assert.Equal([true, false], coverage.Select(service => service.GetProperty("meetsWantedCoverage").GetBoolean()));
        Assert.Equal(
            At.AddDays(8),
            coverage[0].GetProperty("coveredUntil").GetDateTimeOffset().UtcDateTime);
    }

    [Fact]
    public async Task AServiceWithNoProgrammeAtAllReportsNoCoverage()
    {
        await using var feature = new EpgFeature(
            [Stream(4, 32_736, [1049])],
            collection: Wanting,
            clock: new FixedTimeProvider(At));

        (_, JsonElement body) = await feature.GetAsync("/api/epg/collection-status");
        JsonElement only = Assert.Single(body.GetProperty("data").GetProperty("streams").EnumerateArray());
        JsonElement coverage = Assert.Single(only.GetProperty("coverage").EnumerateArray());

        Assert.Equal(JsonValueKind.Null, coverage.GetProperty("coveredUntil").ValueKind);
        Assert.False(coverage.GetProperty("meetsWantedCoverage").GetBoolean());
    }

    [Fact]
    public async Task TheLastVisitsTallySaysWhichServiceCameBackShort()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049])]);
        StreamVisit visit = StreamVisit.Record(
            new NetworkId(4),
            new TransportStreamId(32_736),
            VisitOutcome.Incomplete,
            At,
            TimeSpan.FromSeconds(182));

        visit.Tallied(
        [
            VisitTally.Rehydrate(
                new NetworkId(4),
                new TransportStreamId(32_736),
                new ServiceId(1049),
                80,
                87,
                32,
                31,
                248,
                240,
                3),
        ]);

        await feature.Visits.SaveAsync(visit, CancellationToken.None);

        (_, JsonElement body) = await feature.GetAsync("/api/epg/collection-status");
        JsonElement only = Assert.Single(body.GetProperty("data").GetProperty("streams").EnumerateArray());
        JsonElement counted = Assert.Single(only.GetProperty("tally").EnumerateArray());

        Assert.Equal(1049, counted.GetProperty("serviceId").GetInt32());
        Assert.Equal(80, counted.GetProperty("tableId").GetInt32());
        Assert.Equal(87, counted.GetProperty("lastTableId").GetInt32());
        Assert.Equal(32, counted.GetProperty("segmentsDeclared").GetInt32());
        Assert.Equal(31, counted.GetProperty("segmentsHeard").GetInt32());
        Assert.Equal(248, counted.GetProperty("sectionsDeclared").GetInt32());
        Assert.Equal(240, counted.GetProperty("sectionsHeard").GetInt32());
        Assert.Equal(3, counted.GetProperty("versionChanges").GetInt32());
    }

    private static readonly CollectionSettings Wanting = new() { WantedCoverage = TimeSpan.FromDays(3) };

    private static Programme ProgrammeStartingAt(int network, int service, DateTime startsAt)
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(network), new ServiceId(service), new EventId(1)),
            new TransportStreamId(32_736),
            startsAt,
            startsAt.AddHours(1),
            "name",
            string.Empty,
            false,
            startsAt);

    private static BroadcastStream Stream(int network, int stream, IReadOnlyList<int> services)
        => new(
            new NetworkId(network),
            new TransportStreamId(stream),
            TuningParameters.Terrestrial(22),
            [.. services.Select(service => new ServiceId(service))]);
}
