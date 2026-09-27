using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Live;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

public sealed class LiveStreamExitTests
{
    private const string Watched = "/api/live/32736-1024/stream";

    private static readonly DateTime At = new(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan NoTimeAtAllToSayAnything = TimeSpan.FromMilliseconds(200);

    private readonly PipedSupply supply = new();

    private readonly TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 4 });

    private readonly HeldTranscoders transcoders;

    public LiveStreamExitTests() => transcoders = new HeldTranscoders(budget);

    [Fact]
    public async Task ATicketOpensTheStreamOnceAndTheSameTicketOpensItNoSecondTime()
    {
        await using AuthProbe probe = Wiring();
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        HttpResponseMessage first = await OpenedAsync(player);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(LiveStreamDelivery.MediaType, first.Content.Headers.ContentType?.MediaType);

        first.Dispose();

        using HttpResponseMessage again = await player.GetAsync(Asked(), HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
        Assert.Null(again.Headers.Location);
    }

    [Fact]
    public async Task AReaderCarryingNoTicketAndNoCookieIsRefusedWithNothingInTheBody()
    {
        await using AuthProbe probe = Wiring();

        using HttpClient player = probe.Wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });

        using HttpResponseMessage refused = await player.GetAsync(Asked(), HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Null(refused.Headers.Location);
        Assert.Empty(await refused.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheBytesOfTheChannelReachTheReaderAsTheyAre()
    {
        await using AuthProbe probe = Wiring();
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        using HttpResponseMessage opened = await OpenedAsync(player);

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);

        byte[] sent = Mouthful();
        byte[] twice = [.. sent, .. sent];

        await supply.Opened[0].WriteAsync(sent);

        await using Stream reading = await opened.Content.ReadAsStreamAsync();

        Assert.Equal(twice, await ReadAsync(reading, twice.Length));
    }

    [Fact]
    public async Task AReaderRidesTheReadingAWireIsWatchingAndNoSecondTunerIsTaken()
    {
        await using AuthProbe probe = Wiring();
        string cookie = await probe.SignedInCookieAsync();
        string ticket = await IssuedAsync(probe);

        using System.Net.WebSockets.WebSocket wire = await Wired(probe, cookie).ConnectAsync(
            Handshake(),
            Patiently());

        Assert.Equal(1, transcoders.Started);

        using HttpClient player = Carrying(probe, ticket);
        using HttpResponseMessage opened = await OpenedAsync(player);

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal(1, supply.Asked);
        Assert.Single(supply.Opened);
        Assert.Equal(1, transcoders.Started);
        Assert.Equal(1, budget.Running);
    }

    [Fact]
    public async Task TheReadingIsHeldOpenWhileTheReaderReadsAndIsLetGoWhenItStops()
    {
        await using AuthProbe probe = Wiring();
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        HttpResponseMessage opened = await OpenedAsync(player);

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);

        await Eventually.Happens(
            () => supply.Opened[0].HeldOpenUntil.Count > 0,
            "the reading is asked to be held open while it is being read");

        opened.Dispose();

        await Eventually.Happens(() => supply.Opened[0].Disposed, "the reading is let go once the reader has stopped");
    }

    [Fact]
    public async Task AChannelOutsideWhatABroadcastCarriesIsRefusedBeforeAnythingIsTuned()
    {
        await using AuthProbe probe = Wiring();
        string cookie = await probe.SignedInCookieAsync();

        using HttpClient watcher = probe.Relaying(cookie);
        using HttpResponseMessage refused = await watcher.GetAsync(
            new Uri("/api/live/70000-1024/stream", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(0, supply.Asked);
    }

    [Fact]
    public async Task AChannelNoTunerWillReachIsRefusedInWordsAndTheTicketIsGoodAfterwards()
    {
        supply.Refusing = LiveRefusal.NoTunerFree;

        await using AuthProbe probe = Wiring();
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        using HttpResponseMessage refused = await player.GetAsync(Asked(), HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal(
            LiveRefusalClosures.Because(LiveRefusal.NoTunerFree),
            await refused.Content.ReadAsStringAsync());

        supply.Refusing = null;

        using HttpResponseMessage opened = await OpenedAsync(player);

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task AChannelThatSaysNothingInTimeIsRefusedRatherThanOpenedOnNothing()
    {
        await using AuthProbe probe = Wiring(NoTimeAtAllToSayAnything);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        using HttpResponseMessage refused = await player.GetAsync(Asked(), HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal(LiveStreamDelivery.NothingCameInTime, await refused.Content.ReadAsStringAsync());
    }

    private static Uri Asked() => new(Watched, UriKind.Relative);

    private static Uri Handshake()
        => new($"ws://localhost{LiveWire.Path}?network=32736&service=1024&profile=720p30");

    private static CancellationToken Patiently() => new CancellationTokenSource(Eventually.Patience).Token;

    private static byte[] Mouthful() => [.. Enumerable.Range(0, 4_000).Select(at => (byte)(at % 251))];

    private static WebSocketClient Wired(AuthProbe probe, string cookie)
    {
        WebSocketClient client = probe.Wired.Server.CreateWebSocketClient();

        client.ConfigureRequest += request => request.Headers[HeaderNames.Cookie] = cookie;

        return client;
    }

    private static async Task<byte[]> ReadAsync(Stream reading, int many)
    {
        byte[] heard = new byte[many];
        int filled = 0;

        while (filled < many)
        {
            int read = await reading.ReadAsync(heard.AsMemory(filled), Patiently());

            if (read is 0)
            {
                break;
            }

            filled += read;
        }

        return [.. heard.Take(filled)];
    }

    private static HttpClient Carrying(AuthProbe probe, string ticket)
    {
        HttpClient player = probe.Wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });

        player.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ticket);

        return player;
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

    private async Task<HttpResponseMessage> OpenedAsync(HttpClient player)
    {
        Task<HttpResponseMessage> opening = player.GetAsync(Asked(), HttpCompletionOption.ResponseHeadersRead);

        while (!opening.IsCompleted)
        {
            if (supply.Opened.Count > 0)
            {
                await supply.Opened[^1].WriteAsync(Mouthful());
            }

            await Task.WhenAny(opening, Task.Delay(TimeSpan.FromMilliseconds(20)));
        }

        return await opening;
    }

    private AuthProbe Wiring() => Wiring(new LiveSessionSettings().LongestRaise);

    private AuthProbe Wiring(TimeSpan longestRaise)
    {
        HeldServices services = new();
        HeldCandidates candidates = new();
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

        return AuthProbe.OverHttp(wired =>
        {
            wired.AddSingleton<IBroadcastServiceRepository>(services);
            wired.AddSingleton<ICandidateChannelRepository>(candidates);
            wired.AddSingleton<ILiveSupply>(supply);
            wired.AddSingleton<ITranscodeBudget>(budget);
            wired.AddSingleton<ILiveTranscoderFactory>(transcoders);
            wired.AddSingleton(new LiveSessionSettings(
                longestRaise: longestRaise,
                betweenHolds: TimeSpan.FromMilliseconds(50),
                heldAhead: TimeSpan.FromSeconds(2)));
        });
    }
}
