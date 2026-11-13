using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

using Carina.Api.Live;
using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Auth;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Feature.Streaming;

[Trait("Category", "Feature")]
public sealed partial class LiveStreamDeliveryTests
{
    private const uint OneFrame = 1001;

    private const string Channel = "/api/live/32736-1024";

    private static readonly Uri Exit = new("/api/live/32736-1024/stream", UriKind.Relative);

    private static readonly DateTime At = new(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan NoTimeAtAllToSayAnything = TimeSpan.FromMilliseconds(200);

    private readonly PipedSupply supply = new();

    private readonly TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 4 });

    private readonly HeldTranscoders transcoders;

    private readonly LiveFanout fanout = new(new LiveFanoutSettings());

    private int gops;

    public LiveStreamDeliveryTests() => transcoders = new HeldTranscoders(budget);

    [Fact]
    public async Task APlayerThatOpensTheStreamAgainWithTheSameTicketIsLetInAgain()
    {
        await using AuthProbe probe = Wiring();
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        HttpResponseMessage first = await OpenedAsync(player);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(LiveStreamDelivery.MediaType, first.Content.Headers.ContentType?.MediaType);

        first.Dispose();

        using HttpResponseMessage again = await OpenedAsync(player);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(LiveStreamDelivery.MediaType, again.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TheTicketThatOpenedOneChannelOpensNoOther()
    {
        await using AuthProbe probe = Wiring();
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        using HttpResponseMessage opened = await OpenedAsync(player);

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);

        using HttpResponseMessage another = await player.GetAsync(
            new Uri("/api/live/32737-1032/stream", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Forbidden, another.StatusCode);
        Assert.Null(another.Headers.Location);
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

        using HttpResponseMessage refused = await player.GetAsync(Exit, HttpCompletionOption.ResponseHeadersRead);

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
        using HttpResponseMessage refused = await player.GetAsync(Exit, HttpCompletionOption.ResponseHeadersRead);

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
        using HttpResponseMessage refused = await player.GetAsync(Exit, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal(LiveStreamDelivery.NothingCameInTime, await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheOneSurfaceALiveTicketOpensIsTheChannelHandedOverAsItIs()
    {
        await using AuthProbe probe = WiringWithARecording(out _);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        using HttpResponseMessage opened = await OpenedAsync(player);

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal(LiveStreamDelivery.MediaType, opened.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ATicketForOneChannelOpensTheChannelHandedOverOnNoOther()
    {
        await using AuthProbe probe = WiringWithARecording(out _);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        using HttpResponseMessage refused = await player.GetAsync(
            new Uri("/api/live/32736-1025/stream", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Theory(DisplayName = "a player handed the live ticket in the path is handed the channel, whatever name ends the path")]
    [InlineData("a-channel%20a-programme.ts")]
    [InlineData("")]
    [InlineData("a-name.mp4")]
    public async Task APlayerHandedTheLiveTicketInThePathIsHandedTheChannel(string name)
    {
        await using AuthProbe probe = WiringWithARecording(out _);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, null);
        using HttpResponseMessage opened = await OpenedAsync(
            player,
            new Uri($"/api/live/32736-1024/with-ticket/{ticket}/{name}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal(LiveStreamDelivery.MediaType, opened.Content.Headers.ContentType?.MediaType);
    }

    [Fact(DisplayName = "a live ticket in the path opens the channel it was issued for and no other")]
    public async Task ALiveTicketInThePathOpensTheChannelItWasIssuedForAndNoOther()
    {
        await using AuthProbe probe = WiringWithARecording(out _);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, null);
        using HttpResponseMessage refused = await player.GetAsync(
            new Uri($"/api/live/32736-1025/with-ticket/{ticket}/a-channel.ts", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact(DisplayName = "something that is not the shape of a ticket in the live path is refused without being sent to a sign-in screen")]
    public async Task SomethingThatIsNotATicketInTheLivePathIsRefused()
    {
        await using AuthProbe probe = WiringWithARecording(out _);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        using HttpResponseMessage refused = await player.GetAsync(
            new Uri("/api/live/32736-1024/with-ticket/short/a-channel.ts", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Null(refused.Headers.Location);
    }

    [Fact(DisplayName = "a live ticket in the query is refused, because it shows as the title in a player")]
    public async Task ALiveTicketInTheQueryIsRefused()
    {
        await using AuthProbe probe = WiringWithARecording(out _);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, null);
        using HttpResponseMessage refused = await player.GetAsync(
            new Uri($"{Exit}?ticket={ticket}", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact(DisplayName = "BR-PV-002, BR-PV-005: a ticket in the path under a name ending in .m3u8 is answered with a playlist naming the playlist of one key")]
    public async Task ATicketUnderANameEndingInM3u8IsAnsweredWithAPlaylistNamingThePlaylistOfOneKey()
    {
        await using AuthProbe probe = WiringTheFanout();
        using HttpClient player = Player(probe);

        using HttpResponseMessage answer = await OpenedOnTheFanoutAsync(player, WithTicket(await IssuedAsync(probe), "a-channel%20a-programme.m3u8"));
        string playlist = await answer.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(LiveHlsPlaylists.MediaType, answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store, private", answer.Headers.CacheControl?.ToString());
        Assert.StartsWith("#EXTM3U\n", playlist, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(playlist, "#EXT-X-STREAM-INF:"));
        Assert.Contains(",RESOLUTION=1920x1080,FRAME-RATE=59.940,CODECS=\"avc1.640028,mp4a.40.2\"\n", playlist, StringComparison.Ordinal);
        Assert.True(Unguessable.IsOne(KeyIn(playlist)));
    }

    [Fact(DisplayName = "BR-PD-022: an external player's playlist sits on the profile this machine opens at with the main sound, in H.264 though another format is encoded here")]
    public async Task AnExternalPlayersPlaylistSitsOnTheProfileThisMachineOpensAtInH264()
    {
        await using AuthProbe probe = WiringTheFanout();
        using HttpClient player = Player(probe);

        using HttpResponseMessage answer = await OpenedOnTheFanoutAsync(player, WithTicket(await IssuedAsync(probe), "a.m3u8"));

        LiveSessionKey seated = Assert.Single(Seating(probe).Asked);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(new LiveSessionKey(new NetworkId(32736), new ServiceId(1024), LiveProfile.FullHd60), seated);
        Assert.Equal(VideoCodec.H264, seated.Format);
    }

    [Fact(DisplayName = "BR-PV-002, BR-PV-005: the ticket is spent on the first playlist, and opening the same name again enters by the grant and is handed a new key")]
    public async Task OpeningTheSameNameAgainEntersByTheGrantAndIsHandedANewKey()
    {
        await using AuthProbe probe = WiringTheFanout();
        using HttpClient player = Player(probe);
        string path = WithTicket(await IssuedAsync(probe), "a.m3u8");

        string first = KeyIn(await (await OpenedOnTheFanoutAsync(player, path)).Content.ReadAsStringAsync());
        string second = KeyIn(await (await OpenedOnTheFanoutAsync(player, path)).Content.ReadAsStringAsync());

        Assert.NotEqual(first, second);
        Assert.Equal(2, fanout.Viewers);
    }

    [Fact]
    public async Task ATicketForOneChannelOpensNoPlaylistOnAnother()
    {
        await using AuthProbe probe = WiringTheFanout();
        using HttpClient player = Player(probe);

        using HttpResponseMessage refused = await player.GetAsync(
            new Uri($"/api/live/32736-1025/with-ticket/{await IssuedAsync(probe)}/a.m3u8", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Empty(Seating(probe).Asked);
    }

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

    [Fact(DisplayName = "BR-BA-004: an application's ticket opens no live stream")]
    public async Task AnApplicationsTicketOpensNoLiveStream()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        string ticket = started.GetProperty("inTheClear").GetString()!;

        using HttpResponseMessage stream = await feature.Frame.GetAsync(new Uri($"/api/live/32736-1024/with-ticket/{ticket}/a.ts", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Forbidden, stream.StatusCode);
        Assert.Single(feature.Relay.Asked);
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

    private static HttpClient Carrying(AuthProbe probe, string? ticket)
    {
        HttpClient player = probe.Wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });

        if (ticket is not null)
        {
            player.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ticket);
        }

        return player;
    }

    private Task<HttpResponseMessage> OpenedAsync(HttpClient player) => OpenedAsync(player, Exit);

    private async Task<HttpResponseMessage> OpenedAsync(HttpClient player, Uri at)
    {
        Task<HttpResponseMessage> opening = player.GetAsync(at, HttpCompletionOption.ResponseHeadersRead);

        while (!opening.IsCompleted)
        {
            await supply.FeedAsync(Mouthful());
            await Task.WhenAny(opening, Task.Delay(TimeSpan.FromMilliseconds(20)));
        }

        return await opening;
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

    [GeneratedRegex(@"^/api/hls/([^/]+)/index\.m3u8$", RegexOptions.Multiline)]
    private static partial Regex KeyedPlaylist();

    private static string KeyIn(string playlist) => KeyedPlaylist().Match(playlist).Groups[1].Value;

    private static string WithTicket(string ticket, string name) => $"{Channel}/with-ticket/{ticket}/{name}";

    private static SeatingAt Seating(AuthProbe probe)
        => Assert.IsType<SeatingAt>(probe.Wired.Services.GetRequiredService<ILiveSessionManager>());

    private static HttpClient Player(AuthProbe probe)
        => probe.Wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    private LiveFrame Gop()
    {
        int gop = gops++;

        return new LiveFrame(
            LiveChannel.Picture,
            LivePts.Start,
            Fmp4Media.Fragment((ulong)gop * 60 * OneFrame, [.. Enumerable.Repeat(OneFrame, 60)], true));
    }

    /// <summary>
    /// Opens <paramref name="path"/> while the channel goes on carrying a keyframe every two seconds of pictures, for
    /// as long as the answer takes.
    /// </summary>
    private async Task<HttpResponseMessage> OpenedOnTheFanoutAsync(HttpClient client, string path)
    {
        Task<HttpResponseMessage> opening = client.GetAsync(new Uri(path, UriKind.Relative));

        while (!opening.IsCompleted)
        {
            fanout.Publish(Gop());
            await Task.WhenAny(opening, Task.Delay(TimeSpan.FromMilliseconds(20)));
        }

        return await opening;
    }

    private AuthProbe WiringTheFanout(HlsKeys? keys = null, RecordedStartup? heard = null)
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
        fanout.Publish(new LiveFrame(LiveChannel.PictureHeader, LivePts.Start, Fmp4Media.Initialisation()));

        void Wired(IServiceCollection wired)
        {
            wired.AddSingleton<IBroadcastServiceRepository>(services);
            wired.AddSingleton<ICandidateChannelRepository>(candidates);
            wired.AddSingleton<ILiveSessionManager>(new SeatingAt(fanout));
            wired.AddSingleton<ILiveEncoderSelector>(new AlreadyChosen(LiveEncoder.Vaapi, [VideoCodec.H264, VideoCodec.H265]));

            if (keys is not null)
            {
                wired.AddSingleton<IHlsKeys>(keys);
            }
        }

        return heard is null ? AuthProbe.OverHttp(Wired) : AuthProbe.OverHttpHeardBy(heard, Wired);
    }
}
