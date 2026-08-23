using System.Globalization;
using System.Net;
using System.Text.Json;

using Carina.Api.Events;
using Carina.Broadcast.Descriptors;
using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class SyntheticEpgCollectionTests
{
    private const int Network = 4;
    private const int Stream = 32_736;
    private const int Television = 1049;
    private const int SecondTelevision = 1050;
    private const int DataService = 1088;
    private const int OneSegSimulcast = 1080;

    private const string ADay = "?type=isdbT&from=2026-09-01T00:00:00Z&to=2026-09-02T00:00:00Z";

    private static readonly DateTimeOffset Airs = new(2026, 9, 1, 21, 0, 0, TimeSpan.FromHours(9));

    private static readonly TuningParameters Channel = TuningParameters.Terrestrial(22);

    [Fact]
    public async Task ACollectedGuideServesItsColumnsAndKeepsEveryServiceInTheStore()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.Carrying((Guide() with { CorruptSections = 2 }).ToBytes()));

        await using var feature = new EpgFeature([OnAir()], driver);

        await CollectAsync(feature);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/programs{ADay}");
        JsonElement data = body.GetProperty("data");
        string?[] served = [.. data.GetProperty("programmes").EnumerateArray()
            .Select(programme => programme.GetProperty("id").GetString())];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [Television, SecondTelevision],
            data.GetProperty("services").EnumerateArray().Select(service => service.GetProperty("serviceId").GetInt32()));
        Assert.Contains($"{Network}-{Television}-1", served);
        Assert.Contains($"{Network}-{SecondTelevision}-7", served);
        Assert.DoesNotContain($"{Network}-{DataService}-5", served);
        Assert.Contains(
            feature.Programmes.Programmes,
            held => held.ServiceId.Value == DataService && held.EventId.Value == 5);

        (_, JsonElement ledger) = await feature.GetAsync("/api/epg/collection-status");
        JsonElement visited = Assert.Single(ledger.GetProperty("data").GetProperty("streams").EnumerateArray());

        Assert.Equal("complete", visited.GetProperty("outcome").GetString());
        Assert.NotEqual(JsonValueKind.Null, visited.GetProperty("lastCompletedAt").ValueKind);
    }

    [Fact]
    public async Task ACollectedProgrammeOpensAloneWithItsOpenEndAndItsRelay()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.Carrying(Guide().ToBytes()));

        await using var feature = new EpgFeature([OnAir()], driver);

        await CollectAsync(feature);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/programs/{Network}-{SecondTelevision}-7");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Night Handover", data.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("endsAt").ValueKind);
        Assert.Equal("scheduleBasic", data.GetProperty("source").GetString());

        JsonElement relay = Assert.Single(data.GetProperty("related").EnumerateArray());

        Assert.Equal(Television, relay.GetProperty("serviceId").GetInt32());
        Assert.Equal(11, relay.GetProperty("eventId").GetInt32());
        Assert.Equal("relayed", relay.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task ASearchWithCombinedConditionsFindsExactlyWhatMatches()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.Carrying(Guide().ToBytes()));

        await using var feature = new EpgFeature([OnAir()], driver);

        await CollectAsync(feature);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            "/api/programs/search?keyword=Bulletin&from=2026-08-31T00:00:00Z&to=2026-09-02T00:00:00Z&perPage=10");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal($"{Network}-{Television}-1", data.GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task AHarvestMissingASegmentIsNeverRecordedAsASuccess()
    {
        var driver = new ScriptedDriverClient();
        byte[] shortfall = (Guide() with
        {
            Services = [Missing(Television, "Synthetic One")],
        }).ToBytes();

        driver.Script(Channel, new ChannelScript { Paced = () => Stalling(shortfall) });

        await using var feature = new EpgFeature(
            [OnAir()],
            driver,
            new CollectionSettings { LongestVisit = TimeSpan.FromMilliseconds(300) });

        await CollectAsync(feature);

        (_, JsonElement body) = await feature.GetAsync("/api/epg/collection-status");
        JsonElement visited = Assert.Single(body.GetProperty("data").GetProperty("streams").EnumerateArray());

        Assert.Equal("incomplete", visited.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, visited.GetProperty("lastCompletedAt").ValueKind);
        Assert.Equal(1, visited.GetProperty("consecutiveIncomplete").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, visited.GetProperty("notBefore").ValueKind);
    }

    [Fact]
    public async Task OnlyATuningFailureIsReportedAgainstTheTunedChannel()
    {
        var unreachable = new CandidateChannelId(Guid.NewGuid());
        var starved = new CandidateChannelId(Guid.NewGuid());
        TuningParameters second = TuningParameters.Terrestrial(24);
        var driver = new ScriptedDriverClient();
        byte[] shortfall = (Guide() with
        {
            NetworkId = Network + 1,
            Services = [Missing(SecondTelevision, "Synthetic Two")],
        }).ToBytes();

        driver.Script(Channel, ChannelScript.NoLock());
        driver.Script(second, new ChannelScript { Paced = () => Stalling(shortfall) });

        await using var feature = new EpgFeature(
            [
                OnAir() with { TunedWith = unreachable },
                new BroadcastStream(
                    new NetworkId(Network + 1),
                    new TransportStreamId(Stream + 1),
                    second,
                    [new ServiceId(SecondTelevision)])
                {
                    TunedWith = starved,
                },
            ],
            driver,
            new CollectionSettings { LongestVisit = TimeSpan.FromMilliseconds(300) });
        DateTime at = DateTime.UtcNow;

        feature.Candidates.Candidates.Add(CandidateChannel.Discover(
            unreachable,
            new NetworkId(Network),
            new ServiceId(Television),
            Channel,
            at));
        feature.Candidates.Candidates.Add(CandidateChannel.Discover(
            starved,
            new NetworkId(Network + 1),
            new ServiceId(SecondTelevision),
            second,
            at));

        await CollectAsync(feature);

        CandidateChannel blamed = feature.Candidates.Candidates
            .Single(candidate => candidate.Id.Equals(unreachable));
        CandidateChannel spared = feature.Candidates.Candidates
            .Single(candidate => candidate.Id.Equals(starved));

        Assert.Equal(1, blamed.ConsecutiveFailures);
        Assert.Equal(0, spared.ConsecutiveFailures);
        Assert.True(spared.IsInRotation);

        (_, JsonElement body) = await feature.GetAsync("/api/epg/collection-status");
        JsonElement held = body.GetProperty("data").GetProperty("streams").EnumerateArray()
            .Single(stream => stream.GetProperty("networkId").GetInt32() == Network + 1);

        Assert.Equal("incomplete", held.GetProperty("outcome").GetString());
        Assert.NotEqual(JsonValueKind.Null, held.GetProperty("notBefore").ValueKind);
    }

    [Fact]
    public async Task ARebuildDiscardsTheGuideAndAFreshCollectionRestoresIt()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.Carrying(Guide().ToBytes()));

        await using var feature = new EpgFeature(
            [OnAir()],
            driver,
            new CollectionSettings { BetweenBoosts = TimeSpan.Zero, BetweenVisits = TimeSpan.Zero });

        await CollectAsync(feature);

        Assert.NotEmpty(feature.Programmes.Programmes);

        (HttpStatusCode rebuilt, _) = await feature.PostAsync(
            "/api/epg/rebuild",
            new { confirm = "discard-everything" });

        Assert.Equal(HttpStatusCode.OK, rebuilt);
        Assert.Equal(1, feature.Programmes.Wiped);
        Assert.Empty(feature.Programmes.Programmes);

        await CollectAsync(feature);

        (_, JsonElement body) = await feature.GetAsync($"/api/programs{ADay}");
        string?[] served = [.. body.GetProperty("data").GetProperty("programmes").EnumerateArray()
            .Select(programme => programme.GetProperty("id").GetString())];

        Assert.Contains($"{Network}-{Television}-1", served);
        Assert.Equal(
            [Television, SecondTelevision],
            Assert.Single(await feature.Streams.ListAsync(CancellationToken.None))
                .Services.Select(service => service.Value));

        using HttpResponseMessage stale = await feature.Client.GetAsync(
            new Uri("/api/programs/bulk?cursor=1:0", UriKind.Relative));

        Assert.Equal("{\"op\":\"reset\"}", (await stale.Content.ReadAsStringAsync()).Trim());
        Assert.StartsWith(
            "2:",
            stale.Headers.GetValues(ProgrammeFeedStream.CursorHeader).First(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACollectionAnnouncesItselfOnTheOpenEventStream()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.Carrying(Guide().ToBytes()));

        await using var feature = new EpgFeature([OnAir()], driver);

        using HttpResponseMessage listening = await feature.Client.GetAsync(
            new Uri(AppEventStream.Path, UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);
        await using Stream open = await listening.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(open);

        await CollectAsync(feature);

        var arrived = new List<string>();
        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (!arrived.Contains("event: programs") || !arrived.Contains("event: epgCollection"))
        {
            string? line = await reader.ReadLineAsync(patience.Token);

            if (line is { Length: > 0 })
            {
                arrived.Add(line);
            }
        }

        Assert.Contains("event: programs", arrived);
        Assert.Contains("event: epgCollection", arrived);
    }

    [Fact]
    public async Task WhatTheGuideLeavesOutOfItsColumnsIsStillHeldInTheStore()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.Carrying(GuideCarryingAPortableSimulcast().ToBytes()));

        await using var feature = new EpgFeature([EverythingTheStreamCarries()], driver);

        feature.Catalogue.Services.Add(Catalogued(Television, ServiceCategory.Television));
        feature.Catalogue.Services.Add(Catalogued(SecondTelevision, ServiceCategory.Television));
        feature.Catalogue.Services.Add(Catalogued(OneSegSimulcast, ServiceCategory.OneSeg));
        feature.Catalogue.Services.Add(Catalogued(DataService, ServiceCategory.Data));

        await CollectAsync(feature);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/programs{ADay}");
        JsonElement data = body.GetProperty("data");
        string?[] served = [.. data.GetProperty("programmes").EnumerateArray()
            .Select(programme => programme.GetProperty("id").GetString())];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [Television, SecondTelevision],
            data.GetProperty("services").EnumerateArray().Select(service => service.GetProperty("serviceId").GetInt32()));
        Assert.Contains($"{Network}-{Television}-1", served);
        Assert.DoesNotContain($"{Network}-{OneSegSimulcast}-3", served);
        Assert.DoesNotContain($"{Network}-{DataService}-5", served);
        Assert.DoesNotContain($"{Network}-{Television}-9", served);

        Assert.Contains(
            feature.Programmes.Programmes,
            held => held.ServiceId.Value == OneSegSimulcast && held.EventId.Value == 3);
        Assert.Contains(
            feature.Programmes.Programmes,
            held => held.ServiceId.Value == DataService && held.EventId.Value == 5);
        Assert.Contains(
            feature.Programmes.Programmes,
            held => held.ServiceId.Value == Television && held.EventId.Value == 9 && held.IsShadow);

        (_, JsonElement skeleton) = await feature.GetAsync($"/api/programs/{Network}-{Television}-9");

        Assert.True(skeleton.GetProperty("data").GetProperty("isShadow").GetBoolean());
    }

    [Fact]
    public async Task AServiceTheCatalogueHasNoOpinionOnIsNotWithheld()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.Carrying(GuideCarryingAPortableSimulcast().ToBytes()));

        await using var feature = new EpgFeature([EverythingTheStreamCarries()], driver);

        await CollectAsync(feature);

        (_, JsonElement body) = await feature.GetAsync($"/api/programs{ADay}");

        Assert.Equal(
            [Television, SecondTelevision, OneSegSimulcast, DataService],
            body.GetProperty("data").GetProperty("services").EnumerateArray()
                .Select(service => service.GetProperty("serviceId").GetInt32()));
    }

    [Fact]
    public async Task AColumnThatALaterScanWithdrawsIsNotAnsweredFromACachedGuide()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.Carrying(GuideCarryingAPortableSimulcast().ToBytes()));

        await using var feature = new EpgFeature([EverythingTheStreamCarries()], driver);
        BroadcastService portable = Catalogued(OneSegSimulcast, ServiceCategory.Television);

        feature.Catalogue.Services.Add(Catalogued(Television, ServiceCategory.Television));
        feature.Catalogue.Services.Add(Catalogued(SecondTelevision, ServiceCategory.Television));
        feature.Catalogue.Services.Add(portable);
        feature.Catalogue.Services.Add(Catalogued(DataService, ServiceCategory.Television));

        await CollectAsync(feature);

        using HttpResponseMessage held = await feature.Client.GetAsync(
            new Uri($"/api/programs{ADay}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, held.StatusCode);

        portable.Describe("Synthetic", ServiceCategory.OneSeg, DateTime.UtcNow.AddMinutes(1));

        using var asking = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"/api/programs{ADay}", UriKind.Relative));

        asking.Headers.IfNoneMatch.Add(held.Headers.ETag!);

        using HttpResponseMessage answered = await feature.Client.SendAsync(asking);

        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);

        using JsonDocument served = JsonDocument.Parse(await answered.Content.ReadAsStringAsync());

        Assert.Equal(
            [Television, SecondTelevision, DataService],
            served.RootElement.GetProperty("data").GetProperty("services").EnumerateArray()
                .Select(service => service.GetProperty("serviceId").GetInt32()));
    }

    [Fact]
    public async Task AGuideGatheredOverEightDaysIsStillReadableOnItsEighthDay()
    {
        var driver = new ScriptedDriverClient();
        DateTimeOffset opens = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9));

        driver.Script(Channel, ChannelScript.Carrying(OverEightDays(opens.AddHours(2)).ToBytes()));

        await using var feature = new EpgFeature([OnlyTheOneService()], driver);

        feature.Catalogue.Services.Add(Catalogued(Television, ServiceCategory.Television));

        await CollectAsync(feature);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/programs?type=isdbT&from={Stamp(opens.AddDays(8))}&to={Stamp(opens.AddDays(9))}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [$"{Network}-{Television}-9"],
            body.GetProperty("data").GetProperty("programmes").EnumerateArray()
                .Select(programme => programme.GetProperty("id").GetString()));

        (_, JsonElement ledger) = await feature.GetAsync("/api/epg/collection-status");
        JsonElement reported = ledger.GetProperty("data");
        JsonElement coverage = Assert.Single(
            Assert.Single(reported.GetProperty("streams").EnumerateArray())
                .GetProperty("coverage").EnumerateArray());

        Assert.Equal(192, reported.GetProperty("wantedCoverageHours").GetInt32());
        Assert.True(coverage.GetProperty("meetsWantedCoverage").GetBoolean());
    }

    private static string Stamp(DateTimeOffset at)
        => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static BroadcastService Catalogued(int serviceId, ServiceCategory category)
        => BroadcastService.Discover(
            new NetworkId(Network),
            new ServiceId(serviceId),
            "Synthetic",
            category,
            DateTime.UtcNow);

    private static SyntheticGuide OverEightDays(DateTimeOffset opens)
        => new()
        {
            NetworkId = Network,
            TransportStreamId = Stream,
            Services =
            [
                new SyntheticGuideService(Television, "Synthetic One")
                {
                    Programmes =
                    [
                        .. Enumerable.Range(0, 9).Select(day => new SyntheticProgramme(
                            day + 1,
                            opens.AddDays(day),
                            TimeSpan.FromHours(1))
                        {
                            Name = $"Day {day} Bulletin",
                        }),
                    ],
                },
            ],
        };

    private static SyntheticGuide GuideCarryingAPortableSimulcast()
        => Guide() with
        {
            Services =
            [
                .. Guide().Services,
                new SyntheticGuideService(OneSegSimulcast, "Synthetic One (portable)")
                {
                    Programmes =
                    [
                        new SyntheticProgramme(3, Airs, TimeSpan.FromMinutes(30)) { Name = "Evening Bulletin" },
                    ],
                },
            ],
        };

    private static SyntheticGuide Guide()
        => new()
        {
            NetworkId = Network,
            TransportStreamId = Stream,
            Services =
            [
                new SyntheticGuideService(Television, "Synthetic One")
                {
                    Programmes =
                    [
                        new SyntheticProgramme(1, Airs, TimeSpan.FromMinutes(30)) { Name = "Evening Bulletin" },
                        new SyntheticProgramme(9, Airs.AddMinutes(30), TimeSpan.FromMinutes(30))
                        {
                            SharedWith = [(SecondTelevision, 7)],
                        },
                    ],
                },
                new SyntheticGuideService(SecondTelevision, "Synthetic Two")
                {
                    Programmes =
                    [
                        new SyntheticProgramme(7, Airs.AddMinutes(30), null)
                        {
                            Name = "Night Handover",
                            RelaysTo = [(Television, 11)],
                        },
                    ],
                },
                new SyntheticGuideService(DataService, "Synthetic Carousel")
                {
                    Kind = ServiceKind.Data,
                    Programmes =
                    [
                        new SyntheticProgramme(5, Airs, TimeSpan.FromHours(1)) { Name = "Data Carousel" },
                    ],
                },
            ],
        };

    private static SyntheticGuideService Missing(int serviceId, string name)
        => new(serviceId, name)
        {
            Programmes = [new SyntheticProgramme(1, Airs, TimeSpan.FromMinutes(30)) { Name = "Cut Short" }],
            MissingSegment = true,
        };

    private static BroadcastStream OnAir()
        => new(
            new NetworkId(Network),
            new TransportStreamId(Stream),
            Channel,
            [new ServiceId(Television), new ServiceId(SecondTelevision)]);

    private static BroadcastStream EverythingTheStreamCarries()
        => OnAir() with
        {
            Services =
            [
                new ServiceId(Television),
                new ServiceId(SecondTelevision),
                new ServiceId(OneSegSimulcast),
                new ServiceId(DataService),
            ],
        };

    private static BroadcastStream OnlyTheOneService()
        => OnAir() with { Services = [new ServiceId(Television)] };

    private static PacedStream Stalling(byte[] bytes)
    {
        PacedStream paced = PacedStream.InChunksOf(bytes, bytes.Length);

        paced.Allow(1);

        return paced;
    }

    private static async Task CollectAsync(EpgFeature feature)
    {
        (HttpStatusCode status, _) = await feature.PostAsync("/api/epg/collect-now");

        Assert.Equal(HttpStatusCode.Accepted, status);

        await feature.CollectionSettled();
    }
}
