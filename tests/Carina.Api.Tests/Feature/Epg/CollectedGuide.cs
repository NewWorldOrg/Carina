using System.Globalization;
using System.Net;
using System.Text.Json;

using Carina.Broadcast.Descriptors;
using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.TestSupport;

namespace Carina.Api.Tests.Feature.Epg;

/// <summary>A synthetic broadcast whose guide the collector gathers, and the features that collect it.</summary>
internal static class CollectedGuide
{
    public const int Network = 4;
    public const int Stream = 32_736;
    public const int Television = 1049;
    public const int SecondTelevision = 1050;
    public const int DataService = 1088;
    public const int OneSegSimulcast = 1080;
    public const int WithdrawnService = 1091;

    public const string ADay = "?type=isdbT&from=2026-09-01T00:00:00Z&to=2026-09-02T00:00:00Z";

    public const string TheDay = "from=2026-09-01T00:00:00Z&to=2026-09-02T00:00:00Z";

    public static readonly DateTimeOffset Airs = new(2026, 9, 1, 21, 0, 0, TimeSpan.FromHours(9));

    public static readonly DateTimeOffset ScheduleStarts = new(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(9));

    public static readonly TuningParameters Channel = TuningParameters.Terrestrial(22);

    public static ArchivedProgramme Archived(int serviceId, int eventId, string name)
        => ArchivedProgramme.Rehydrate(
            new NetworkId(Network),
            new ServiceId(serviceId),
            new EventId(eventId),
            Airs.UtcDateTime,
            Airs.UtcDateTime.AddMinutes(30),
            name,
            string.Empty,
            false,
            [],
            [],
            Airs.UtcDateTime.AddDays(1));

    public static string Stamp(DateTimeOffset at)
        => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    public static BroadcastService Catalogued(int serviceId, ServiceCategory category)
        => BroadcastService.Discover(
            new NetworkId(Network),
            new ServiceId(serviceId),
            "Synthetic",
            category,
            DateTime.UtcNow);

    public static async Task<JsonElement> CollectionStatusAsync(EpgFeature feature)
    {
        (HttpStatusCode status, JsonElement ledger) = await feature.GetAsync("/api/epg/collection-status");

        Assert.Equal(HttpStatusCode.OK, status);

        return ledger.GetProperty("data");
    }

    public static JsonElement OnlyCoverage(JsonElement reported)
        => Assert.Single(
            Assert.Single(reported.GetProperty("streams").EnumerateArray())
                .GetProperty("coverage").EnumerateArray());

    public static SyntheticGuide OverEightDays(DateTimeOffset scheduleStarts, int days = 8)
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
                        .. Enumerable.Range(0, days).Select(day => new SyntheticProgramme(
                            day + 1,
                            scheduleStarts.AddDays(day).AddHours(23),
                            TimeSpan.FromHours(1))
                        {
                            Name = $"Day {day} Bulletin",
                        }),
                    ],
                },
            ],
        };

    public static SyntheticGuide GuideCarryingAPortableSimulcast()
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

    public static SyntheticGuide Guide()
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
                        new SyntheticProgramme(5, Airs, TimeSpan.FromHours(1)) { Name = "Evening Carousel" },
                    ],
                },
            ],
        };

    public static SyntheticGuideService Missing(int serviceId, string name)
        => new(serviceId, name)
        {
            Programmes = [new SyntheticProgramme(1, Airs, TimeSpan.FromMinutes(30)) { Name = "Cut Short" }],
            MissingSegment = true,
        };

    public static BroadcastStream OnAir()
        => new(
            new NetworkId(Network),
            new TransportStreamId(Stream),
            Channel,
            [new ServiceId(Television), new ServiceId(SecondTelevision)]);

    public static BroadcastStream EverythingTheStreamCarries()
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

    public static BroadcastStream OnlyTheOneService()
        => OnAir() with { Services = [new ServiceId(Television)] };

    public static PacedStream Stalling(byte[] bytes)
    {
        PacedStream paced = PacedStream.InChunksOf(bytes, bytes.Length);

        paced.Allow(1);

        return paced;
    }

    public static async Task CollectAsync(EpgFeature feature)
    {
        (HttpStatusCode status, _) = await feature.PostAsync("/api/epg/collect-now");

        Assert.Equal(HttpStatusCode.Accepted, status);

        await feature.CollectionSettled();
    }

    public static async Task<EpgFeature> CollectedWithCorruptSectionsAsync()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.Carrying((Guide() with { CorruptSections = 2 }).ToBytes()));

        return await CollectedAsync(new EpgFeature([OnAir()], driver, clock: new WoundClock(Airs)));
    }

    public static async Task<EpgFeature> CollectedOnceAsync()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.Carrying(Guide().ToBytes()));

        EpgFeature feature = await CollectedAsync(new EpgFeature(
            [OnAir()],
            driver,
            new CollectionSettings { BetweenBoosts = TimeSpan.Zero, BetweenVisits = TimeSpan.Zero },
            clock: new WoundClock(Airs)));

        Assert.NotEmpty(feature.Programmes.Programmes);

        return feature;
    }

    public static async Task<HttpStatusCode> RebuildAsync(EpgFeature feature)
    {
        (HttpStatusCode rebuilt, _) = await feature.PostAsync(
            "/api/epg/rebuild",
            new { confirm = "discard-everything" });

        return rebuilt;
    }

    public static async Task<EpgFeature> CollectedWithEveryServiceCataloguedAsync()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.Carrying(GuideCarryingAPortableSimulcast().ToBytes()));

        var feature = new EpgFeature([EverythingTheStreamCarries()], driver, clock: new WoundClock(Airs));

        feature.Catalogue.Services.Add(Catalogued(Television, ServiceCategory.Television));
        feature.Catalogue.Services.Add(Catalogued(SecondTelevision, ServiceCategory.Television));
        feature.Catalogue.Services.Add(Catalogued(OneSegSimulcast, ServiceCategory.OneSeg));
        feature.Catalogue.Services.Add(Catalogued(DataService, ServiceCategory.Data));

        return await CollectedAsync(feature);
    }

    public static async Task<EpgFeature> CollectedOverEightDaysAsync()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(Channel, ChannelScript.Carrying(OverEightDays(ScheduleStarts).ToBytes()));

        var feature = new EpgFeature([OnlyTheOneService()], driver, clock: new WoundClock(Airs));

        feature.Catalogue.Services.Add(Catalogued(Television, ServiceCategory.Television));

        return await CollectedAsync(feature);
    }

    private static async Task<EpgFeature> CollectedAsync(EpgFeature feature)
    {
        try
        {
            await CollectAsync(feature);

            return feature;
        }
        catch
        {
            await feature.DisposeAsync();

            throw;
        }
    }
}
