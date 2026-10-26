using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

using Carina.Api.Live;
using Carina.Domain.Auth;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

public sealed partial class LiveHlsTests
{
    private const uint OneFrame = 1001;

    private const string Channel = "/api/live/32736-1024";

    private static readonly DateTime At = new(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyDictionary<uint, Fmp4TrackDefaults> Tracks = new Dictionary<uint, Fmp4TrackDefaults>
    {
        [Fmp4Media.PictureTrack] = new(30_000, 0, 0, 0),
        [Fmp4Media.SoundTrack] = new(48_000, 1024, 0, Fmp4Media.SyncSample),
    };

    private readonly LiveFanout fanout = new(new LiveFanoutSettings());

    private int gops;

    [Fact(DisplayName = "BR-PV-002, BR-PV-005: a ticket in the path under a name ending in .m3u8 is answered with a playlist naming the playlist of one key")]
    public async Task ATicketUnderANameEndingInM3u8IsAnsweredWithAPlaylistNamingThePlaylistOfOneKey()
    {
        await using AuthProbe probe = Wiring();
        using HttpClient player = Player(probe);

        using HttpResponseMessage answer = await OpenedAsync(player, WithTicket(await IssuedAsync(probe), "a-channel%20a-programme.m3u8"));
        string playlist = await answer.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(LiveHlsPlaylists.MediaType, answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store, private", answer.Headers.CacheControl?.ToString());
        Assert.StartsWith("#EXTM3U\n", playlist, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(playlist, "#EXT-X-STREAM-INF:"));
        Assert.Contains(",RESOLUTION=1920x1080,FRAME-RATE=59.940,CODECS=\"avc1.640028,mp4a.40.2\"\n", playlist, StringComparison.Ordinal);
        Assert.True(Unguessable.IsOne(KeyIn(playlist)));
    }

    [Fact(DisplayName = "BR-PV-005: the playlist of a key, its initialisation and its segments are opened by the key alone")]
    public async Task ThePlaylistOfAKeyItsInitialisationAndItsSegmentsAreOpenedByTheKeyAlone()
    {
        await using AuthProbe probe = Wiring();
        using HttpClient player = Player(probe);
        string key = KeyIn(await (await OpenedAsync(player, WithTicket(await IssuedAsync(probe), "a.m3u8"))).Content.ReadAsStringAsync());

        using HttpResponseMessage listed = await player.GetAsync(new Uri($"/api/hls/{key}/index.m3u8", UriKind.Relative));
        using HttpResponseMessage initialisation = await player.GetAsync(new Uri($"/api/hls/{key}/init.mp4", UriKind.Relative));
        string playlist = await listed.Content.ReadAsStringAsync();
        long first = long.Parse(MediaSequence().Match(playlist).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        using HttpResponseMessage segment = await player.GetAsync(new Uri($"/api/hls/{key}/{first}.m4s", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Equal(LiveHlsPlaylists.MediaType, listed.Content.Headers.ContentType?.MediaType);
        Assert.Contains("#EXT-X-MAP:URI=\"init.mp4\"\n", playlist, StringComparison.Ordinal);
        Assert.Contains("#EXTINF:2.002,\n", playlist, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, initialisation.StatusCode);
        Assert.Equal("video/mp4", initialisation.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Fmp4Media.Initialisation(), await initialisation.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, segment.StatusCode);
        Assert.Equal("no-store, private", segment.Headers.CacheControl?.ToString());
        Assert.True(Fmp4Fragment.Read(await segment.Content.ReadAsByteArrayAsync(), Tracks)!.Of(Fmp4Media.PictureTrack)!.Samples[0].IsASync);
    }

    [Fact]
    public async Task ASegmentNoLongerOrNotYetHeldIsNotFound()
    {
        await using AuthProbe probe = Wiring();
        using HttpClient player = Player(probe);
        string key = KeyIn(await (await OpenedAsync(player, WithTicket(await IssuedAsync(probe), "a.m3u8"))).Content.ReadAsStringAsync());

        using HttpResponseMessage ahead = await player.GetAsync(new Uri($"/api/hls/{key}/9999.m4s", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, ahead.StatusCode);
    }

    [Theory(DisplayName = "BR-PV-005: a key never handed out opens nothing, and something not the shape of a key is refused as carrying nothing")]
    [InlineData("index.m3u8")]
    [InlineData("init.mp4")]
    [InlineData("0.m4s")]
    public async Task AKeyNeverHandedOutOpensNothing(string beneath)
    {
        await using AuthProbe probe = Wiring();
        using HttpClient player = Player(probe);

        using HttpResponseMessage unknown = await player.GetAsync(new Uri($"/api/hls/{Unguessable.Issue()}/{beneath}", UriKind.Relative));
        using HttpResponseMessage misshapen = await player.GetAsync(new Uri($"/api/hls/{new string('~', Unguessable.Length)}/{beneath}", UriKind.Relative));
        using HttpResponseMessage shortened = await player.GetAsync(new Uri($"/api/hls/short/{beneath}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, misshapen.StatusCode);
        Assert.Empty(await misshapen.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, shortened.StatusCode);
    }

    [Fact(DisplayName = "BR-PD-022: an external player's playlist sits on the profile this machine opens at with the main sound, in H.264 though another format is encoded here")]
    public async Task AnExternalPlayersPlaylistSitsOnTheProfileThisMachineOpensAtInH264()
    {
        await using AuthProbe probe = Wiring();
        using HttpClient player = Player(probe);

        using HttpResponseMessage answer = await OpenedAsync(player, WithTicket(await IssuedAsync(probe), "a.m3u8"));

        LiveSessionKey seated = Assert.Single(Seating(probe).Asked);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(new LiveSessionKey(new NetworkId(32736), new ServiceId(1024), LiveProfile.FullHd60), seated);
        Assert.Equal(VideoCodec.H264, seated.Format);
    }

    [Fact(DisplayName = "BR-PD-022: a playlist sits on the session a wire asking for the same profile and sound sits on, so it takes no transcoder of its own")]
    public async Task APlaylistSitsOnTheSessionAWireAskingForTheSameSitsOn()
    {
        await using AuthProbe probe = Wiring();
        await probe.SignedInAsync();

        using HttpResponseMessage answer = await OpenedAsync(probe.Client, $"{Channel}/hls.m3u8?profile=720p30&sound=secondary");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(
            new LiveSessionKey(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd30, SoundTrack.Secondary),
            Assert.Single(Seating(probe).Asked));
        Assert.Equal(1, fanout.Viewers);
    }

    [Fact]
    public async Task ABrowsersPlaylistAskedWithoutAProfileOpensAtTheProfileThisMachineOpensAt()
    {
        await using AuthProbe probe = Wiring();
        await probe.SignedInAsync();

        using HttpResponseMessage answer = await OpenedAsync(probe.Client, $"{Channel}/hls.m3u8");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Same(LiveProfile.FullHd60, Assert.Single(Seating(probe).Asked).Profile);
    }

    [Fact(DisplayName = "BR-PV-005: a browser's playlist is opened with its session alone, never with a ticket")]
    public async Task ABrowsersPlaylistIsOpenedWithItsSessionAlone()
    {
        await using AuthProbe probe = Wiring();
        string ticket = await IssuedAsync(probe);
        using HttpClient player = Player(probe);

        player.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ticket);

        using HttpResponseMessage refused = await player.GetAsync(new Uri($"{Channel}/hls.m3u8", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Empty(Seating(probe).Asked);
    }

    [Theory]
    [InlineData("?profile=4k")]
    [InlineData("?sound=fourth")]
    public async Task ABrowsersPlaylistAskedForWhatThereIsNotIsRefusedBeforeAnybodyIsSeated(string query)
    {
        await using AuthProbe probe = Wiring();
        await probe.SignedInAsync();

        using HttpResponseMessage refused = await probe.Client.GetAsync(new Uri($"{Channel}/hls.m3u8{query}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Empty(Seating(probe).Asked);
    }

    [Fact(DisplayName = "BR-PV-002, BR-PV-005: the ticket is spent on the first playlist, and opening the same name again enters by the grant and is handed a new key")]
    public async Task OpeningTheSameNameAgainEntersByTheGrantAndIsHandedANewKey()
    {
        await using AuthProbe probe = Wiring();
        using HttpClient player = Player(probe);
        string path = WithTicket(await IssuedAsync(probe), "a.m3u8");

        string first = KeyIn(await (await OpenedAsync(player, path)).Content.ReadAsStringAsync());
        string second = KeyIn(await (await OpenedAsync(player, path)).Content.ReadAsStringAsync());

        Assert.NotEqual(first, second);
        Assert.Equal(2, fanout.Viewers);
    }

    [Fact]
    public async Task ATicketForOneChannelOpensNoPlaylistOnAnother()
    {
        await using AuthProbe probe = Wiring();
        using HttpClient player = Player(probe);

        using HttpResponseMessage refused = await player.GetAsync(
            new Uri($"/api/live/32736-1025/with-ticket/{await IssuedAsync(probe)}/a.m3u8", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Empty(Seating(probe).Asked);
    }

    [Fact(DisplayName = "BR-PV-005: a key nothing is asked with for thirty seconds is refused, and the seat it held is let go of")]
    public async Task AKeyNothingIsAskedWithForThirtySecondsIsRefusedAndItsSeatLetGoOf()
    {
        HandTurnedClock clock = new();
        await using LiveHlsKeys keys = new(new LiveHlsSettings(), clock);
        await using AuthProbe probe = Wiring(keys);
        using HttpClient player = Player(probe);
        string key = KeyIn(await (await OpenedAsync(player, WithTicket(await IssuedAsync(probe), "a.m3u8"))).Content.ReadAsStringAsync());

        clock.Turn(TimeSpan.FromSeconds(20));

        using HttpResponseMessage kept = await player.GetAsync(new Uri($"/api/hls/{key}/index.m3u8", UriKind.Relative));

        clock.Turn(TimeSpan.FromSeconds(30));

        using HttpResponseMessage lapsed = await player.GetAsync(new Uri($"/api/hls/{key}/index.m3u8", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, lapsed.StatusCode);
        await Eventually.Happens(() => fanout.Viewers is 0, "the seat of the key let go of was given up");
    }

    [Fact(DisplayName = "BR-PV-005: signing out lets go of the keys made for the one signing out")]
    public async Task SigningOutLetsGoOfTheKeysMadeForTheOneSigningOut()
    {
        await using AuthProbe probe = Wiring();
        await probe.SignedInAsync();
        string key = KeyIn(await (await OpenedAsync(probe.Client, $"{Channel}/hls.m3u8")).Content.ReadAsStringAsync());
        using HttpClient player = Player(probe);

        using HttpResponseMessage signedOut = await probe.Client.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), AuthProbe.Json());
        using HttpResponseMessage refused = await player.GetAsync(new Uri($"/api/hls/{key}/index.m3u8", UriKind.Relative));

        Assert.True(signedOut.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task APlaylistOfASessionThatHasEndedSaysNoMoreIsComing()
    {
        await using AuthProbe probe = Wiring();
        using HttpClient player = Player(probe);
        string key = KeyIn(await (await OpenedAsync(player, WithTicket(await IssuedAsync(probe), "a.m3u8"))).Content.ReadAsStringAsync());

        fanout.End();

        string playlist = await Eventually.Yields(
            () => player.GetStringAsync(new Uri($"/api/hls/{key}/index.m3u8", UriKind.Relative)),
            listed => listed.EndsWith("#EXT-X-ENDLIST\n", StringComparison.Ordinal),
            listed => listed,
            "the playlist of an ended session said no more is coming");

        Assert.Contains("#EXTINF:", playlist, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-PV-002, BR-PV-005: with every log turned up to its finest, no line carries the ticket or the key")]
    public async Task WithEveryLogTurnedUpNoLineCarriesTheTicketOrTheKey()
    {
        RecordedStartup heard = new();
        await using AuthProbe probe = Wiring(heard: heard);
        using HttpClient player = Player(probe);
        string ticket = await IssuedAsync(probe);
        string key = KeyIn(await (await OpenedAsync(player, WithTicket(ticket, "a.m3u8"))).Content.ReadAsStringAsync());

        using HttpResponseMessage listed = await player.GetAsync(new Uri($"/api/hls/{key}/index.m3u8", UriKind.Relative));
        using HttpResponseMessage initialisation = await player.GetAsync(new Uri($"/api/hls/{key}/init.mp4", UriKind.Relative));
        using HttpResponseMessage unknown = await player.GetAsync(new Uri($"/api/hls/{key[..^1]}A/index.m3u8", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Contains(heard.Everything, line => line.Category.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.Empty(heard.Everything
            .Where(line => line.Text.Contains(ticket, StringComparison.Ordinal) || line.Text.Contains(key, StringComparison.Ordinal))
            .Select(line => $"{line.Category} {line.Level}")
            .Distinct());
    }

    [GeneratedRegex(@"^/api/hls/([^/]+)/index\.m3u8$", RegexOptions.Multiline)]
    private static partial Regex KeyedPlaylist();

    [GeneratedRegex(@"#EXT-X-MEDIA-SEQUENCE:(\d+)")]
    private static partial Regex MediaSequence();

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
    private async Task<HttpResponseMessage> OpenedAsync(HttpClient client, string path)
    {
        Task<HttpResponseMessage> opening = client.GetAsync(new Uri(path, UriKind.Relative));

        while (!opening.IsCompleted)
        {
            fanout.Publish(Gop());
            await Task.WhenAny(opening, Task.Delay(TimeSpan.FromMilliseconds(20)));
        }

        return await opening;
    }

    private AuthProbe Wiring(LiveHlsKeys? keys = null, RecordedStartup? heard = null)
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
                wired.AddSingleton<ILiveHlsKeys>(keys);
            }
        }

        return heard is null ? AuthProbe.OverHttp(Wired) : AuthProbe.OverHttpHeardBy(heard, Wired);
    }
}
