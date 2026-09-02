using System.Net;
using System.Net.WebSockets;

using Carina.Api.Live;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class LiveWireTests
{
    private static readonly Uri Wire = new(LiveWire.Path, UriKind.Relative);

    private static readonly Uri Handshake = new("ws://localhost" + LiveWire.Path + "?network=32736&service=1024&profile=720p30");

    private static readonly byte[] Picture = [0x0a, 0x0b, 0x0c];

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

    [Fact]
    public async Task AHandshakeNamingAnotherOriginIsRefused()
    {
        var held = new HeldLiveSource();
        await using AuthProbe probe = Wiring(held);
        string cookie = await probe.SignedInCookieAsync();

        WebSocketClient client = Carrying(probe, cookie);

        client.ConfigureRequest += request => request.Headers[HeaderNames.Origin] = "http://elsewhere.invalid";

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
        await using AuthProbe probe = Wiring(held, new LiveWireSettings { BetweenPings = TimeSpan.FromMilliseconds(50) });
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

    private static CancellationToken Patiently() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

    private static AuthProbe Wiring(ILiveWireSource source, LiveWireSettings? settings = null)
        => AuthProbe.OverHttp(services =>
        {
            services.AddSingleton<ILiveSessionManager>(new SeatingAt(source));

            if (settings is not null)
            {
                services.AddSingleton(settings);
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

    private sealed class SeatingAt(ILiveWireSource source) : ILiveSessionManager
    {
        private readonly Lock gate = new();

        private readonly List<LiveSessionKey> asked = [];

        public IReadOnlyList<LiveSessionKey> Asked
        {
            get
            {
                lock (gate)
                {
                    return [.. asked];
                }
            }
        }

        public async Task<LiveJoin> JoinAsync(LiveSessionKey key, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                asked.Add(key);
            }

            return await source.JoinAsync(cancellationToken) is { } viewing
                ? LiveJoin.Joined(viewing)
                : LiveJoin.Refused(LiveRefusal.TranscoderWouldNotStart, "what was being sent ended before a viewer could be seated.");
        }
    }
}
