using System.Net;
using System.Net.WebSockets;

using Carina.Api.Live;
using Carina.Domain.Streaming;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class LiveWireTests
{
    private static readonly Uri Wire = new(LiveWire.Path, UriKind.Relative);

    private static readonly Uri Handshake = new("ws://localhost" + LiveWire.Path);

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

    [Fact]
    public async Task AHandshakeIsRefusedWhileNothingIsBeingSentLive()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        string cookie = await probe.SignedInCookieAsync();

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Carrying(probe, cookie).ConnectAsync(Handshake, Patiently()));

        Assert.Contains("503", refused.Message, StringComparison.Ordinal);
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

    private static CancellationToken Patiently() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

    private static AuthProbe Wiring(HeldLiveSource held, LiveWireSettings? settings = null)
        => AuthProbe.OverHttp(services =>
        {
            services.AddSingleton<ILiveWireSource>(held);

            if (settings is not null)
            {
                services.AddSingleton(settings);
            }
        });

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
}
