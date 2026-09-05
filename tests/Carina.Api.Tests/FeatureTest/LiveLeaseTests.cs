using System.Net.WebSockets;

using Carina.Api.Live;
using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class LiveLeaseTests
{
    private static readonly TimeSpan Linger = TimeSpan.FromMilliseconds(400);

    private static readonly TuningParameters Channel27 = TuningParameters.Terrestrial(27);

    private readonly ScriptedDriverClient driver = new();

    private readonly TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 4 });

    private readonly HeldTranscoders transcoders;

    public LiveLeaseTests()
    {
        transcoders = new HeldTranscoders(budget);
        driver.Script(Channel27, new ChannelScript { Paced = () => PacedStream.InChunksOf(new byte[188 * 16], 188) });
    }

    [Fact]
    public async Task AWireThatSaysItIsLeavingHasItsSessionStoppedOnTheDriverOnceAfterTheLinger()
    {
        await using AuthProbe probe = Wiring();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake("1024"), Patiently());

        SessionId held = await TheOneSessionHeldAsync();

        await socket.SendAsync(LiveControls.Frame(LiveControl.Leaving).ToArray(), WebSocketMessageType.Binary, true, Patiently());
        await ReadUntilClosed(socket);

        await StoppedOnceAsync(held);
    }

    [Fact]
    public async Task AWireClosedTheOrdinaryWayHasItsSessionStoppedOnTheDriverOnceAfterTheLinger()
    {
        await using AuthProbe probe = Wiring();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake("1024"), Patiently());

        SessionId held = await TheOneSessionHeldAsync();

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "leaving", Patiently());
        await ReadUntilClosed(socket);

        await StoppedOnceAsync(held);
    }

    [Fact]
    public async Task AWireThatBreaksOffWithoutAWordHasItsSessionStoppedOnTheDriverOnceAfterTheLinger()
    {
        await using AuthProbe probe = Wiring();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake("1024"), Patiently());

        SessionId held = await TheOneSessionHeldAsync();

        socket.Abort();

        await StoppedOnceAsync(held);
    }

    [Fact]
    public async Task AWireSwitchingChannelsStopsOnlyTheSessionItLeftAndTheNewOneWhenItLeavesThatToo()
    {
        await using AuthProbe probe = Wiring();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket first = await Carrying(probe, cookie).ConnectAsync(Handshake("1024"), Patiently());

        SessionId before = await TheOneSessionHeldAsync();

        using WebSocket second = await Carrying(probe, cookie).ConnectAsync(Handshake("1032"), Patiently());

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
        await using AuthProbe probe = Wiring();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket gone = await Carrying(probe, cookie).ConnectAsync(Handshake("1024"), Patiently());

        SessionId held = await TheOneSessionHeldAsync();

        await gone.SendAsync(LiveControls.Frame(LiveControl.Leaving).ToArray(), WebSocketMessageType.Binary, true, Patiently());
        await ReadUntilClosed(gone);

        using WebSocket back = await Carrying(probe, cookie).ConnectAsync(Handshake("1024"), Patiently());

        Assert.Equal(LiveChannel.Control, (await Take(back)).Channel);

        await Task.Delay(Linger * 2);

        Assert.Single(driver.Started);
        Assert.Empty(driver.Stopped);
        Assert.Equal([held], driver.Live);

        await back.SendAsync(LiveControls.Frame(LiveControl.Leaving).ToArray(), WebSocketMessageType.Binary, true, Patiently());
        await ReadUntilClosed(back);

        await StoppedOnceAsync(held);

        Assert.Single(driver.Started);
    }

    private static Uri Handshake(string service)
        => new($"ws://localhost{LiveWire.Path}?network=32736&service={service}&profile=720p30");

    private static CancellationToken Patiently() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

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

    private AuthProbe Wiring()
        => AuthProbe.OverHttp(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<IDriverClient>(driver);
            services.AddSingleton<IServiceTuningDirectory>(new ResolvedTuning(
                TuningResolution.Tunable(new CandidateChannelId(Guid.NewGuid()), Channel27, impaired: false)));
            services.AddSingleton(new LiveSessionSettings { Linger = Linger });
            services.AddSingleton<ITranscodeBudget>(budget);
            services.AddSingleton<ILiveTranscoderFactory>(transcoders);
        });
}
