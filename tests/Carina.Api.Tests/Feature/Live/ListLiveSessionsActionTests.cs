using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Streaming;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.Feature.Live;

[Trait("Category", "Feature")]
public sealed class ListLiveSessionsActionTests
{
    [Fact]
    public async Task ASessionSaysHowFarBehindTheFurthestBehindViewerIsAndNotOnlyWhatItAlreadyThrewAway()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Watched", remoteControlKey: 1);
        feature.Watching(
            1024,
            LiveProfile.Hd30,
            2,
            LiveStartup.NotStarted.Reaching(LiveStartupSegment.FirstPicture, TimeSpan.FromMilliseconds(9)),
            dropped: 28L,
            queued: 11);

        (_, JsonDocument body) = await feature.GetAsync("/api/live/sessions");

        JsonElement session = body.RootElement.GetProperty("data")[0];

        Assert.Equal(28L, session.GetProperty("dropped").GetInt64());
        Assert.Equal(11, session.GetProperty("queued").GetInt32());
    }

    [Fact]
    public async Task ASessionSaysWhatTheDriverThrewAwayBeforeItEverReachedUs()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Watched", remoteControlKey: 1);
        feature.Watching(
            1024,
            LiveProfile.Hd30,
            2,
            LiveStartup.NotStarted,
            dropped: 0L,
            chunksDroppedSinceTheSupplyOpened: 5_318L);

        (_, JsonDocument body) = await feature.GetAsync("/api/live/sessions");

        JsonElement session = body.RootElement.GetProperty("data")[0];

        Assert.Equal(5_318L, session.GetProperty("chunksDroppedSinceTheSupplyOpened").GetInt64());
        Assert.Equal(0L, session.GetProperty("dropped").GetInt64());
    }

    [Fact]
    public async Task ASupplyNobodyCouldBeAskedAboutIsNotAnsweredAsHavingLostNothing()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Watched", remoteControlKey: 1);
        feature.Watching(1024, LiveProfile.Hd30, 1);

        (_, JsonDocument body) = await feature.GetAsync("/api/live/sessions");

        JsonElement session = body.RootElement.GetProperty("data")[0];

        Assert.Equal(JsonValueKind.Null, session.GetProperty("chunksDroppedSinceTheSupplyOpened").ValueKind);
    }

    [Fact]
    public async Task EachViewerOfASessionIsAnsweredForApartSoTheOneSufferingIsNotHiddenInATotal()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Watched", remoteControlKey: 1);
        feature.Watching(
            1024,
            LiveProfile.Hd30,
            2,
            LiveStartup.NotStarted,
            dropped: 28L,
            queued: 11,
            watching: [new LiveBacklog(11, 28L), new LiveBacklog(0, 0L)]);

        (_, JsonDocument body) = await feature.GetAsync("/api/live/sessions");

        JsonElement watching = body.RootElement.GetProperty("data")[0].GetProperty("watching");

        Assert.Equal(2, watching.GetArrayLength());
        Assert.Equal(28L, watching[0].GetProperty("droppedSinceTheyJoined").GetInt64());
        Assert.Equal(11, watching[0].GetProperty("queued").GetInt32());
        Assert.Equal(0L, watching[1].GetProperty("droppedSinceTheyJoined").GetInt64());
    }

    [Fact(DisplayName = "a session names the format its picture is transcoded in")]
    public async Task ASessionNamesTheFormatItsPictureIsTranscodedIn()
    {
        await using LiveFeature feature = new();

        feature.Watching(1024, LiveProfile.Hd30, VideoCodec.H265, 1);
        feature.Watching(1024, LiveProfile.Hd30, 1);

        (_, JsonDocument body) = await feature.GetAsync("/api/live/sessions");

        Assert.Equal(
            ["h264", "h265"],
            body.RootElement.GetProperty("data").EnumerateArray().Select(session => session.GetProperty("format").GetString()));
    }

    [Fact]
    public async Task TheSessionsAreTheOnesRunningWithTheirViewersTheirStartupAndWhatTheyThrewAway()
    {
        await using LiveFeature feature = new();

        feature.Watching(1032, LiveProfile.Hd60, 1, dropped: 3L);
        feature.Watching(1024, LiveProfile.Hd30, 2, dropped: 28L);

        (HttpStatusCode status, JsonDocument body) = await feature.GetAsync("/api/live/sessions");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement[] sessions = [.. body.RootElement.GetProperty("data").EnumerateArray()];

        Assert.Equal([1024, 1032], sessions.Select(session => session.GetProperty("serviceId").GetInt32()));
        Assert.Equal(["720p30", "720p60"], sessions.Select(session => session.GetProperty("profile").GetString()));
        Assert.Equal([2, 1], sessions.Select(session => session.GetProperty("viewers").GetInt32()));
        Assert.Equal([28L, 3L], sessions.Select(session => session.GetProperty("dropped").GetInt64()));

        JsonElement startup = sessions[0].GetProperty("startup");
        JsonElement[] marks = [.. startup.GetProperty("marks").EnumerateArray()];

        Assert.True(startup.GetProperty("inProgress").GetBoolean());
        Assert.Equal(
            ["tunerSecured", "channelLocked", "transcoderStarted", "initReached", "firstPicture"],
            marks.Select(mark => mark.GetProperty("segment").GetString()));
        Assert.Equal(9L, marks[2].GetProperty("reachedAtMs").GetInt64());
        Assert.Equal(JsonValueKind.Null, marks[4].GetProperty("reachedAtMs").ValueKind);
    }

    [Fact]
    public async Task AStartupWhoseChannelLockedAfterTheTranscoderStartedReportsNoNegativeIntervalAndSaysWhatEachWaitedFor()
    {
        await using LiveFeature feature = new();

        feature.Watching(
            1024,
            LiveProfile.Hd30,
            1,
            LiveStartup.NotStarted
                .Reaching(LiveStartupSegment.TunerSecured, TimeSpan.FromMilliseconds(485))
                .Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromMilliseconds(495))
                .Reaching(LiveStartupSegment.ChannelLocked, TimeSpan.FromMilliseconds(733))
                .Reaching(LiveStartupSegment.InitReached, TimeSpan.FromMilliseconds(4366))
                .Reaching(LiveStartupSegment.FirstPicture, TimeSpan.FromMilliseconds(4368)));

        (HttpStatusCode status, JsonDocument body) = await feature.GetAsync("/api/live/sessions");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement startup = body.RootElement.GetProperty("data")[0].GetProperty("startup");
        JsonElement[] marks = [.. startup.GetProperty("marks").EnumerateArray()];

        Assert.False(startup.GetProperty("inProgress").GetBoolean());
        Assert.Equal([485L, 248L, 10L, 3633L, 2L], marks.Select(mark => mark.GetProperty("tookMs").GetInt64()));
        Assert.All(marks, mark => Assert.True(mark.GetProperty("tookMs").GetInt64() >= 0));
        Assert.Equal(JsonValueKind.Null, marks[0].GetProperty("tookFrom").ValueKind);
        Assert.Equal("tunerSecured", marks[1].GetProperty("tookFrom").GetString());
        Assert.Equal("tunerSecured", marks[2].GetProperty("tookFrom").GetString());
        Assert.Equal("channelLocked", marks[3].GetProperty("tookFrom").GetString());
        Assert.Equal("initReached", marks[4].GetProperty("tookFrom").GetString());
    }

    [Fact]
    public async Task NoSessionsIsAnEmptyListRatherThanARefusal()
    {
        await using LiveFeature feature = new();

        (HttpStatusCode status, JsonDocument body) = await feature.GetAsync("/api/live/sessions");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(body.RootElement.GetProperty("data").EnumerateArray());
    }
}
