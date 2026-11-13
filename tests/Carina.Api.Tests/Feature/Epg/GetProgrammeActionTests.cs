using System.Net;
using System.Text.Json;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.TestSupport;

namespace Carina.Api.Tests.Feature.Epg;

[Trait("Category", "Feature")]
public sealed class GetProgrammeActionTests
{
    private static readonly DateTime From = new(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AProgrammeIsReadBackByTheNameTheGuideGaveIt()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        feature.Programmes.Programmes.Add(Programme(4, 1049, 1, From.AddHours(9)));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/programs/4-1049-1");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("a programme", body.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ANameNobodyHoldsIsNotFound()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/4-1049-9");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task ANameThatIsNotThreeNumbersIsRefusedRatherThanLookedUp()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/not-a-programme");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task AProgrammeTheReplacementTookThePlaceOfHasNoDetailWhileTheReplacementHasOne()
    {
        await using EpgFeature feature = ReplacedGuide.Replaced();

        (HttpStatusCode replaced, _) = await feature.GetAsync("/api/programs/4-1049-6");
        (HttpStatusCode standing, JsonElement body) = await feature.GetAsync("/api/programs/4-1049-101");

        Assert.Equal(HttpStatusCode.NotFound, replaced);
        Assert.Equal(HttpStatusCode.OK, standing);
        Assert.Equal("差し替えの番組", body.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ACollectedProgrammeOpensAloneWithItsOpenEndAndItsRelay()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(CollectedGuide.Channel, ChannelScript.Carrying(CollectedGuide.Guide().ToBytes()));

        await using var feature = new EpgFeature([CollectedGuide.OnAir()], driver, clock: new WoundClock(CollectedGuide.Airs));

        await CollectedGuide.CollectAsync(feature);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(
            $"/api/programs/{CollectedGuide.Network}-{CollectedGuide.SecondTelevision}-7");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Night Handover", data.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("endsAt").ValueKind);
        Assert.Equal("scheduleBasic", data.GetProperty("source").GetString());

        JsonElement relay = Assert.Single(data.GetProperty("related").EnumerateArray());

        Assert.Equal(CollectedGuide.Television, relay.GetProperty("serviceId").GetInt32());
        Assert.Equal(11, relay.GetProperty("eventId").GetInt32());
        Assert.Equal("relayed", relay.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task WhatTheGuideLeavesOutOfItsColumnsIsStillHeldInTheStore()
    {
        await using EpgFeature feature = await CollectedGuide.CollectedWithEveryServiceCataloguedAsync();

        (_, JsonElement skeleton) = await feature.GetAsync(
            $"/api/programs/{CollectedGuide.Network}-{CollectedGuide.Television}-9");

        Assert.True(skeleton.GetProperty("data").GetProperty("isShadow").GetBoolean());
    }

    private static BroadcastStream Terrestrial(int network, int stream, int service)
        => Carrying(network, stream, service, TuningParameters.Terrestrial(22));

    private static BroadcastStream Carrying(int network, int stream, int service, TuningParameters tuning)
        => new(
            new NetworkId(network),
            new TransportStreamId(stream),
            tuning,
            [new ServiceId(service)]);

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
