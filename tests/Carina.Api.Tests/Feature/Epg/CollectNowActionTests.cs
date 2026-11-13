using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Api.Events;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.TestSupport;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Epg;

[Trait("Category", "Feature")]
public sealed class CollectNowActionTests
{
    [Fact]
    public async Task ABoostOverEverythingWalksEveryStreamOnOffer()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049]), Stream(4, 32_737, [1050])]);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/epg/collect-now");

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(2, body.GetProperty("data").GetProperty("streams").GetInt32());
        Assert.NotEqual(Guid.Empty, body.GetProperty("data").GetProperty("boostId").GetGuid());
    }

    [Fact]
    public async Task ABoostOverOneStreamWalksOnlyThatOne()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049]), Stream(4, 32_737, [1050])]);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/epg/collect-now",
            new { networkId = 4, transportStreamId = 32_737 });

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("streams").GetInt32());
    }

    [Fact]
    public async Task ABoostNamingAServiceWalksTheStreamCarryingIt()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049]), Stream(4, 32_737, [1050])]);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/epg/collect-now",
            new { serviceId = 1050 });

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("streams").GetInt32());
    }

    [Fact]
    public async Task ABoostForSomethingNobodyCarriesIsNotFound()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049])]);

        (HttpStatusCode status, _) = await feature.PostAsync(
            "/api/epg/collect-now",
            new { serviceId = 9999 });

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task ASecondBoostAskedForAtOnceIsRefusedAndSaysWhy()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049])]);

        (HttpStatusCode first, _) = await feature.PostAsync("/api/epg/collect-now");
        (HttpStatusCode second, JsonElement refused) = await feature.PostAsync("/api/epg/collect-now");

        Assert.Equal(HttpStatusCode.Accepted, first);
        Assert.Equal(HttpStatusCode.Conflict, second);
        Assert.Contains(
            refused.GetProperty("data").GetProperty("refusal").GetString(),
            (string[])["oneIsAlreadyRunning", "tooSoonAfterTheLastOne"]);
    }

    [Fact]
    public async Task ABoostVisitsAStreamAnOrdinarySweepWouldStillBeBackingOffFrom()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), ChannelScript.NoLock());

        await using var feature = new EpgFeature(
            [Stream(4, 32_736, [1049])],
            driver,
            new CollectionSettings { BetweenBoosts = TimeSpan.Zero });

        await BoostAsync(feature);
        await BoostAsync(feature);

        (_, JsonElement body) = await feature.GetAsync("/api/epg/collection-status");
        JsonElement visited = Assert.Single(body.GetProperty("data").GetProperty("streams").EnumerateArray());

        Assert.Equal(2, visited.GetProperty("consecutiveIncomplete").GetInt32());
    }

    [Fact]
    public async Task ABoostAsksTheDriverForTheHurriedPurpose()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), ChannelScript.NoLock());

        await using var feature = new EpgFeature([Stream(4, 32_736, [1049])], driver);

        await BoostAsync(feature);

        Assert.Equal([SessionPurpose.SurveyNow], driver.Purposes);
    }

    [Fact]
    public async Task ABoostThatHasFinishedStillLeavesTheNextOneWaitingOutTheCooldown()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), ChannelScript.NoLock());

        await using var feature = new EpgFeature([Stream(4, 32_736, [1049])], driver);

        await BoostAsync(feature);

        (HttpStatusCode status, JsonElement refused) = await feature.PostAsync("/api/epg/collect-now");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("tooSoonAfterTheLastOne", refused.GetProperty("data").GetProperty("refusal").GetString());
    }

    [Fact]
    public async Task AFormPostCannotSetABoostGoing()
    {
        await using var feature = new EpgFeature([Stream(4, 32_736, [1049])]);
        using var form = new StringContent("networkId=4", System.Text.Encoding.UTF8);

        form.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using HttpResponseMessage response = await feature.Client.PostAsync(
            new Uri("/api/epg/collect-now", UriKind.Relative),
            form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task AHarvestMissingASegmentIsNeverRecordedAsASuccess()
    {
        var driver = new ScriptedDriverClient();
        byte[] shortfall = (CollectedGuide.Guide() with
        {
            Services = [CollectedGuide.Missing(CollectedGuide.Television, "Synthetic One")],
        }).ToBytes();

        driver.Script(CollectedGuide.Channel, new ChannelScript { Paced = () => CollectedGuide.Stalling(shortfall) });

        await using var feature = new EpgFeature(
            [CollectedGuide.OnAir()],
            driver,
            new CollectionSettings { LongestVisit = TimeSpan.FromMilliseconds(300) },
            clock: new WoundClock(CollectedGuide.Airs));

        await CollectedGuide.CollectAsync(feature);

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
        byte[] shortfall = (CollectedGuide.Guide() with
        {
            NetworkId = CollectedGuide.Network + 1,
            Services = [CollectedGuide.Missing(CollectedGuide.SecondTelevision, "Synthetic Two")],
        }).ToBytes();

        driver.Script(CollectedGuide.Channel, ChannelScript.NoLock());
        driver.Script(second, new ChannelScript { Paced = () => CollectedGuide.Stalling(shortfall) });

        await using var feature = new EpgFeature(
            [
                CollectedGuide.OnAir() with { TunedWith = unreachable },
                new BroadcastStream(
                    new NetworkId(CollectedGuide.Network + 1),
                    new TransportStreamId(CollectedGuide.Stream + 1),
                    second,
                    [new ServiceId(CollectedGuide.SecondTelevision)])
                {
                    TunedWith = starved,
                },
            ],
            driver,
            new CollectionSettings { LongestVisit = TimeSpan.FromMilliseconds(300) },
            clock: new WoundClock(CollectedGuide.Airs));
        DateTime at = DateTime.UtcNow;

        feature.Candidates.Candidates.Add(CandidateChannel.Discover(
            unreachable,
            new NetworkId(CollectedGuide.Network),
            new ServiceId(CollectedGuide.Television),
            CollectedGuide.Channel,
            at));
        feature.Candidates.Candidates.Add(CandidateChannel.Discover(
            starved,
            new NetworkId(CollectedGuide.Network + 1),
            new ServiceId(CollectedGuide.SecondTelevision),
            second,
            at));

        await CollectedGuide.CollectAsync(feature);

        CandidateChannel blamed = feature.Candidates.Candidates
            .Single(candidate => candidate.Id.Equals(unreachable));
        CandidateChannel spared = feature.Candidates.Candidates
            .Single(candidate => candidate.Id.Equals(starved));

        Assert.Equal(1, blamed.ConsecutiveFailures);
        Assert.Equal(0, spared.ConsecutiveFailures);
        Assert.True(spared.IsInRotation);

        (_, JsonElement body) = await feature.GetAsync("/api/epg/collection-status");
        JsonElement held = body.GetProperty("data").GetProperty("streams").EnumerateArray()
            .Single(stream => stream.GetProperty("networkId").GetInt32() == CollectedGuide.Network + 1);

        Assert.Equal("incomplete", held.GetProperty("outcome").GetString());
        Assert.NotEqual(JsonValueKind.Null, held.GetProperty("notBefore").ValueKind);
    }

    [Fact]
    public async Task ACollectionAnnouncesItselfOnTheOpenEventStream()
    {
        var driver = new ScriptedDriverClient();

        driver.Script(CollectedGuide.Channel, ChannelScript.Carrying(CollectedGuide.Guide().ToBytes()));

        await using var feature = new EpgFeature([CollectedGuide.OnAir()], driver, clock: new WoundClock(CollectedGuide.Airs));

        using HttpResponseMessage listening = await feature.Client.GetAsync(
            new Uri(AppEventStream.Path, UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);
        await using Stream open = await listening.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(open);

        await CollectedGuide.CollectAsync(feature);

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

    private static async Task BoostAsync(EpgFeature feature)
    {
        (HttpStatusCode status, _) = await feature.PostAsync("/api/epg/collect-now");

        Assert.Equal(HttpStatusCode.Accepted, status);

        await feature.CollectionSettled();
    }

    private static BroadcastStream Stream(int network, int stream, IReadOnlyList<int> services)
        => new(
            new NetworkId(network),
            new TransportStreamId(stream),
            TuningParameters.Terrestrial(22),
            [.. services.Select(service => new ServiceId(service))]);
}
