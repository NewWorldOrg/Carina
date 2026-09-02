using System.Net.WebSockets;

using Carina.Api.Live;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class LiveSessionWireTests
{
    private static readonly LiveSessionKey EveryFrame = new(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd30);

    private readonly PipedSupply supply = new();

    private readonly TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 4 });

    private readonly HeldTranscoders transcoders;

    public LiveSessionWireTests()
    {
        transcoders = new HeldTranscoders(budget);
    }

    [Fact]
    public async Task TwoWiresAskingForOneKeyAreCarriedByOneTranscoderAndSeeTheSamePicture()
    {
        await using AuthProbe probe = Wiring();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket one = await Carrying(probe, cookie).ConnectAsync(Handshake("32736", "1024", "720p30"), Patiently());
        using WebSocket another = await Carrying(probe, cookie).ConnectAsync(Handshake("32736", "1024", "720p30"), Patiently());

        Assert.Equal(1, transcoders.Started);
        Assert.Equal(1, budget.Running);

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);
        await transcoders.Raised[0].WriteAsync(Fmp4.Fragment(1_000));

        LiveFrame[] toOne = [await Take(one), await Take(one), await Take(one)];
        LiveFrame[] toAnother = [await Take(another), await Take(another), await Take(another)];

        Assert.Equal([LiveChannel.Control, LiveChannel.PictureHeader, LiveChannel.Picture], toOne.Select(frame => frame.Channel));
        Assert.Equal(toOne.Select(frame => frame.Channel), toAnother.Select(frame => frame.Channel));
        Assert.Equal(
            toOne.Skip(1).Select(frame => frame.Payload.ToArray()),
            toAnother.Skip(1).Select(frame => frame.Payload.ToArray()));
        Assert.Equal(2, Sessions(probe).Viewers(EveryFrame));
    }

    [Fact]
    public async Task AWireAskingForAnotherProfileOfTheSameChannelRaisesASecondTranscoder()
    {
        await using AuthProbe probe = Wiring();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket frames = await Carrying(probe, cookie).ConnectAsync(Handshake("32736", "1024", "720p30"), Patiently());
        using WebSocket fields = await Carrying(probe, cookie).ConnectAsync(Handshake("32736", "1024", "720p60"), Patiently());

        Assert.Equal(2, transcoders.Started);
        Assert.Equal(2, budget.Running);
        Assert.Equal([LiveProfile.Hd30, LiveProfile.Hd60], transcoders.Raised.Select(raised => raised.Profile));
    }

    [Fact]
    public async Task AWireOnASessionStillStartingIsFirstToldHowFarItHasGot()
    {
        await using AuthProbe probe = Wiring();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake("32736", "1024", "720p30"), Patiently());

        LiveFrame progress = await Take(socket);

        Assert.Equal(LiveChannel.Control, progress.Channel);

        LiveStartupReading read = LiveStartup.ReadProgress(progress.Payload.Span);

        Assert.Null(read.Fault);
        Assert.True(read.Startup!.Reached(LiveStartupSegment.TranscoderStarted));
        Assert.False(read.Startup.Reached(LiveStartupSegment.InitReached));
        Assert.False(read.Startup.Reached(LiveStartupSegment.TunerSecured));

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);
        await transcoders.Raised[0].WriteAsync(Fmp4.Fragment(1_000));

        Assert.Equal(LiveChannel.PictureHeader, (await Take(socket)).Channel);
        Assert.Equal(LiveChannel.Picture, (await Take(socket)).Channel);
    }

    [Fact]
    public async Task AWireJoiningASessionPastItsStartupIsHandedTheHeaderFirst()
    {
        await using AuthProbe probe = Wiring();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket early = await Carrying(probe, cookie).ConnectAsync(Handshake("32736", "1024", "720p30"), Patiently());

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);
        await transcoders.Raised[0].WriteAsync(Fmp4.Fragment(1_000));
        await Eventually.Happens(
            () => Sessions(probe).Startup(EveryFrame)?.Current is { InProgress: false },
            "the first picture ends the startup");

        using WebSocket late = await Carrying(probe, cookie).ConnectAsync(Handshake("32736", "1024", "720p30"), Patiently());

        Assert.Equal(LiveChannel.PictureHeader, (await Take(late)).Channel);
    }

    [Fact]
    public async Task AWireLeavingIsForgottenByTheSession()
    {
        await using AuthProbe probe = Wiring();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket staying = await Carrying(probe, cookie).ConnectAsync(Handshake("32736", "1024", "720p30"), Patiently());
        using WebSocket leaving = await Carrying(probe, cookie).ConnectAsync(Handshake("32736", "1024", "720p30"), Patiently());

        await leaving.SendAsync(LiveControls.Frame(LiveControl.Leaving).ToArray(), WebSocketMessageType.Binary, true, Patiently());

        await Eventually.Happens(() => Sessions(probe).Viewers(EveryFrame) is 1, "the session counts one viewer fewer");

        Assert.Equal(1, budget.Running);
    }

    [Fact]
    public async Task AWireNamingAProfileOffTheListIsRefusedBeforeAnythingIsRaised()
    {
        await using AuthProbe probe = Wiring();
        string cookie = await probe.SignedInCookieAsync();

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Carrying(probe, cookie).ConnectAsync(Handshake("32736", "1024", "hls"), Patiently()));

        Assert.Contains("503", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, supply.Asked);
        Assert.Equal(0, transcoders.Started);
    }

    [Fact]
    public async Task AWireIsRefusedWhileNothingSuppliesAStreamToThisApp()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        string cookie = await probe.SignedInCookieAsync();

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Carrying(probe, cookie).ConnectAsync(Handshake("32736", "1024", "720p30"), Patiently()));

        Assert.Contains("503", refused.Message, StringComparison.Ordinal);
    }

    private static Uri Handshake(string network, string service, string profile)
        => new($"ws://localhost{LiveWire.Path}?network={network}&service={service}&profile={profile}");

    private static CancellationToken Patiently() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

    private static LiveSessionManager Sessions(AuthProbe probe)
        => Assert.IsType<LiveSessionManager>(probe.Wired.Services.GetRequiredService<ILiveSessionManager>());

    private static WebSocketClient Carrying(AuthProbe probe, string cookie)
    {
        WebSocketClient client = probe.Wired.Server.CreateWebSocketClient();

        client.ConfigureRequest += request => request.Headers[HeaderNames.Cookie] = cookie;

        return client;
    }

    private static async Task<LiveFrame> Take(WebSocket socket)
    {
        byte[] heard = new byte[64 * 1024];

        WebSocketReceiveResult said = await socket.ReceiveAsync(new ArraySegment<byte>(heard), Patiently());

        Assert.Equal(WebSocketMessageType.Binary, said.MessageType);

        LiveFraming framing = LiveFrame.Read(heard.AsSpan(0, said.Count));

        Assert.NotNull(framing.Frame);

        return framing.Frame;
    }

    private AuthProbe Wiring()
        => AuthProbe.OverHttp(services =>
        {
            services.AddSingleton<ILiveSupply>(supply);
            services.AddSingleton<ITranscodeBudget>(budget);
            services.AddSingleton<ILiveTranscoderFactory>(transcoders);
        });
}
