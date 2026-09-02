using System.Net;
using System.Net.WebSockets;

using Carina.Api.Events;
using Carina.Api.Live;
using Carina.Contracts;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class LiveSignalTests
{
    private static readonly Uri Events = new(AppEventStream.Path, UriKind.Relative);

    private static readonly Uri Handshake = new("ws://localhost" + LiveWire.Path + "?network=32736&service=1024&profile=720p30");

    private readonly PipedSupply supply = new();

    private readonly TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 4 });

    [Fact]
    public async Task AWireRaisingASessionIsSignalledOnTheHubAsLiveAndNothingElseIsCarried()
    {
        await using AuthProbe probe = Wiring();
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
        await using AuthProbe probe = Wiring();
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
        await using AuthProbe probe = Wiring();
        string cookie = await probe.SignedInCookieAsync();

        using WebSocket first = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        using HttpResponseMessage listening = await probe.Client.GetAsync(Events, HttpCompletionOption.ResponseHeadersRead);
        await using Stream body = await listening.Content.ReadAsStreamAsync();
        using StreamReader reader = new(body);

        using WebSocket second = await Carrying(probe, cookie).ConnectAsync(Handshake, Patiently());

        Task<string?> anything = reader.ReadLineAsync();

        Assert.NotSame(anything, await Task.WhenAny(anything, Task.Delay(TimeSpan.FromMilliseconds(500))));
    }

    private static CancellationToken Patiently() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

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
            services.AddSingleton(new LiveSessionSettings { Linger = TimeSpan.FromMilliseconds(100) });
        });
}
