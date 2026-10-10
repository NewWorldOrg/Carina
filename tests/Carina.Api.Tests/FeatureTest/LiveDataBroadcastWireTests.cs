using System.Net.WebSockets;
using System.Text;

using Carina.Api.Live;
using Carina.BroadcastTestSupport;
using Carina.Domain.Streaming;
using Carina.Infrastructure.DataBroadcast;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

/// <summary>
/// The data broadcast side channel of a live wire, from the transport stream the tuner hands over to the
/// frames a viewer's socket receives.
/// </summary>
public sealed class LiveDataBroadcastWireTests
{
    private static readonly Uri Handshake = new($"ws://localhost{LiveWire.Path}?network=32736&service={CarouselBroadcast.ProgramNumber}&profile=720p30");

    private static readonly CarouselModule Startup = new(
        0x0000,
        1,
        CarouselBroadcast.Resource("startup.bml", EntityWriter.BmlType, Encoding.ASCII.GetBytes("<bml/>")));

    private static readonly CarouselModule Logo = new(
        0x0001,
        1,
        CarouselBroadcast.Resource("logo.png", EntityWriter.PngType, [0x89, 0x50, 0x4E, 0x47]));

    private readonly PipedSupply supply = new();

    private readonly TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 4 });

    [Fact(DisplayName = "BR-BD-004: a wire joining a channel already being watched receives the catalog and every module before anything else")]
    public async Task AWireJoiningLaterReceivesTheCatalogAndEveryModuleFirst()
    {
        await using AuthProbe probe = Wiring();
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
        IReadOnlyList<LiveFrame> toTheFirst = await DataBroadcastUntil(first, frames => frames.Count(IsModule) is 2 && !IsModule(frames[^1]));

        using WebSocket late = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());
        LiveFrame[] toTheLate = [await TakePastProgress(late), await TakePastProgress(late), await TakePastProgress(late)];

        Assert.All(toTheLate, frame => Assert.Equal(LiveChannel.DataBroadcast, frame.Channel));
        Assert.Equal(
            [DataBroadcastFrames.CatalogKind, DataBroadcastFrames.ModuleKind, DataBroadcastFrames.ModuleKind],
            toTheLate.Select(frame => frame.Payload.Span[0]));
        Assert.Equal(toTheFirst[^1].Payload.ToArray(), toTheLate[0].Payload.ToArray());
        Assert.Equal(
            toTheFirst.Where(IsModule).Select(frame => frame.Payload.ToArray()),
            toTheLate.Skip(1).Select(frame => frame.Payload.ToArray()));
    }

    [Fact(DisplayName = "BR-BD-004: a wire on a service with no data broadcast is told so on the data broadcast channel")]
    public async Task AWireOnAServiceWithNoDataBroadcastIsToldSo()
    {
        await using AuthProbe probe = Wiring();
        string cookie = await probe.SignedInCookieAsync();
        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        await supply.Opened[0].WriteAsync(new CarouselBroadcast().Associated().Mapped(carrying: false).At(CarouselBroadcast.Second).Bytes);

        LiveFrame said = await TakePastProgress(socket);

        Assert.Equal(LiveChannel.DataBroadcast, said.Channel);
        Assert.Equal([DataBroadcastFrames.AbsentKind], said.Payload.ToArray());
    }

    private static bool IsModule(LiveFrame frame) => frame.Payload.Span[0] == DataBroadcastFrames.ModuleKind;

    private static async Task<IReadOnlyList<LiveFrame>> DataBroadcastUntil(WebSocket socket, Func<IReadOnlyList<LiveFrame>, bool> enough)
    {
        List<LiveFrame> heard = [];

        while (!enough(heard))
        {
            LiveFrame frame = await TakePastProgress(socket);

            Assert.Equal(LiveChannel.DataBroadcast, frame.Channel);
            heard.Add(frame);
        }

        return heard;
    }

    private static async Task<LiveFrame> TakePastProgress(WebSocket socket)
    {
        while (true)
        {
            LiveFrame frame = await Take(socket);

            if (frame.Channel is not LiveChannel.Control)
            {
                return frame;
            }
        }
    }

    private static async Task<LiveFrame> Take(WebSocket socket)
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

    private static CancellationToken Patiently() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

    private static WebSocketClient Carrying(AuthProbe probe, string cookie)
    {
        WebSocketClient client = probe.Wired.Server.CreateWebSocketClient();

        client.ConfigureRequest += request => request.Headers[HeaderNames.Cookie] = cookie;

        return client;
    }

    private AuthProbe Wiring()
        => AuthProbe.OverHttp(services =>
        {
            services.AddSingleton<ILiveSupply>(supply);
            services.AddSingleton<ITranscodeBudget>(budget);
            services.AddSingleton<ILiveTranscoderFactory>(new HeldTranscoders(budget));
        });
}
