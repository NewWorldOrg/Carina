using System.Net;
using System.Net.WebSockets;
using System.Text.Json;

using Carina.Api.Events;
using Carina.Api.Live;
using Carina.Api.Tests.FeatureTest;
using Carina.BroadcastTestSupport;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.Infrastructure.DataBroadcast;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

using Puppis.Contracts;

using SessionId = Puppis.Contracts.SessionId;

namespace Carina.Api.Tests.Feature.Streaming;

[Trait("Category", "Feature")]
public sealed class LiveWireTests
{
    private static readonly Uri Wire = new(LiveWire.Path, UriKind.Relative);

    private static readonly Uri Handshake = new("ws://localhost" + LiveWire.Path + "?network=32736&service=1024&profile=720p30");

    private static readonly byte[] Picture = [0x0a, 0x0b, 0x0c];

    private static readonly LiveSessionKey EveryFrame = new(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd30);

    private static readonly TimeSpan Linger = TimeSpan.FromMilliseconds(400);

    private static readonly TuningParameters Channel27 = TuningParameters.Terrestrial(27);

    private static readonly Uri Events = new(AppEventStream.Path, UriKind.Relative);

    private static readonly DateTime At = new(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

    private static readonly CarouselModule Startup = new(
        0x0000,
        1,
        CarouselBroadcast.Resource("startup.bml", EntityWriter.BmlType, System.Text.Encoding.ASCII.GetBytes("<bml/>")));

    private static readonly CarouselModule Logo = new(
        0x0001,
        1,
        CarouselBroadcast.Resource("logo.png", EntityWriter.PngType, [0x89, 0x50, 0x4E, 0x47]));

    private readonly PipedSupply supply = new();

    private readonly TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 4 });

    private readonly HeldTranscoders transcoders;

    private readonly ScriptedDriverClient driver = new();

    public LiveWireTests()
    {
        transcoders = new HeldTranscoders(budget);
        driver.Script(Channel27, new ChannelScript { Paced = () => PacedStream.InChunksOf(new byte[188 * 16], 188) });
    }

    [Fact]
    public async Task TheWireIsRefusedToACallerCarryingNoCookie()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpResponseMessage response = await probe.Client.GetAsync(
            Wire,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AHandshakeCarryingNoCookieIsRefusedBeforeItBecomesAWebSocket()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => probe.Wired.Server.CreateWebSocketClient().ConnectAsync(Handshake, Patiently()));

        Assert.Contains("401", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHandshakeCarryingARevokedSessionIsRefusedTheSameWay()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        string cookie = await probe.SignedInCookieAsync();

        probe.Sessions.Sessions[^1].Revoke(DateTime.UtcNow);

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Carrying(probe, cookie).ConnectAsync(Handshake, Patiently()));

        Assert.Contains("401", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AskingForTheWireWithoutAskingToUpgradeIsRefused()
    {
        var held = new HeldLiveSource();
        await using AuthProbe probe = Wiring(held);

        await probe.SignedInAsync();

        using HttpResponseMessage response = await probe.Client.GetAsync(Wire);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("http://elsewhere.invalid")]
    [InlineData("null")]
    public async Task AHandshakeNamingAnotherOriginIsRefused(string origin)
    {
        var held = new HeldLiveSource();
        await using AuthProbe probe = Wiring(held);
        string cookie = await probe.SignedInCookieAsync();

        WebSocketClient client = Carrying(probe, cookie);

        client.ConfigureRequest += request => request.Headers[HeaderNames.Origin] = origin;

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ConnectAsync(Handshake, Patiently()));

        Assert.Contains("403", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?network=32736&service=1024")]
    [InlineData("?network=32736&service=1024&profile=hls")]
    [InlineData("?network=32736&service=1024&profile=720P30")]
    [InlineData("?network=thirty&service=1024&profile=720p30")]
    [InlineData("?network=32736&service=1024&profile=720p30&format=hevc")]
    [InlineData("?network=32736&service=1024&profile=720p30&format=h264&format=h265")]
    public async Task AHandshakeNamingNoKeyOrAProfileOffTheListIsRefusedBeforeItBecomesAWebSocket(string query)
    {
        HeldLiveSource held = new();
        await using AuthProbe probe = Wiring(held);
        string cookie = await probe.SignedInCookieAsync();

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Carrying(probe, cookie).ConnectAsync(new Uri("ws://localhost" + LiveWire.Path + query), Patiently()));

        Assert.Contains("400", refused.Message, StringComparison.Ordinal);
        Assert.Empty(Seating(probe).Asked);
    }

    [Theory(DisplayName = "a handshake asking for a format this machine does not transcode in is refused before it becomes a WebSocket, and no tuner is asked for")]
    [InlineData("h265")]
    [InlineData("av1")]
    public async Task AHandshakeAskingForAFormatThisMachineDoesNotTranscodeInIsRefused(string format)
    {
        HeldLiveSource held = new();
        await using AuthProbe probe = Wiring(held, formats: [VideoCodec.H264]);
        string cookie = await probe.SignedInCookieAsync();

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Carrying(probe, cookie).ConnectAsync(
                new Uri("ws://localhost" + LiveWire.Path + "?network=32736&service=1024&profile=720p30&format=" + format),
                Patiently()));

        Assert.Contains("400", refused.Message, StringComparison.Ordinal);
        Assert.Empty(Seating(probe).Asked);
        Assert.Empty(Seating(probe).Reached);
    }

    [Theory(DisplayName = "a handshake asking for a format this machine transcodes in is seated on a key in that format")]
    [InlineData("h265", VideoCodec.H265)]
    [InlineData("av1", VideoCodec.Av1)]
    public async Task AHandshakeAskingForAFormatThisMachineTranscodesInIsSeatedOnAKeyInThatFormat(string named, VideoCodec format)
    {
        HeldLiveSource held = new();
        await using AuthProbe probe = Wiring(held, formats: VideoCodecs.InOrder);
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(
            new Uri("ws://localhost" + LiveWire.Path + "?network=32736&service=1024&profile=720p30&format=" + named),
            Patiently());

        LiveSessionKey asked = Assert.Single(Seating(probe).Asked);

        Assert.Equal(format, asked.Format);
        Assert.Equal(LiveProfile.Hd30, asked.Profile);
    }

    [Fact(DisplayName = "a handshake naming no format is seated on a key in H.264, as every wire was")]
    public async Task AHandshakeNamingNoFormatIsSeatedOnAKeyInH264()
    {
        HeldLiveSource held = new();
        await using AuthProbe probe = Wiring(held);
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        Assert.Equal(VideoCodec.H264, Assert.Single(Seating(probe).Asked).Format);
    }

    [Fact]
    public async Task TheHeaderAndThePicturesArriveInTheOrderTheSourcePutThemIn()
    {
        var held = new HeldLiveSource();
        await using AuthProbe probe = Wiring(held);
        string cookie = await probe.SignedInCookieAsync();

        held.Send(new LiveFrame(LiveChannel.PictureHeader, LivePts.Start, Picture));
        held.Send(new LiveFrame(LiveChannel.Picture, LivePts.Of(90_000UL), Picture));
        held.Send(new LiveFrame(LiveChannel.Sound, LivePts.Of(180_000UL), Picture));

        using WebSocket socket = await Carrying(probe, cookie)
            .ConnectAsync(Handshake, Patiently());

        LiveFrame first = await Take(socket);
        LiveFrame second = await Take(socket);
        LiveFrame third = await Take(socket);

        Assert.Equal(LiveChannel.PictureHeader, first.Channel);
        Assert.Equal(LivePts.Start, first.Pts);
        Assert.Equal(LiveChannel.Picture, second.Channel);
        Assert.Equal(LivePts.Of(90_000UL), second.Pts);
        Assert.Equal(LiveChannel.Sound, third.Channel);
        Assert.Equal(Picture, third.Payload.ToArray());
    }

    [Fact]
    public async Task AWireThatIsAlreadyOpenKeepsCarryingFramesAfterItsSessionIsEnded()
    {
        var held = new HeldLiveSource();
        await using AuthProbe probe = Wiring(held);
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie)
            .ConnectAsync(Handshake, Patiently());

        probe.Sessions.Sessions[^1].Revoke(DateTime.UtcNow);

        held.Send(new LiveFrame(LiveChannel.Picture, LivePts.Of(90_000UL), Picture));

        LiveFrame carried = await Take(socket);

        Assert.Equal(LiveChannel.Picture, carried.Channel);
        Assert.Equal(Picture, carried.Payload.ToArray());
    }

    [Fact]
    public async Task APingArrivesOnAWireThatIsCarryingNothing()
    {
        var held = new HeldLiveSource();
        await using AuthProbe probe = Wiring(held, new LiveWireSettings(betweenPings: TimeSpan.FromMilliseconds(50)));
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie)
            .ConnectAsync(Handshake, Patiently());

        LiveFrame ping = await Take(socket);

        Assert.Equal(LiveChannel.Control, ping.Channel);
        Assert.Equal([(byte)LiveControl.Ping], ping.Payload.ToArray());
    }

    [Fact]
    public async Task AViewerSayingItIsLeavingIsClosedNormallyAndLetGoOf()
    {
        var held = new HeldLiveSource();
        await using AuthProbe probe = Wiring(held);
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie)
            .ConnectAsync(Handshake, Patiently());

        await Say(socket, LiveControl.Leaving);

        WebSocketReceiveResult ending = await Heard(socket);

        Assert.Equal(WebSocketMessageType.Close, ending.MessageType);

        Assert.Equal(WebSocketCloseStatus.NormalClosure, ending.CloseStatus);
        Assert.Equal(LiveDepartures.Because(LiveDeparture.ViewerLeft), ending.CloseStatusDescription);
        await Until(() => held.LetGo);
    }

    [Fact]
    public async Task AViewerSayingSomethingTheWireDoesNotUnderstandIsToldWhyItIsClosed()
    {
        var held = new HeldLiveSource();
        await using AuthProbe probe = Wiring(held);
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie)
            .ConnectAsync(Handshake, Patiently());

        await socket.SendAsync(
            "seek 42"u8.ToArray(),
            WebSocketMessageType.Text,
            true,
            Patiently());

        WebSocketReceiveResult ending = await Heard(socket);

        Assert.Equal(WebSocketMessageType.Close, ending.MessageType);

        Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, ending.CloseStatus);
        Assert.Equal(LiveDepartures.Because(LiveDeparture.SaidSomethingUnknown), ending.CloseStatusDescription);
    }

    [Fact]
    public async Task TheSourceRunningOutClosesTheWireAndSaysSo()
    {
        var held = new HeldLiveSource();
        await using AuthProbe probe = Wiring(held);
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie)
            .ConnectAsync(Handshake, Patiently());

        held.NoMore();

        LiveFrame said = await Take(socket);

        Assert.Equal(LiveChannel.Control, said.Channel);
        Assert.Equal(
            LiveDepartures.Ending(LiveDeparture.SourceEnded),
            LiveEndingReport.Read(said.Payload.Span).Report!.Why);

        WebSocketReceiveResult ending = await Heard(socket);

        Assert.Equal(WebSocketMessageType.Close, ending.MessageType);

        Assert.Equal(WebSocketCloseStatus.NormalClosure, ending.CloseStatus);
        Assert.Equal(LiveDepartures.Because(LiveDeparture.SourceEnded), ending.CloseStatusDescription);
    }

    [Fact]
    public async Task AViewerJoiningAFanoutLateIsHandedTheHeaderBeforeTheNextPicture()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());
        await using AuthProbe probe = Wiring(fanout);
        string cookie = await probe.SignedInCookieAsync();

        fanout.Publish(new LiveFrame(LiveChannel.PictureHeader, LivePts.Start, Picture));
        fanout.Publish(new LiveFrame(LiveChannel.Picture, LivePts.Of(90_000UL), Picture));

        using WebSocket socket = await Carrying(probe, cookie)
            .ConnectAsync(Handshake, Patiently());

        fanout.Publish(new LiveFrame(LiveChannel.Picture, LivePts.Of(180_000UL), Picture));

        LiveFrame first = await Take(socket);
        LiveFrame second = await Take(socket);

        Assert.Equal(LiveChannel.PictureHeader, first.Channel);
        Assert.Equal(LiveChannel.Picture, second.Channel);
        Assert.Equal(LivePts.Of(180_000UL), second.Pts);
        Assert.Equal(1, fanout.Viewers);
    }

    [Fact]
    public async Task TwoWiresOnOneFanoutAreBothCarriedAndBothClosedWhenItEnds()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());
        await using AuthProbe probe = Wiring(fanout);
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket one = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());
        using WebSocket another = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        fanout.Publish(new LiveFrame(LiveChannel.Picture, LivePts.Of(90_000UL), Picture));

        Assert.Equal(LivePts.Of(90_000UL), (await Take(one)).Pts);
        Assert.Equal(LivePts.Of(90_000UL), (await Take(another)).Pts);

        fanout.End();

        Assert.Equal(LiveChannel.Control, (await Take(one)).Channel);
        Assert.Equal(LiveChannel.Control, (await Take(another)).Channel);

        WebSocketReceiveResult oneEnding = await Heard(one);
        WebSocketReceiveResult anotherEnding = await Heard(another);

        Assert.Equal(WebSocketCloseStatus.NormalClosure, oneEnding.CloseStatus);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, anotherEnding.CloseStatus);
        Assert.Equal(LiveDepartures.Because(LiveDeparture.SourceEnded), oneEnding.CloseStatusDescription);
        await Until(() => fanout.Viewers is 0);
    }

    [Fact]
    public async Task AWireOnAFanoutThatHasEndedIsToldSoOnTheControlChannelAndClosed()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());
        await using AuthProbe probe = Wiring(fanout);
        string cookie = await probe.SignedInCookieAsync();

        fanout.End();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        LiveFrame said = await Take(socket);

        Assert.Equal(LiveChannel.Control, said.Channel);

        LiveRefusalReading read = LiveRefusalReport.Read(said.Payload.Span);

        Assert.Null(read.Fault);
        Assert.Equal(LiveRefusal.TranscoderWouldNotStart, read.Report!.Refusal);

        WebSocketReceiveResult ending = await Heard(socket);

        Assert.Equal(WebSocketMessageType.Close, ending.MessageType);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, ending.CloseStatus);
        Assert.Equal(LiveRefusalClosures.Because(LiveRefusal.TranscoderWouldNotStart), ending.CloseStatusDescription);
    }

    [Fact]
    public async Task TheKeyInTheHandshakeIsTheKeyThatIsJoined()
    {
        HeldLiveSource held = new();
        await using AuthProbe probe = Wiring(held);
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        Assert.Equal(
            [new LiveSessionKey(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd30)],
            Seating(probe).Asked);
    }

    [Fact]
    public async Task TheChannelInTheHandshakeIsReachedForUntilItsSessionIsJoined()
    {
        HeldLiveSource held = new();
        await using AuthProbe probe = Wiring(held);
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        Assert.Equal([new LiveChannelKey(new NetworkId(32736), new ServiceId(1024))], Seating(probe).Reached);
        Assert.True(Seating(probe).ReachHeldWhileJoining);
        Assert.Equal(1, Seating(probe).ReachesLetGo);
    }

    [Fact]
    public async Task TheChannelIsAlreadyBeingReachedForWhenTheSoundOnAirIsLookedUp()
    {
        SeatingAt seating = new(new HeldLiveSource());
        SoundAskedWhileReaching sound = new(() => seating.HoldsAReach);
        await using AuthProbe probe = AuthProbe.OverHttp(services =>
        {
            services.AddSingleton<ILiveSessionManager>(seating);
            services.AddSingleton<ISoundOnAir>(sound);
        });
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        Assert.Equal(1, sound.Asked);
        Assert.True(sound.AskedWhileReaching);
    }

    [Fact]
    public async Task AWireThatAViewerLeftIsCountedAsTheViewerLeaving()
    {
        var held = new HeldLiveSource();
        await using AuthProbe probe = Wiring(held);
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        Assert.Equal(0L, Everything(probe));

        await Say(socket, LiveControl.Leaving);
        await Heard(socket);
        await Until(() => Counted(probe, LiveDeparture.ViewerLeft) is 1L);

        Assert.Equal(1L, Everything(probe));
    }

    [Fact]
    public async Task AWireEndedByWhatTheViewerSaidIsCountedUnderThatAndNotUnderLeaving()
    {
        var held = new HeldLiveSource();
        await using AuthProbe probe = Wiring(held);
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        await socket.SendAsync("seek 42"u8.ToArray(), WebSocketMessageType.Text, true, Patiently());
        await Heard(socket);
        await Until(() => Counted(probe, LiveDeparture.SaidSomethingUnknown) is 1L);

        Assert.Equal(0L, Counted(probe, LiveDeparture.ViewerLeft));
    }

    [Fact]
    public async Task AWireEndedBecauseTheSourceRanOutIsCountedAsTheSourceEnding()
    {
        var held = new HeldLiveSource();
        await using AuthProbe probe = Wiring(held);
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        held.NoMore();

        await Take(socket);
        await Heard(socket);
        await Until(() => Counted(probe, LiveDeparture.SourceEnded) is 1L);

        Assert.Equal(1L, Everything(probe));
    }

    [Fact]
    public async Task AWireThatWasNeverSeatedIsNotCountedAsHavingEnded()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());
        await using AuthProbe probe = Wiring(fanout);
        string cookie = await probe.SignedInCookieAsync();

        fanout.End();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        await Take(socket);
        await Heard(socket);
        await Stays(() => Everything(probe) is 0L);
    }

    [Fact]
    public async Task TwoWiresAskingForOneKeyAreCarriedByOneTranscoderAndSeeTheSamePicture()
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket one = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());
        using WebSocket another = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());

        Assert.Equal(1, transcoders.Started);
        Assert.Equal(1, budget.Running);

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);
        await transcoders.Raised[0].WriteAsync(Fmp4.Fragment(1_000));

        LiveFrame[] toOne = [await TakePictureHeader(one), await TakePastProgress(one)];
        LiveFrame[] toAnother = [await TakePictureHeader(another), await TakePastProgress(another)];

        Assert.Equal([LiveChannel.PictureHeader, LiveChannel.Picture], toOne.Select(frame => frame.Channel));
        Assert.Equal(toOne.Select(frame => frame.Channel), toAnother.Select(frame => frame.Channel));
        Assert.Equal(
            toOne.Select(frame => frame.Payload.ToArray()),
            toAnother.Select(frame => frame.Payload.ToArray()));
        Assert.Equal(2, Sessions(probe).Viewers(EveryFrame));
    }

    [Fact]
    public async Task AWireAskingForAnotherProfileOfTheSameChannelRaisesASecondTranscoder()
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket frames = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());
        using WebSocket fields = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p60"), Patiently());

        Assert.Equal(2, transcoders.Started);
        Assert.Equal(2, budget.Running);
        Assert.Equal([LiveProfile.Hd30, LiveProfile.Hd60], transcoders.Raised.Select(raised => raised.Profile));
    }

    [Fact]
    public async Task AWireOnASessionStillStartingIsFirstToldHowFarItHasGot()
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());

        LiveFrame progress = await TakeAFrame(socket);

        Assert.Equal(LiveChannel.Control, progress.Channel);

        LiveStartupReading read = LiveStartup.ReadProgress(progress.Payload.Span);

        Assert.Null(read.Fault);
        Assert.True(read.Startup!.Reached(LiveStartupSegment.TranscoderStarted));
        Assert.False(read.Startup.Reached(LiveStartupSegment.InitReached));
        Assert.True(read.Startup.Reached(LiveStartupSegment.TunerSecured));
        Assert.False(read.Startup.Reached(LiveStartupSegment.ChannelLocked));

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);
        await transcoders.Raised[0].WriteAsync(Fmp4.Fragment(1_000));

        Assert.Equal(LiveChannel.PictureHeader, (await TakePictureHeader(socket)).Channel);
        Assert.Equal(LiveChannel.Picture, (await TakePastProgress(socket)).Channel);
    }

    [Fact]
    public async Task AWireIsToldEachStepOfTheStartupAsItIsReached()
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());

        LiveStartup atTheHandshake = await NextProgress(socket);

        Assert.True(atTheHandshake.Reached(LiveStartupSegment.TranscoderStarted));
        Assert.False(atTheHandshake.Reached(LiveStartupSegment.ChannelLocked));

        await supply.Opened[0].WriteAsync(new byte[1_000]);

        LiveStartup locked = await NextProgress(socket);

        Assert.True(locked.Reached(LiveStartupSegment.ChannelLocked));
        Assert.False(locked.Reached(LiveStartupSegment.InitReached));

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);

        LiveStartup init = await NextProgress(socket);

        Assert.True(init.Reached(LiveStartupSegment.InitReached));
        Assert.False(init.Reached(LiveStartupSegment.FirstPicture));

        await transcoders.Raised[0].WriteAsync(Fmp4.Fragment(1_000));

        LiveStartup done = await NextProgress(socket);

        Assert.True(done.Reached(LiveStartupSegment.FirstPicture));
        Assert.False(done.InProgress);
        Assert.All(done.Timeline, mark => Assert.True(mark.Took >= TimeSpan.Zero));
    }

    [Fact]
    public async Task AWireJoiningASessionPastItsStartupIsHandedTheHeaderFirst()
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket early = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);
        await transcoders.Raised[0].WriteAsync(Fmp4.Fragment(1_000));
        await Eventually.Happens(
            () => Sessions(probe).Startup(EveryFrame)?.Current is { InProgress: false },
            "the first picture ends the startup");

        using WebSocket late = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());

        Assert.Equal(LiveChannel.PictureHeader, (await TakeAFrame(late)).Channel);
    }

    [Fact]
    public async Task AWireLeavingIsForgottenByTheSession()
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket staying = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());
        using WebSocket leaving = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());

        await leaving.SendAsync(LiveControls.Frame(LiveControl.Leaving).ToArray(), WebSocketMessageType.Binary, true, Patiently());

        await Eventually.Happens(() => Sessions(probe).Viewers(EveryFrame) is 1, "the session counts one viewer fewer");

        Assert.Equal(1, budget.Running);
    }

    [Fact]
    public async Task WhyTheSupplyEndedReachesTheWireOnTheControlChannelBeforeItIsClosed()
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());

        Assert.Equal(LiveChannel.Control, (await TakeAFrame(socket)).Channel);

        supply.Opened[0].Ending = LiveSupplyEnding.Of(LiveSupplyEnd.TakenForARecording, "a recording outranked it.");
        supply.Opened[0].NoMore();

        await Eventually.Happens(() => supply.Opened[0].Disposed, "the reading of the supply has taken in why it ended");

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);
        transcoders.Raised[0].NoMore();

        (IReadOnlyList<LiveFrame> heard, WebSocketReceiveResult ending) = await UntilItIsClosed(socket);

        Assert.Contains(LiveChannel.PictureHeader, heard.Select(frame => frame.Channel));

        LiveEndingReport said = Assert.Single(
            heard.Where(frame => frame.Channel is LiveChannel.Control)
                .Select(frame => LiveEndingReport.Read(frame.Payload.Span))
                .Where(read => read.Fault is null)
                .Select(read => read.Report!));

        Assert.Equal(LiveSupplyEnd.TakenForARecording, said.Why);
        Assert.Equal(WebSocketMessageType.Close, ending.MessageType);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, ending.CloseStatus);
        Assert.Equal(LiveDepartures.Because(LiveDeparture.SourceEnded), ending.CloseStatusDescription);
    }

    [Fact]
    public async Task WhyItEndedReachesTheWireWhenTheTranscoderEndsBeforeTheSupplyEverDoes()
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());

        Assert.Equal(LiveChannel.Control, (await TakeAFrame(socket)).Channel);

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);
        transcoders.Raised[0].NoMore();

        (IReadOnlyList<LiveFrame> heard, WebSocketReceiveResult ending) = await UntilItIsClosed(socket);

        Assert.Contains(LiveChannel.PictureHeader, heard.Select(frame => frame.Channel));

        LiveEndingReport said = Assert.Single(
            heard.Where(frame => frame.Channel is LiveChannel.Control)
                .Select(frame => LiveEndingReport.Read(frame.Payload.Span))
                .Where(read => read.Fault is null)
                .Select(read => read.Report!));

        Assert.Null(supply.Opened[0].Ending);
        Assert.Equal(LiveSupplyEnd.LetGo, said.Why);
        Assert.Equal(WebSocketMessageType.Close, ending.MessageType);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, ending.CloseStatus);
        Assert.Equal(LiveDepartures.Because(LiveDeparture.SourceEnded), ending.CloseStatusDescription);
    }

    [Fact]
    public async Task AWireNamingAProfileOffTheListIsRefusedBeforeAnythingIsRaised()
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "hls"), Patiently()));

        Assert.Contains("400", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, supply.Asked);
        Assert.Equal(0, transcoders.Started);
    }

    [Fact]
    public async Task AWireIsToldOnTheControlChannelThatTheDriverCannotBeReachedAndIsClosed()
    {
        TuningByServiceId tuning = new();

        tuning.Answer(1024, TuningParameters.Terrestrial(27));

        await using AuthProbe probe = AuthProbe.OverHttp(services =>
            services.AddSingleton<IServiceTuningDirectory>(tuning));
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());

        LiveRefusalReport report = await Refused(socket);

        Assert.Equal(LiveRefusal.DriverUnavailable, report.Refusal);
        Assert.Null(report.Ceiling);

        WebSocketReceiveResult ending = await Heard(socket);

        Assert.Equal(WebSocketMessageType.Close, ending.MessageType);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, ending.CloseStatus);
        Assert.Equal(LiveRefusalClosures.Because(LiveRefusal.DriverUnavailable), ending.CloseStatusDescription);
    }

    [Theory]
    [InlineData(LiveRefusal.NoSuchChannel, WebSocketCloseStatus.InvalidPayloadData)]
    [InlineData(LiveRefusal.NoTunerFree, WebSocketCloseStatus.PolicyViolation)]
    [InlineData(LiveRefusal.WouldNotTune, WebSocketCloseStatus.InternalServerError)]
    public async Task WhatTheSupplyRefusesForReachesTheViewerAsThatReason(LiveRefusal why, WebSocketCloseStatus closed)
    {
        supply.Refusing = why;
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());

        LiveRefusalReport report = await Refused(socket);

        Assert.Equal(why, report.Refusal);
        Assert.Null(report.Ceiling);

        WebSocketReceiveResult ending = await Heard(socket);

        Assert.Equal(closed, ending.CloseStatus);
        Assert.Equal(LiveRefusalClosures.Because(why), ending.CloseStatusDescription);
        Assert.Equal(0, transcoders.Started);
    }

    [Fact]
    public async Task AFullBudgetReachesTheViewerWithHowFullItIs()
    {
        TranscodeBudget one = new(new TranscodeBudgetSettings { AtOnce = 1 });
        HeldTranscoders few = new(one);
        await using AuthProbe probe = AuthProbe.OverHttp(services =>
        {
            services.AddSingleton<ILiveSupply>(supply);
            services.AddSingleton<ITranscodeBudget>(one);
            services.AddSingleton<ILiveTranscoderFactory>(few);
        });
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket seated = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());
        using WebSocket refused = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p60"), Patiently());

        LiveRefusalReport report = await Refused(refused);

        Assert.Equal(LiveRefusal.TooManyAlready, report.Refusal);
        Assert.Equal(new TranscodeCeiling(1, 1), report.Ceiling);

        WebSocketReceiveResult ending = await Heard(refused);

        Assert.Equal(WebSocketCloseStatus.PolicyViolation, ending.CloseStatus);
        Assert.Equal(1, few.Started);
        Assert.Equal(1, Sessions(probe).Viewers(EveryFrame));
    }

    [Theory(DisplayName = "BR-PD-008: a wire opened while the channel is announcing two languages on one sound is given the channel of the language asked for")]
    [InlineData("", SoundChannel.Left)]
    [InlineData("&sound=main", SoundChannel.Left)]
    [InlineData("&sound=secondary", SoundChannel.Right)]
    public async Task AWireOpenedWhileTwoLanguagesAreOnOneSoundIsGivenTheChannelOfTheLanguageAskedFor(
        string asked,
        SoundChannel channel)
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();

        probe.Programmes.Programmes.Add(OnAir(event_: 7, AudioMode.DualMono, sounds: 1, TimeSpan.FromMinutes(-10)));

        using WebSocket socket = await Carrying(probe, cookie)
            .ConnectAsync(new Uri(HandshakeFor("32736", "1024", "720p30") + asked), Patiently());

        Assert.Equal(SoundPlacement.OneChannelOf(0, channel), Assert.Single(transcoders.Raised).Sound);
    }

    [Theory(DisplayName = "BR-PD-008: a wire opened while the channel announces two languages on its main sound beside a second stream is given a channel of the main sound, or the second stream as the third sound")]
    [InlineData("", 0, SoundChannel.Left)]
    [InlineData("&sound=secondary", 0, SoundChannel.Right)]
    [InlineData("&sound=third", 1, null)]
    public async Task AWireOpenedWhileTwoLanguagesAreOnTheMainSoundBesideASecondStreamIsGivenTheSoundAskedFor(
        string asked,
        int ordinal,
        SoundChannel? channel)
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();

        probe.Programmes.Programmes.Add(OnAir(event_: 7, AudioMode.DualMono, sounds: 2, TimeSpan.FromMinutes(-10)));

        using WebSocket socket = await Carrying(probe, cookie)
            .ConnectAsync(new Uri(HandshakeFor("32736", "1024", "720p30") + asked), Patiently());

        SoundPlacement expected = channel is { } side ? SoundPlacement.OneChannelOf(ordinal, side) : SoundPlacement.WholeStream(ordinal);

        Assert.Equal(expected, Assert.Single(transcoders.Raised).Sound);
    }

    [Theory(DisplayName = "BR-PD-008: a wire opened on any other broadcast is given the whole stream of the sound asked for")]
    [InlineData(AudioMode.Stereo, 1, "", 0)]
    [InlineData(AudioMode.Stereo, 2, "&sound=secondary", 1)]
    [InlineData(AudioMode.Surround, 1, "", 0)]
    public async Task AWireOpenedOnAnyOtherBroadcastIsGivenTheWholeStreamOfTheSoundAskedFor(
        AudioMode audio,
        int sounds,
        string asked,
        int ordinal)
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();

        probe.Programmes.Programmes.Add(OnAir(event_: 7, audio, sounds, TimeSpan.FromMinutes(-10)));

        using WebSocket socket = await Carrying(probe, cookie)
            .ConnectAsync(new Uri(HandshakeFor("32736", "1024", "720p30") + asked), Patiently());

        Assert.Equal(SoundPlacement.WholeStream(ordinal), Assert.Single(transcoders.Raised).Sound);
    }

    [Fact(DisplayName = "BR-PD-008: what the channel announced before and what it will announce next do not decide how the sound on air is taken")]
    public async Task WhatWasAnnouncedBeforeAndWhatComesNextDoNotDecideHowTheSoundOnAirIsTaken()
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();

        probe.Programmes.Programmes.Add(OnAir(event_: 6, AudioMode.DualMono, sounds: 1, TimeSpan.FromMinutes(-70)));
        probe.Programmes.Programmes.Add(OnAir(event_: 7, AudioMode.Stereo, sounds: 1, TimeSpan.FromMinutes(-10)));
        probe.Programmes.Programmes.Add(OnAir(event_: 8, AudioMode.DualMono, sounds: 1, TimeSpan.FromMinutes(50)));
        probe.Programmes.Programmes.Add(OnAir(event_: 9, AudioMode.DualMono, sounds: 1, TimeSpan.FromMinutes(-10), service: 1032));

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());

        Assert.Equal(SoundPlacement.WholeStream(0), Assert.Single(transcoders.Raised).Sound);
    }

    [Fact(DisplayName = "BR-PD-008: a wire opened after the broadcast changed how it carries its sound is not seated on the transcoder raised before it did")]
    public async Task AWireOpenedAfterTheBroadcastChangedIsNotSeatedOnTheTranscoderRaisedBefore()
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();
        Programme twoLanguages = OnAir(event_: 7, AudioMode.DualMono, sounds: 1, TimeSpan.FromMinutes(-10));

        probe.Programmes.Programmes.Add(twoLanguages);

        using WebSocket before = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());

        probe.Programmes.Programmes.Remove(twoLanguages);
        probe.Programmes.Programmes.Add(OnAir(event_: 8, AudioMode.Stereo, sounds: 1, TimeSpan.FromMinutes(-1)));

        using WebSocket after = await Carrying(probe, cookie).ConnectAsync(HandshakeFor("32736", "1024", "720p30"), Patiently());

        Assert.Equal(
            [SoundPlacement.OneChannelOf(0, SoundChannel.Left), SoundPlacement.WholeStream(0)],
            transcoders.Raised.Select(raised => raised.Sound));
        Assert.Equal(1, Sessions(probe).Viewers(EveryFrame));
        Assert.Equal(1, Sessions(probe).Viewers(EveryFrame.Taking(SoundPlacement.OneChannelOf(0, SoundChannel.Left))));
    }

    [Fact]
    public async Task AWireThatSaysItIsLeavingHasItsSessionStoppedOnTheDriverOnceAfterTheLinger()
    {
        await using AuthProbe probe = WiringTheDriver();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(HandshakeOn("1024"), Patiently());

        SessionId held = await TheOneSessionHeldAsync();

        await socket.SendAsync(LiveControls.Frame(LiveControl.Leaving).ToArray(), WebSocketMessageType.Binary, true, Patiently());
        await ReadUntilClosed(socket);

        await StoppedOnceAsync(held);
    }

    [Fact]
    public async Task AWireClosedTheOrdinaryWayHasItsSessionStoppedOnTheDriverOnceAfterTheLinger()
    {
        await using AuthProbe probe = WiringTheDriver();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(HandshakeOn("1024"), Patiently());

        SessionId held = await TheOneSessionHeldAsync();

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "leaving", Patiently());
        await ReadUntilClosed(socket);

        await StoppedOnceAsync(held);
    }

    [Fact]
    public async Task AWireThatBreaksOffWithoutAWordHasItsSessionStoppedOnTheDriverOnceAfterTheLinger()
    {
        await using AuthProbe probe = WiringTheDriver();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(HandshakeOn("1024"), Patiently());

        SessionId held = await TheOneSessionHeldAsync();

        socket.Abort();

        await StoppedOnceAsync(held);
    }

    [Fact]
    public async Task AWireSwitchingChannelsStopsOnlyTheSessionItLeftAndTheNewOneWhenItLeavesThatToo()
    {
        await using AuthProbe probe = WiringTheDriver();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket first = await Carrying(probe, cookie).ConnectAsync(HandshakeOn("1024"), Patiently());

        SessionId before = await TheOneSessionHeldAsync();

        using WebSocket second = await Carrying(probe, cookie).ConnectAsync(HandshakeOn("1032"), Patiently());

        await Eventually.Happens(() => driver.Live.Count is 2, "the second channel has a session of its own on the driver");

        SessionId after = driver.Live.Single(session => session != before);

        await first.SendAsync(LiveControls.Frame(LiveControl.Leaving).ToArray(), WebSocketMessageType.Binary, true, Patiently());
        await ReadUntilClosed(first);

        await StoppedOnceAsync(before);

        Assert.Equal([after], driver.Live);

        await second.SendAsync(LiveControls.Frame(LiveControl.Leaving).ToArray(), WebSocketMessageType.Binary, true, Patiently());
        await ReadUntilClosed(second);

        await Eventually.Happens(() => driver.Stopped.Count is 2, "the second session is stopped once its viewer has left");
        await Task.Delay(Linger * 3);

        Assert.Equal([before, after], driver.Stopped);
        Assert.Empty(driver.Live);
    }

    [Fact]
    public async Task AWireBackWithinTheLingerRidesTheSameSessionAndItsLeavingAgainStopsThatSessionOnce()
    {
        await using AuthProbe probe = WiringTheDriver();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket gone = await Carrying(probe, cookie).ConnectAsync(HandshakeOn("1024"), Patiently());

        SessionId held = await TheOneSessionHeldAsync();

        await gone.SendAsync(LiveControls.Frame(LiveControl.Leaving).ToArray(), WebSocketMessageType.Binary, true, Patiently());
        await ReadUntilClosed(gone);

        using WebSocket back = await Carrying(probe, cookie).ConnectAsync(HandshakeOn("1024"), Patiently());

        Assert.Equal(LiveChannel.Control, (await TakeABinaryFrame(back)).Channel);

        await Task.Delay(Linger * 2);

        Assert.Single(driver.Started);
        Assert.Empty(driver.Stopped);
        Assert.Equal([held], driver.Live);

        await back.SendAsync(LiveControls.Frame(LiveControl.Leaving).ToArray(), WebSocketMessageType.Binary, true, Patiently());
        await ReadUntilClosed(back);

        await StoppedOnceAsync(held);

        Assert.Single(driver.Started);
    }

    [Fact]
    public async Task AWireRaisingASessionIsSignalledOnTheHubAsLiveAndNothingElseIsCarried()
    {
        await using AuthProbe probe = WiringTheHub();
        string cookie = await probe.SignedInCookieAsync();

        using HttpResponseMessage listening = await probe.Client.GetAsync(Events, HttpCompletionOption.ResponseHeadersRead);
        await using Stream body = await listening.Content.ReadAsStreamAsync();
        using StreamReader reader = new(body);

        Assert.Equal(HttpStatusCode.OK, listening.StatusCode);

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        Assert.Equal([$"event: {AppEvents.Live}", "data"], await NextFrame(reader));
    }

    [Fact]
    public async Task TheLastViewerLeavingIsSignalledAgainOnceTheSessionIsGone()
    {
        await using AuthProbe probe = WiringTheHub();
        string cookie = await probe.SignedInCookieAsync();

        using HttpResponseMessage listening = await probe.Client.GetAsync(Events, HttpCompletionOption.ResponseHeadersRead);
        await using Stream body = await listening.Content.ReadAsStreamAsync();
        using StreamReader reader = new(body);

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        Assert.Equal([$"event: {AppEvents.Live}", "data"], await NextFrame(reader));

        await socket.SendAsync(LiveControls.Frame(LiveControl.Leaving).ToArray(), WebSocketMessageType.Binary, true, Patiently());

        Assert.Equal([$"event: {AppEvents.Live}", "data"], await NextFrame(reader));
        await Eventually.Happens(() => budget.Running is 0, "the session is torn down once its linger is over");
    }

    [Fact]
    public async Task ASecondViewerOnTheSameSessionSignalsNothing()
    {
        await using AuthProbe probe = WiringTheHub();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket first = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        using HttpResponseMessage listening = await probe.Client.GetAsync(Events, HttpCompletionOption.ResponseHeadersRead);
        await using Stream body = await listening.Content.ReadAsStreamAsync();
        using StreamReader reader = new(body);

        using WebSocket second = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        Task<string?> anything = reader.ReadLineAsync();

        Assert.NotSame(anything, await Task.WhenAny(anything, Task.Delay(TimeSpan.FromMilliseconds(500))));
    }

    [Theory]
    [InlineData("wss", "https")]
    [InlineData("ws", "http")]
    public async Task AWireTheProxyLabelsAsAWebSocketIsOpenedForThePageThatWasServedOverTheMatchingScheme(
        string forwardedProto,
        string page)
    {
        await using ProxiedKestrelProbe probe = ProxiedKestrelProbe.ListeningOnKestrel();
        string cookie = await probe.SignedInCookieAsync();

        using ClientWebSocket client = probe.Opening(cookie, forwardedProto, probe.PageAt(page));

        await client.ConnectAsync(probe.Wire, Patiently());

        Assert.Equal(WebSocketState.Open, client.State);
        Assert.Equal(HttpStatusCode.SwitchingProtocols, client.HttpStatusCode);

        LiveFrame first = await TakeABinaryFrame(client);

        Assert.Equal(LiveChannel.Control, first.Channel);
        Assert.Null(LiveStartup.ReadProgress(first.Payload.Span).Fault);
        Assert.Equal(1, probe.Transcoders.Started);
    }

    [Theory]
    [InlineData("wss", "https://elsewhere.example")]
    [InlineData("wss", "http")]
    [InlineData("ws", "https")]
    [InlineData("ftp", "http")]
    public async Task AWireFromAPageThisAppDidNotServeIsStillRefusedWhateverTheProxyLabelsIt(
        string forwardedProto,
        string origin)
    {
        await using ProxiedKestrelProbe probe = ProxiedKestrelProbe.ListeningOnKestrel();
        string cookie = await probe.SignedInCookieAsync();
        string page = origin.Contains("://", StringComparison.Ordinal) ? origin : probe.PageAt(origin);

        using ClientWebSocket client = probe.Opening(cookie, forwardedProto, page);

        await Assert.ThrowsAsync<WebSocketException>(() => client.ConnectAsync(probe.Wire, Patiently()));

        Assert.Equal(HttpStatusCode.Forbidden, client.HttpStatusCode);
        Assert.Equal(0, probe.Transcoders.Started);
    }

    [Fact]
    public async Task AViewerThatStopsReadingOverRealKestrelIsRecordedAsHavingStoppedReading()
    {
        var held = new HeldLiveSource();
        await using LiveKestrelHost host = await LiveKestrelHost.StartAsync(
            held,
            new LiveWireSettings(
                betweenPings: TimeSpan.FromSeconds(30),
                writePatience: TimeSpan.FromMilliseconds(300)));

        using var client = new ClientWebSocket();
        await client.ConnectAsync(host.Wire, Patiently());

        held.Send(new LiveFrame(LiveChannel.Picture, LivePts.Start, new byte[8 * 1024 * 1024]));

        LiveDeparture departure = await host.DepartureAsync(Patiently());

        Assert.Equal(LiveDeparture.ViewerStoppedReading, departure);

        WebSocketException cut = await Assert.ThrowsAsync<WebSocketException>(() => ReadUntilClose(client));

        Assert.Equal(WebSocketError.ConnectionClosedPrematurely, cut.WebSocketErrorCode);
        Assert.Null(client.CloseStatus);
    }

    [Fact]
    public async Task TheSourceRunningOutIsReadByARealClientAsACleanCloseItCanNameTheReasonOf()
    {
        var held = new HeldLiveSource();
        await using LiveKestrelHost host = await LiveKestrelHost.StartAsync(held);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(host.Wire, Patiently());

        held.Send(new LiveFrame(LiveChannel.Picture, LivePts.Of(90_000UL), Picture));
        held.NoMore();

        WebSocketReceiveResult ending = await ReadUntilClose(client);

        Assert.Equal(WebSocketMessageType.Close, ending.MessageType);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, client.CloseStatus);
        Assert.Equal(LiveDepartures.Because(LiveDeparture.SourceEnded), client.CloseStatusDescription);
        Assert.Equal(LiveDeparture.SourceEnded, await host.DepartureAsync(Patiently()));
        Assert.Equal(
            1L,
            host.Tally.Counted.Single(counted => counted.Departure == LiveDeparture.SourceEnded).Times);
        Assert.NotNull(
            host.Tally.Counted.Single(counted => counted.Departure == LiveDeparture.SourceEnded).Longest);
    }

    [Fact]
    public async Task ALiveTicketOfferedOnTheWireHandshakeIsRefusedBeforeItBecomesAWebSocket()
    {
        await using AuthProbe probe = WiringWithARecording(out _);
        string ticket = await IssuedAsync(probe);

        WebSocketClient client = probe.Wired.Server.CreateWebSocketClient();

        client.ConfigureRequest += request => request.Headers[HeaderNames.Authorization] = $"Bearer {ticket}";

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ConnectAsync(Handshake, new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token));

        Assert.Contains("401", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATicketIsNotHandedToTheWire()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using HttpResponseMessage ticket = await feature.AskForATicketAsync(new { networkId = 32736, serviceId = 1024 });
        using JsonDocument body = JsonDocument.Parse(await ticket.Content.ReadAsStringAsync());
        string inTheClear = body.RootElement.GetProperty("data").GetProperty("inTheClear").GetString()!;

        using HttpRequestMessage asking = new(
            HttpMethod.Get,
            new Uri(LiveWire.Path + "?network=32736&service=1024&profile=720p30", UriKind.Relative));

        asking.Headers.TryAddWithoutValidation("Authorization", $"Bearer {inTheClear}");

        using HttpResponseMessage response = await feature.Anonymous.SendAsync(asking);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "BR-BD-004: a wire joining a channel already being watched receives the catalog naming what has arrived and every module before anything else")]
    public async Task AWireJoiningLaterReceivesTheCatalogAndEveryModuleFirst()
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();
        using WebSocket first = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        await supply.Opened[0].WriteAsync(new CarouselBroadcast()
            .Associated()
            .Mapped()
            .At(CarouselBroadcast.Second)
            .Listed(1, Startup, Logo)
            .Delivered(Startup)
            .Delivered(Logo)
            .Bytes);
        IReadOnlyList<LiveFrame> toTheFirst = await DataBroadcastUntil(first, frames => frames.Count(IsModule) is 2);

        using WebSocket late = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());
        LiveFrame[] toTheLate = [await TakePastControl(late), await TakePastControl(late), await TakePastControl(late)];

        Assert.All(toTheLate, frame => Assert.Equal(LiveChannel.DataBroadcast, frame.Channel));
        Assert.Equal(
            [DataBroadcastFrames.CatalogKind, DataBroadcastFrames.ModuleKind, DataBroadcastFrames.ModuleKind],
            toTheLate.Select(frame => frame.Payload.Span[0]));
        Assert.Equal(
            [DataBroadcastFrames.CatalogKind, DataBroadcastFrames.CatalogKind, DataBroadcastFrames.ModuleKind, DataBroadcastFrames.ModuleKind],
            toTheFirst.Select(frame => frame.Payload.Span[0]));
        Assert.All(["startup.bml", "logo.png"], path => Assert.True(Names(toTheLate[0], path)));
        Assert.False(Names(toTheFirst[1], "logo.png"));
        Assert.Equal(
            toTheFirst.Where(IsModule).Select(frame => frame.Payload.ToArray()),
            toTheLate.Skip(1).Select(frame => frame.Payload.ToArray()));
    }

    [Fact(DisplayName = "BR-BD-004: a wire on a service with no data broadcast is told so on the data broadcast channel")]
    public async Task AWireOnAServiceWithNoDataBroadcastIsToldSo()
    {
        await using AuthProbe probe = WiringSessions();
        string cookie = await probe.SignedInCookieAsync();
        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        await supply.Opened[0].WriteAsync(new CarouselBroadcast().Associated().Mapped(carrying: false).At(CarouselBroadcast.Second).Bytes);

        LiveFrame said = await TakePastControl(socket);

        Assert.Equal(LiveChannel.DataBroadcast, said.Channel);
        Assert.Equal([DataBroadcastFrames.AbsentKind], said.Payload.ToArray());
    }

    private static long Counted(AuthProbe probe, LiveDeparture departure)
        => Departures(probe).Counted.Single(counted => counted.Departure == departure).Times;

    private static long Everything(AuthProbe probe) => Departures(probe).Counted.Sum(counted => counted.Times);

    private static LiveDepartureTally Departures(AuthProbe probe)
        => probe.Wired.Services.GetRequiredService<ILiveDepartureLedger>().Read();

    private static CancellationToken Patiently() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

    private static AuthProbe Wiring(
        ILiveWireSource source,
        LiveWireSettings? settings = null,
        IReadOnlyList<VideoCodec>? formats = null)
        => AuthProbe.OverHttp(services =>
        {
            services.AddSingleton<ILiveSessionManager>(new SeatingAt(source));

            if (settings is not null)
            {
                services.AddSingleton(settings);
            }

            if (formats is not null)
            {
                services.AddSingleton<ILiveEncoderSelector>(new AlreadyChosen(LiveEncoder.Vaapi, formats));
            }
        });

    private static SeatingAt Seating(AuthProbe probe)
        => Assert.IsType<SeatingAt>(probe.Wired.Services.GetRequiredService<ILiveSessionManager>());

    private static WebSocketClient Carrying(AuthProbe probe, string cookie)
    {
        WebSocketClient client = probe.Wired.Server.CreateWebSocketClient();

        client.ConfigureRequest += request => request.Headers[HeaderNames.Cookie] = cookie;

        return client;
    }

    private static Task Say(WebSocket socket, LiveControl said)
        => socket.SendAsync(
            LiveControls.Frame(said).ToArray(),
            WebSocketMessageType.Binary,
            true,
            Patiently());

    private static async Task<LiveFrame> Take(WebSocket socket)
    {
        byte[] heard = new byte[64 * 1024];

        WebSocketReceiveResult said = await socket.ReceiveAsync(
            new ArraySegment<byte>(heard),
            Patiently());

        LiveFraming framing = LiveFrame.Read(heard.AsSpan(0, said.Count));

        Assert.Null(framing.Fault);

        return framing.Frame!;
    }

    private static async Task Stays(Func<bool> so)
    {
        for (int tries = 0; tries < 40; tries++)
        {
            Assert.True(so(), "What was to stay as it was did not.");

            await Task.Delay(25);
        }
    }

    private static async Task Until(Func<bool> settled)
    {
        for (int tries = 0; tries < 200; tries++)
        {
            if (settled())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail("The wire never finished what was expected of it.");
    }

    private static async Task<WebSocketReceiveResult> Heard(WebSocket socket)
    {
        byte[] heard = new byte[64 * 1024];

        return await socket.ReceiveAsync(new ArraySegment<byte>(heard), Patiently());
    }

    private static Programme OnAir(int event_, AudioMode audio, int sounds, TimeSpan startsIn, int service = 1024)
    {
        DateTime startsAt = DateTime.UtcNow + startsIn;

        return Programme.Rehydrate(
            new ProgrammeId(new NetworkId(32736), new ServiceId(service), new EventId(event_)),
            new TransportStreamId(32736),
            startsAt,
            startsAt + TimeSpan.FromHours(1),
            "a programme",
            string.Empty,
            isShadow: false,
            startsAt,
            audio: audio,
            sounds: sounds);
    }

    private static Uri HandshakeFor(string network, string service, string profile)
        => new($"ws://localhost{LiveWire.Path}?network={network}&service={service}&profile={profile}");

    private static LiveSessionManager Sessions(AuthProbe probe)
        => Assert.IsType<LiveSessionManager>(probe.Wired.Services.GetRequiredService<ILiveSessionManager>());

    private static async Task<LiveRefusalReport> Refused(WebSocket socket)
    {
        LiveFrame said = await TakeAFrame(socket);

        Assert.Equal(LiveChannel.Control, said.Channel);

        LiveRefusalReading read = LiveRefusalReport.Read(said.Payload.Span);

        Assert.Null(read.Fault);

        return read.Report!;
    }

    private static bool IsProgress(LiveFrame frame)
        => frame.Channel is LiveChannel.Control && frame.Payload.Length == LiveStartup.PayloadLength;

    private static async Task<LiveFrame> TakePastProgress(WebSocket socket)
    {
        while (true)
        {
            LiveFrame frame = await TakeAFrame(socket);

            if (!IsProgress(frame))
            {
                return frame;
            }
        }
    }

    private static async Task<LiveFrame> TakePictureHeader(WebSocket socket)
    {
        LiveFrame first = await TakePastProgress(socket);

        if (first.Channel is not LiveChannel.CaptionHeader)
        {
            return first;
        }

        LiveFrame afterTheCanvas = await TakePastProgress(socket);

        Assert.Equal(LiveChannel.PictureHeader, afterTheCanvas.Channel);

        return afterTheCanvas;
    }

    private static async Task<LiveStartup> NextProgress(WebSocket socket)
    {
        while (true)
        {
            LiveFrame frame = await TakeAFrame(socket);

            if (IsProgress(frame))
            {
                LiveStartupReading read = LiveStartup.ReadProgress(frame.Payload.Span);

                Assert.Null(read.Fault);

                return read.Startup!;
            }
        }
    }

    private static async Task<LiveFrame> TakeAFrame(WebSocket socket)
    {
        byte[] heard = new byte[64 * 1024];

        WebSocketReceiveResult said = await socket.ReceiveAsync(new ArraySegment<byte>(heard), Patiently());

        Assert.True(
            said.MessageType is WebSocketMessageType.Binary,
            $"a frame was expected, and the wire said {said.MessageType} ({said.CloseStatusDescription})");

        LiveFraming framing = LiveFrame.Read(heard.AsSpan(0, said.Count));

        Assert.NotNull(framing.Frame);

        return framing.Frame;
    }

    private static async Task<(IReadOnlyList<LiveFrame> Heard, WebSocketReceiveResult Ending)> UntilItIsClosed(
        WebSocket socket)
    {
        List<LiveFrame> heard = [];
        byte[] said = new byte[64 * 1024];

        while (true)
        {
            WebSocketReceiveResult came = await socket.ReceiveAsync(new ArraySegment<byte>(said), Patiently());

            if (came.MessageType is WebSocketMessageType.Close)
            {
                return (heard, came);
            }

            LiveFraming framing = LiveFrame.Read(said.AsSpan(0, came.Count));

            Assert.NotNull(framing.Frame);

            heard.Add(framing.Frame);
        }
    }

    private AuthProbe WiringSessions()
        => AuthProbe.OverHttp(services =>
        {
            services.AddSingleton<ILiveSupply>(supply);
            services.AddSingleton<ITranscodeBudget>(budget);
            services.AddSingleton<ILiveTranscoderFactory>(transcoders);
        });

    private static Uri HandshakeOn(string service)
        => new($"ws://localhost{LiveWire.Path}?network=32736&service={service}&profile=720p30");

    private static async Task<LiveFrame> TakeABinaryFrame(WebSocket socket)
    {
        byte[] heard = new byte[64 * 1024];

        WebSocketReceiveResult said = await socket.ReceiveAsync(new ArraySegment<byte>(heard), Patiently());

        Assert.Equal(WebSocketMessageType.Binary, said.MessageType);

        LiveFraming framing = LiveFrame.Read(heard.AsSpan(0, said.Count));

        Assert.NotNull(framing.Frame);

        return framing.Frame;
    }

    private static async Task ReadUntilClosed(WebSocket socket)
    {
        byte[] heard = new byte[64 * 1024];

        try
        {
            while ((await socket.ReceiveAsync(new ArraySegment<byte>(heard), Patiently())).MessageType is not WebSocketMessageType.Close)
            {
            }
        }
        catch (WebSocketException)
        {
        }
    }

    private async Task<SessionId> TheOneSessionHeldAsync()
    {
        await Eventually.Happens(() => driver.Live.Count is 1, "the driver holds one live session for the wire");

        Assert.Equal([SessionPurpose.Live], driver.Purposes);

        return driver.Live.Single();
    }

    private async Task StoppedOnceAsync(SessionId held)
    {
        await Eventually.Happens(() => driver.Stopped.Count >= 1, "the session is stopped on the driver once the linger is over");
        await Task.Delay(Linger * 3);

        Assert.Equal([held], driver.Stopped);
        Assert.DoesNotContain(held, driver.Live);
    }

    private AuthProbe WiringTheDriver()
        => AuthProbe.OverHttp(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<IDriverClient>(driver);
            services.AddSingleton<IServiceTuningDirectory>(new ResolvedTuning(
                TuningResolution.Tunable(new CandidateChannelId(Guid.NewGuid()), Channel27, impaired: false)));
            services.AddSingleton(new LiveSessionSettings(linger: Linger));
            services.AddSingleton<ITranscodeBudget>(budget);
            services.AddSingleton<ILiveTranscoderFactory>(transcoders);
        });

    private static async Task<string[]> NextFrame(StreamReader reader)
    {
        List<string> frame = [];

        while (true)
        {
            string? line = await reader.ReadLineAsync().WaitAsync(Eventually.Patience);

            Assert.NotNull(line);

            if (line.Length > 0 && line[0] is ':')
            {
                continue;
            }

            if (line.Length is 0)
            {
                if (frame.Count > 0)
                {
                    return [.. frame];
                }

                continue;
            }

            frame.Add(line);
        }
    }

    private AuthProbe WiringTheHub()
        => AuthProbe.OverHttp(services =>
        {
            services.AddSingleton<ILiveSupply>(supply);
            services.AddSingleton<ITranscodeBudget>(budget);
            services.AddSingleton<ILiveTranscoderFactory>(new HeldTranscoders(budget));
            services.AddSingleton(new LiveSessionSettings(linger: TimeSpan.FromMilliseconds(100)));
        });

    private static async Task<WebSocketReceiveResult> ReadUntilClose(ClientWebSocket client)
    {
        byte[] heard = new byte[64 * 1024];

        while (true)
        {
            WebSocketReceiveResult said = await client.ReceiveAsync(new ArraySegment<byte>(heard), Patiently());

            if (said.MessageType is WebSocketMessageType.Close)
            {
                return said;
            }
        }
    }

    private static async Task<string> IssuedAsync(AuthProbe probe)
    {
        await probe.SignedInAsync();

        using HttpResponseMessage answer = await probe.Client.PostAsJsonAsync(
            new Uri("/api/live/ticket", UriKind.Relative),
            new { networkId = 32736, serviceId = 1024 });

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        using JsonDocument read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());

        return read.RootElement.GetProperty("data").GetProperty("inTheClear").GetString()!;
    }

    private AuthProbe WiringWithARecording(out Recording recording)
    {
        HeldServices services = new();
        HeldCandidates candidates = new();
        HeldRecordings recordings = new();
        NetworkId network = new(32736);
        ServiceId service = new(1024);
        CandidateChannel candidate = CandidateChannel.Discover(
            CandidateChannelId.New(),
            network,
            service,
            TuningParameters.Terrestrial(27),
            At);

        candidate.Select(SelectionSource.Manual, null, At);
        services.Services.Add(BroadcastService.Discover(network, service, "Watched", ServiceCategory.Television, At));
        candidates.Candidates.Add(candidate);

        recording = RecordingFeature.Begin(RecordingId.New());
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(RecordingFeature.Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, 4_000, RecordingFeature.Noon.AddMinutes(30));
        recordings.Recordings.Add(recording);

        return AuthProbe.OverHttp(wired =>
        {
            wired.AddSingleton<IBroadcastServiceRepository>(services);
            wired.AddSingleton<ICandidateChannelRepository>(candidates);
            wired.AddSingleton<IRecordingDirectory>(recordings);
            wired.AddSingleton<ILiveSupply>(supply);
        });
    }

    private static bool IsModule(LiveFrame frame) => frame.Payload.Span[0] == DataBroadcastFrames.ModuleKind;

    private static bool Names(LiveFrame catalog, string path) => catalog.Payload.Span.IndexOf(System.Text.Encoding.UTF8.GetBytes(path)) >= 0;

    private static async Task<IReadOnlyList<LiveFrame>> DataBroadcastUntil(WebSocket socket, Func<IReadOnlyList<LiveFrame>, bool> enough)
    {
        List<LiveFrame> heard = [];

        while (!enough(heard))
        {
            LiveFrame frame = await TakePastControl(socket);

            Assert.Equal(LiveChannel.DataBroadcast, frame.Channel);
            heard.Add(frame);
        }

        return heard;
    }

    private static async Task<LiveFrame> TakePastControl(WebSocket socket)
    {
        while (true)
        {
            LiveFrame frame = await TakeWhole(socket);

            if (frame.Channel is not LiveChannel.Control)
            {
                return frame;
            }
        }
    }

    private static async Task<LiveFrame> TakeWhole(WebSocket socket)
    {
        using MemoryStream message = new();
        byte[] heard = new byte[64 * 1024];
        WebSocketReceiveResult said;

        do
        {
            said = await socket.ReceiveAsync(new ArraySegment<byte>(heard), Patiently());

            Assert.True(
                said.MessageType is WebSocketMessageType.Binary,
                $"a frame was expected, and the wire said {said.MessageType} ({said.CloseStatusDescription})");
            message.Write(heard, 0, said.Count);
        }
        while (!said.EndOfMessage);

        LiveFraming framing = LiveFrame.Read(message.ToArray());

        Assert.NotNull(framing.Frame);

        return framing.Frame;
    }
}
