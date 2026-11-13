using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.TestSupport;

namespace Carina.Api.Tests.Feature.Epg;

[Trait("Category", "Feature")]
public sealed class GetProgrammeGuideActionTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const string ADay = "?type=isdbT&from=2026-08-18T00:00:00Z&to=2026-08-19T00:00:00Z";

    private static readonly DateTime From = new(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ADayOfOneTypeComesBackWithTheServicesItCovers()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        feature.Programmes.Programmes.Add(Programme(4, 1049, 1, From.AddHours(9)));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/programs{ADay}");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Single(data.GetProperty("services").EnumerateArray());
        Assert.Equal("4-1049-1", data.GetProperty("programmes")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task AProgrammeOnAnotherBroadcastTypeIsNotInThisGuide()
    {
        await using var feature = new EpgFeature([Satellite(4, 32_736, 1049)]);

        feature.Programmes.Programmes.Add(Programme(4, 1049, 1, From.AddHours(9)));

        (_, JsonElement body) = await feature.GetAsync($"/api/programs{ADay}");

        Assert.Empty(body.GetProperty("data").GetProperty("programmes").EnumerateArray());
    }

    [Fact]
    public async Task AProgrammeOutsideTheWindowIsNotCarried()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        feature.Programmes.Programmes.Add(Programme(4, 1049, 1, From.AddDays(3)));

        (_, JsonElement body) = await feature.GetAsync($"/api/programs{ADay}");

        Assert.Empty(body.GetProperty("data").GetProperty("programmes").EnumerateArray());
    }

    [Fact]
    public async Task AWindowWiderThanTwoDaysIsRefused()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        (HttpStatusCode status, _) = await feature.GetAsync(
            "/api/programs?type=isdbT&from=2026-08-18T00:00:00Z&to=2026-08-21T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task AGuideWithNoTypeNamedIsRefused()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        (HttpStatusCode status, _) = await feature.GetAsync(
            "/api/programs?from=2026-08-18T00:00:00Z&to=2026-08-19T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task AGuideThatHasNotChangedIsNotSentAgain()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        feature.Programmes.Programmes.Add(Programme(4, 1049, 1, From.AddHours(9)));

        using HttpResponseMessage first = await feature.Client.GetAsync(
            new Uri($"/api/programs{ADay}", UriKind.Relative));
        string? tag = first.Headers.ETag?.ToString();

        Assert.NotNull(tag);

        using var asking = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/programs{ADay}", UriKind.Relative));

        asking.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(tag));

        using HttpResponseMessage again = await feature.Client.SendAsync(asking);

        Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
    }

    [Fact]
    public async Task AVisitThatChangedNothingLeavesTheGuideAsItWas()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        feature.Programmes.Programmes.Add(Programme(4, 1049, 1, From.AddHours(9)));

        string? first = await TagAsync(feature);

        await feature.Visits.SaveAsync(
            StreamVisit.Record(
                new NetworkId(4),
                new TransportStreamId(32_736),
                VisitOutcome.BasicOnly,
                From.AddHours(10),
                TimeSpan.FromSeconds(5)),
            CancellationToken.None);

        Assert.Equal(first, await TagAsync(feature));
    }

    [Fact]
    public async Task AProgrammeRewrittenWithoutAVisitMakesTheGuideWorthFetchingAgain()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        await feature.Programmes.AbsorbAsync(
            [Broadcast(1, From.AddHours(9), "a programme")],
            [],
            From,
            CancellationToken.None);

        string? first = await TagAsync(feature);

        await feature.Programmes.AbsorbAsync(
            [Broadcast(1, From.AddHours(9), "a programme, extended")],
            [],
            From.AddHours(1),
            CancellationToken.None);

        Assert.NotEqual(first, await TagAsync(feature));
    }

    [Fact]
    public async Task AGuideEmptiedWithoutAVisitMakesTheGuideWorthFetchingAgain()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        feature.Programmes.Programmes.Add(Programme(4, 1049, 1, From.AddHours(9)));

        string? first = await TagAsync(feature);

        await feature.Programmes.ForgetEverythingAsync(CancellationToken.None);

        Assert.NotEqual(first, await TagAsync(feature));
    }

    [Fact]
    public async Task AKeptProgrammeThatGrewRicherMakesTheGuideWorthFetchingAgain()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);
        ArchivedProgramme kept = Kept(1, From.AddHours(9));

        feature.Archived.Programmes.Add(kept);

        string? first = await TagAsync(feature);

        kept.AbsorbTheRicherOf(ArchivedProgramme.Rehydrate(
            new NetworkId(4),
            new ServiceId(1049),
            new EventId(1),
            From.AddHours(9),
            From.AddHours(9).AddMinutes(30),
            "a programme that ran",
            "and what it was about",
            false,
            [],
            [],
            From.AddHours(10)));

        Assert.NotEqual(first, await TagAsync(feature));
    }

    [Fact]
    public async Task APastDayIsAnsweredFromWhatWasKept()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        feature.Archived.Programmes.Add(Kept(1, From.AddHours(9)));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/programs{ADay}");
        JsonElement only = Assert.Single(body.GetProperty("data").GetProperty("programmes").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("4-1049-1", only.GetProperty("id").GetString());
        Assert.True(only.GetProperty("isArchived").GetBoolean());
    }

    [Fact]
    public async Task AProgrammeStillInTheGuideIsNotAlsoServedFromTheArchive()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        feature.Programmes.Programmes.Add(Programme(4, 1049, 1, From.AddHours(9)));
        feature.Archived.Programmes.Add(Kept(1, From.AddHours(9)));

        (_, JsonElement body) = await feature.GetAsync($"/api/programs{ADay}");
        JsonElement only = Assert.Single(body.GetProperty("data").GetProperty("programmes").EnumerateArray());

        Assert.False(only.GetProperty("isArchived").GetBoolean());
    }

    [Fact]
    public async Task ThePictureTheBroadcastAnnouncedComesBackBesideTheProgrammeItBelongsTo()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        feature.Programmes.Programmes.Add(
            Programme(4, 1049, 1, From.AddHours(9), VideoMode.Interlaced1080, AspectRatio.SixteenByNine));

        (_, JsonElement body) = await feature.GetAsync($"/api/programs{ADay}");
        JsonElement programme = body.GetProperty("data").GetProperty("programmes")[0];

        Assert.Equal("interlaced1080", programme.GetProperty("video").GetString());
        Assert.Equal("sixteenByNine", programme.GetProperty("aspect").GetString());
    }

    [Fact]
    public async Task AProgrammeNobodyAnnouncedAPictureForSaysUndeterminedRatherThanNothing()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        feature.Programmes.Programmes.Add(Programme(4, 1049, 1, From.AddHours(9)));

        (_, JsonElement body) = await feature.GetAsync($"/api/programs{ADay}");
        JsonElement programme = body.GetProperty("data").GetProperty("programmes")[0];

        Assert.Equal("undetermined", programme.GetProperty("video").GetString());
        Assert.Equal("undetermined", programme.GetProperty("aspect").GetString());
    }

    [Fact]
    public async Task TheVocabularyAClientIsGeneratedForNamesEveryPictureTheGuideCanHand()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        JsonObject schemas = document["components"]!["schemas"]!.AsObject();
        JsonObject programme = schemas["ProgrammeResponder"]!["properties"]!.AsObject();

        Assert.Contains("VideoMode", programme["video"]!["$ref"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("AspectRatio", programme["aspect"]!["$ref"]!.GetValue<string>(), StringComparison.Ordinal);

        Assert.Equal(
            [
                "interlaced1080",
                "interlaced480",
                "progressive1080",
                "progressive180",
                "progressive2160",
                "progressive240",
                "progressive4320",
                "progressive480",
                "progressive720",
                "undetermined",
            ],
            Named(schemas, "VideoMode"));

        Assert.Equal(
            ["fourByThree", "sixteenByNine", "sixteenByNineWithPanVector", "undetermined", "widerThanSixteenByNine"],
            Named(schemas, "AspectRatio"));
    }

    [Fact]
    public async Task TheGuideDrawsTheReplacementAndNotTheScheduleItReplaced()
    {
        await using EpgFeature feature = ReplacedGuide.Replaced();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/programs?type=isdbT&{ReplacedGuide.TheDay}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["4-1049-1", "4-1049-101", "4-1049-102"], ReplacedGuide.Ids(body.GetProperty("data").GetProperty("programmes")));
    }

    [Fact]
    public async Task TheProgrammeOnAirAndTheOneAfterItAreTheReplacementAndWhatFollowsIt()
    {
        await using EpgFeature feature = ReplacedGuide.Replaced();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            "/api/programs?type=isdbT&from=2026-08-18T06:10:00Z&to=2026-08-18T09:10:00Z");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["4-1049-101", "4-1049-102"], ReplacedGuide.Ids(body.GetProperty("data").GetProperty("programmes")));
    }

    [Fact]
    public async Task APastDayDrawsTheKeptReplacementAndNotTheScheduleItReplaced()
    {
        await using EpgFeature feature = ReplacedGuide.Replaced();
        feature.Archived.Programmes.AddRange(feature.Programmes.Programmes.Select(ReplacedGuide.Kept));
        feature.Programmes.Programmes.Clear();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/programs?type=isdbT&{ReplacedGuide.TheDay}");
        JsonElement programmes = body.GetProperty("data").GetProperty("programmes");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["4-1049-1", "4-1049-101", "4-1049-102"], ReplacedGuide.Ids(programmes));
        Assert.All(programmes.EnumerateArray(), programme => Assert.True(programme.GetProperty("isArchived").GetBoolean()));
    }

    [Fact]
    public async Task ACollectedGuideServesItsColumnsAndKeepsEveryServiceInTheStore()
    {
        await using EpgFeature feature = await CollectedGuide.CollectedWithCorruptSectionsAsync();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/programs{CollectedGuide.ADay}");
        JsonElement data = body.GetProperty("data");
        string?[] served = [.. data.GetProperty("programmes").EnumerateArray()
            .Select(programme => programme.GetProperty("id").GetString())];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [CollectedGuide.Television, CollectedGuide.SecondTelevision],
            data.GetProperty("services").EnumerateArray().Select(service => service.GetProperty("serviceId").GetInt32()));
        Assert.Contains($"{CollectedGuide.Network}-{CollectedGuide.Television}-1", served);
        Assert.Contains($"{CollectedGuide.Network}-{CollectedGuide.SecondTelevision}-7", served);
        Assert.DoesNotContain($"{CollectedGuide.Network}-{CollectedGuide.DataService}-5", served);
        Assert.Contains(
            feature.Programmes.Programmes,
            held => held.ServiceId.Value == CollectedGuide.DataService && held.EventId.Value == 5);
    }

    [Fact]
    public async Task ARebuildDiscardsTheGuideAndAFreshCollectionRestoresIt()
    {
        await using EpgFeature feature = await CollectedGuide.CollectedOnceAsync();

        Assert.Equal(HttpStatusCode.OK, await CollectedGuide.RebuildAsync(feature));

        await CollectedGuide.CollectAsync(feature);

        (_, JsonElement body) = await feature.GetAsync($"/api/programs{CollectedGuide.ADay}");
        string?[] served = [.. body.GetProperty("data").GetProperty("programmes").EnumerateArray()
            .Select(programme => programme.GetProperty("id").GetString())];

        Assert.Contains($"{CollectedGuide.Network}-{CollectedGuide.Television}-1", served);
        Assert.Equal(
            [CollectedGuide.Television, CollectedGuide.SecondTelevision],
            Assert.Single(await feature.Streams.ListAsync(CancellationToken.None))
                .Services.Select(service => service.Value));
    }

    [Fact]
    public async Task WhatTheGuideLeavesOutOfItsColumnsIsStillHeldInTheStore()
    {
        await using EpgFeature feature = await CollectedGuide.CollectedWithEveryServiceCataloguedAsync();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/programs{CollectedGuide.ADay}");
        JsonElement data = body.GetProperty("data");
        string?[] served = [.. data.GetProperty("programmes").EnumerateArray()
            .Select(programme => programme.GetProperty("id").GetString())];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [CollectedGuide.Television, CollectedGuide.SecondTelevision],
            data.GetProperty("services").EnumerateArray().Select(service => service.GetProperty("serviceId").GetInt32()));
        Assert.Contains($"{CollectedGuide.Network}-{CollectedGuide.Television}-1", served);
        Assert.DoesNotContain($"{CollectedGuide.Network}-{CollectedGuide.OneSegSimulcast}-3", served);
        Assert.DoesNotContain($"{CollectedGuide.Network}-{CollectedGuide.DataService}-5", served);
        Assert.DoesNotContain($"{CollectedGuide.Network}-{CollectedGuide.Television}-9", served);

        Assert.Contains(
            feature.Programmes.Programmes,
            held => held.ServiceId.Value == CollectedGuide.OneSegSimulcast && held.EventId.Value == 3);
        Assert.Contains(
            feature.Programmes.Programmes,
            held => held.ServiceId.Value == CollectedGuide.DataService && held.EventId.Value == 5);
        Assert.Contains(
            feature.Programmes.Programmes,
            held => held.ServiceId.Value == CollectedGuide.Television && held.EventId.Value == 9 && held.IsShadow);
    }

    [Fact]
    public async Task AServiceTheCatalogueHasNoOpinionOnIsNotWithheld()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(CollectedGuide.Channel, ChannelScript.Carrying(CollectedGuide.GuideCarryingAPortableSimulcast().ToBytes()));

        await using var feature = new EpgFeature([CollectedGuide.EverythingTheStreamCarries()], driver, clock: new WoundClock(CollectedGuide.Airs));

        await CollectedGuide.CollectAsync(feature);

        (_, JsonElement body) = await feature.GetAsync($"/api/programs{CollectedGuide.ADay}");

        Assert.Equal(
            [CollectedGuide.Television, CollectedGuide.SecondTelevision, CollectedGuide.OneSegSimulcast, CollectedGuide.DataService],
            body.GetProperty("data").GetProperty("services").EnumerateArray()
                .Select(service => service.GetProperty("serviceId").GetInt32()));
    }

    [Fact]
    public async Task AColumnThatALaterScanWithdrawsIsNotAnsweredFromACachedGuide()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(CollectedGuide.Channel, ChannelScript.Carrying(CollectedGuide.GuideCarryingAPortableSimulcast().ToBytes()));

        await using var feature = new EpgFeature([CollectedGuide.EverythingTheStreamCarries()], driver, clock: new WoundClock(CollectedGuide.Airs));
        BroadcastService portable = CollectedGuide.Catalogued(CollectedGuide.OneSegSimulcast, ServiceCategory.Television);

        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.Television, ServiceCategory.Television));
        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.SecondTelevision, ServiceCategory.Television));
        feature.Catalogue.Services.Add(portable);
        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.DataService, ServiceCategory.Television));

        await CollectedGuide.CollectAsync(feature);

        using HttpResponseMessage held = await feature.Client.GetAsync(
            new Uri($"/api/programs{CollectedGuide.ADay}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, held.StatusCode);

        portable.Describe("Synthetic", ServiceCategory.OneSeg, DateTime.UtcNow.AddMinutes(1));

        using var asking = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"/api/programs{CollectedGuide.ADay}", UriKind.Relative));

        asking.Headers.IfNoneMatch.Add(held.Headers.ETag!);

        using HttpResponseMessage answered = await feature.Client.SendAsync(asking);

        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);

        using JsonDocument served = JsonDocument.Parse(await answered.Content.ReadAsStringAsync());

        Assert.Equal(
            [CollectedGuide.Television, CollectedGuide.SecondTelevision, CollectedGuide.DataService],
            served.RootElement.GetProperty("data").GetProperty("services").EnumerateArray()
                .Select(service => service.GetProperty("serviceId").GetInt32()));
    }

    [Fact]
    public async Task AGuideGatheredOverEightDaysIsStillReadableOnItsEighthDay()
    {
        await using EpgFeature feature = await CollectedGuide.CollectedOverEightDaysAsync();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/programs?type=isdbT&from={CollectedGuide.Stamp(CollectedGuide.ScheduleStarts.AddDays(7))}"
            + $"&to={CollectedGuide.Stamp(CollectedGuide.ScheduleStarts.AddDays(8))}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [$"{CollectedGuide.Network}-{CollectedGuide.Television}-8"],
            body.GetProperty("data").GetProperty("programmes").EnumerateArray()
                .Select(programme => programme.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task AColumnTheChannelScreenWithdrawsIsNotAnsweredFromACachedGuide()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(CollectedGuide.Channel, ChannelScript.Carrying(CollectedGuide.GuideCarryingAPortableSimulcast().ToBytes()));

        await using var feature = new EpgFeature([CollectedGuide.EverythingTheStreamCarries()], driver, clock: new WoundClock(CollectedGuide.Airs));

        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.Television, ServiceCategory.Television));
        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.SecondTelevision, ServiceCategory.Television));
        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.OneSegSimulcast, ServiceCategory.Television));
        feature.Catalogue.Services.Add(CollectedGuide.Catalogued(CollectedGuide.DataService, ServiceCategory.Television));

        await CollectedGuide.CollectAsync(feature);

        using HttpResponseMessage held = await feature.Client.GetAsync(
            new Uri($"/api/programs{CollectedGuide.ADay}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, held.StatusCode);

        feature.Streams.Carried[0] = feature.Streams.Carried[0] with
        {
            Services = [new ServiceId(CollectedGuide.Television), new ServiceId(CollectedGuide.SecondTelevision), new ServiceId(CollectedGuide.DataService)],
        };

        using var asking = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"/api/programs{CollectedGuide.ADay}", UriKind.Relative));

        asking.Headers.IfNoneMatch.Add(held.Headers.ETag!);

        using HttpResponseMessage answered = await feature.Client.SendAsync(asking);

        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);

        using JsonDocument served = JsonDocument.Parse(await answered.Content.ReadAsStringAsync());

        Assert.Equal(
            [CollectedGuide.Television, CollectedGuide.SecondTelevision, CollectedGuide.DataService],
            served.RootElement.GetProperty("data").GetProperty("services").EnumerateArray()
                .Select(service => service.GetProperty("serviceId").GetInt32()));
    }

    private static async Task<string?> TagAsync(EpgFeature feature)
    {
        using HttpResponseMessage read = await feature.Client.GetAsync(
            new Uri($"/api/programs{ADay}", UriKind.Relative));

        return read.Headers.ETag?.ToString();
    }

    private static ProgrammeBroadcast Broadcast(int carried, DateTime startsAt, string name)
        => new(
            new ProgrammeId(new NetworkId(4), new ServiceId(1049), new EventId(carried)),
            new TransportStreamId(32_736),
            startsAt,
            startsAt.AddMinutes(30),
            name,
            string.Empty,
            false);

    private static ArchivedProgramme Kept(int carried, DateTime startsAt)
        => ArchivedProgramme.Rehydrate(
            new NetworkId(4),
            new ServiceId(1049),
            new EventId(carried),
            startsAt,
            startsAt.AddMinutes(30),
            "a programme that ran",
            string.Empty,
            false,
            [],
            [],
            startsAt);

    private static BroadcastStream Terrestrial(int network, int stream, int service)
        => Carrying(network, stream, service, TuningParameters.Terrestrial(22));

    private static BroadcastStream Satellite(int network, int stream, int service)
        => Carrying(network, stream, service, TuningParameters.Bs(5, new TransportStreamId(stream)));

    private static BroadcastStream Carrying(int network, int stream, int service, TuningParameters tuning)
        => new(
            new NetworkId(network),
            new TransportStreamId(stream),
            tuning,
            [new ServiceId(service)]);

    private static IEnumerable<string> Named(JsonObject schemas, string schema)
        => schemas[schema]!["enum"]!
            .AsArray()
            .Select(value => value!.GetValue<string>())
            .Order(StringComparer.Ordinal);

    private static Programme Programme(
        int network,
        int service,
        int carried,
        DateTime startsAt,
        VideoMode video = VideoMode.Undetermined,
        AspectRatio aspect = AspectRatio.Undetermined)
        => Domain.Programmes.Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(network), new ServiceId(service), new EventId(carried)),
                new TransportStreamId(32_736),
                startsAt,
                startsAt.AddMinutes(30),
                "a programme",
                string.Empty,
                false)
            {
                Video = video,
                Aspect = aspect,
            },
            startsAt);
}
