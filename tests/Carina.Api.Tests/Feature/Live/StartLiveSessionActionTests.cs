using System.Net;
using System.Net.WebSockets;
using System.Text.Json;

using Carina.Api.Live;
using Carina.Api.Tests.Support;
using Carina.Domain.Auth;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Feature.Live;

[Trait("Category", "Feature")]
public sealed class StartLiveSessionActionTests
{
    private static readonly Subject Tester = new(TestAuthenticationHandler.Tester);

    private static readonly LiveSessionKey EveryFrame = new(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd30);

    private static readonly LiveSessionKey AnotherChannel = new(new NetworkId(32736), new ServiceId(1032), LiveProfile.Hd30);

    [Fact]
    public async Task AskingForAChannelStartsItsSessionForTheOneWhoAskedAndSaysWhatWasStarted()
    {
        SeatingAt seating = new(new HeldLiveSource());
        await using LiveFeature feature = Seating(seating);

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using HttpResponseMessage response = await feature.StartASessionAsync(new { networkId = 32736, serviceId = 1024 });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement data = body.RootElement.GetProperty("data");

        Assert.Equal(
            ["format", "networkId", "profile", "serviceId", "sound", "state", "waitsForAViewerSeconds"],
            data.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(32736, data.GetProperty("networkId").GetInt32());
        Assert.Equal(1024, data.GetProperty("serviceId").GetInt32());
        Assert.Equal("720p30", data.GetProperty("profile").GetString());
        Assert.Equal("h264", data.GetProperty("format").GetString());
        Assert.Equal("main", data.GetProperty("sound").GetString());
        Assert.Equal("started", data.GetProperty("state").GetString());
        Assert.Equal(8, data.GetProperty("waitsForAViewerSeconds").GetInt32());
        Assert.Equal([(EveryFrame, Tester)], seating.StartedAhead);
        Assert.Empty(seating.Asked);
    }

    [Fact]
    public async Task TheChannelIsReachedForWhileItsSessionIsStartedJustAsTheWireReachesForIt()
    {
        SeatingAt seating = new(new HeldLiveSource());
        await using LiveFeature feature = Seating(seating);

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using HttpResponseMessage response = await feature.StartASessionAsync(new { networkId = 32736, serviceId = 1024 });

        Assert.Equal([new LiveChannelKey(new NetworkId(32736), new ServiceId(1024))], seating.Reached);
        Assert.True(seating.ReachHeldWhileJoining);
        Assert.Equal(1, seating.ReachesLetGo);
    }

    [Fact]
    public async Task TheProfileSoundAndFormatNamedAreTheOnesStarted()
    {
        SeatingAt seating = new(new HeldLiveSource());
        await using LiveFeature feature = Seating(seating, LiveEncoder.Vaapi, [VideoCodec.H264, VideoCodec.H265]);

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using HttpResponseMessage response = await feature.StartASessionAsync(
            new { networkId = 32736, serviceId = 1024, profile = "1080p30", sound = "secondary", format = "h265" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(
            [(new LiveSessionKey(new NetworkId(32736), new ServiceId(1024), LiveProfile.FullHd30, SoundTrack.Secondary, VideoCodec.H265), Tester)],
            seating.StartedAhead);
    }

    [Theory]
    [InlineData(LiveEncoder.Software, "720p30")]
    [InlineData(LiveEncoder.Vaapi, "1080p60")]
    [InlineData(LiveEncoder.Nvenc, "1080p60")]
    public async Task AProfileLeftOutIsTheOneThisMachineEncodesAtAndTheAnswerNamesIt(LiveEncoder encoder, string profile)
    {
        SeatingAt seating = new(new HeldLiveSource());
        await using LiveFeature feature = Seating(seating, encoder);

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using HttpResponseMessage response = await feature.StartASessionAsync(new { networkId = 32736, serviceId = 1024 });
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(profile, body.RootElement.GetProperty("data").GetProperty("profile").GetString());
        Assert.Equal(profile, Assert.Single(seating.StartedAhead).Key.Profile.Name);
    }

    [Theory]
    [InlineData(LiveAhead.AlreadyRunning, "alreadyRunning")]
    [InlineData(LiveAhead.AlreadyWatched, "alreadyWatched")]
    public async Task ASessionThatWasAlreadyThereIsAnsweredTheSameWayAndSaidToHaveBeenThere(LiveAhead found, string said)
    {
        SeatingAt seating = new(new HeldLiveSource()) { AnsweringAhead = found };
        await using LiveFeature feature = Seating(seating);

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using HttpResponseMessage response = await feature.StartASessionAsync(new { networkId = 32736, serviceId = 1024 });
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(said, body.RootElement.GetProperty("data").GetProperty("state").GetString());
    }

    [Theory]
    [InlineData(32736, 1040)]
    [InlineData(32736, 9999)]
    [InlineData(32737, 1024)]
    public async Task AChannelThatCannotBeWatchedStartsNothing(int network, int service)
    {
        SeatingAt seating = new(new HeldLiveSource());
        await using LiveFeature feature = Seating(seating);

        feature.Seed(1024, "Alpha", remoteControlKey: 1);
        feature.Seed(1040, "Unselected", remoteControlKey: 2, selected: false);

        using HttpResponseMessage response = await feature.StartASessionAsync(new { networkId = network, serviceId = service });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(seating.StartedAhead);
        Assert.Empty(seating.Reached);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"networkId\":32736}")]
    [InlineData("{\"networkId\":-1,\"serviceId\":1024}")]
    [InlineData("{\"networkId\":32736,\"serviceId\":65536}")]
    [InlineData("{\"networkId\":\"thirty\",\"serviceId\":1024}")]
    [InlineData("{\"networkId\":32736,\"serviceId\":1024,\"profile\":\"hls\"}")]
    [InlineData("{\"networkId\":32736,\"serviceId\":1024,\"sound\":\"sub\"}")]
    [InlineData("{\"networkId\":32736,\"serviceId\":1024,\"format\":\"vp9\"}")]
    public async Task ASessionAskedForWithoutAWholeChannelOrWithSomethingThereIsNotIsRefused(string body)
    {
        SeatingAt seating = new(new HeldLiveSource());
        await using LiveFeature feature = Seating(seating);

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using StringContent content = new(body, System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await feature.Client.PostAsync(new Uri("/api/live/sessions", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(seating.StartedAhead);
        Assert.Empty(seating.Reached);
    }

    [Fact]
    public async Task AFormatThisMachineDoesNotTranscodeIsRefusedAsTheWireRefusesIt()
    {
        SeatingAt seating = new(new HeldLiveSource());
        await using LiveFeature feature = Seating(seating);

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using HttpResponseMessage response = await feature.StartASessionAsync(
            new { networkId = 32736, serviceId = 1024, format = "h265" });
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("h265", body.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(seating.StartedAhead);
        Assert.Empty(seating.Reached);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task ASessionAskedForInAnythingButJsonIsRefusedAsUnsupportedMedia(string mediaType)
    {
        SeatingAt seating = new(new HeldLiveSource());
        await using LiveFeature feature = Seating(seating);

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using HttpResponseMessage response = await feature.StartASessionAsync(new { networkId = 32736, serviceId = 1024 }, mediaType);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(seating.StartedAhead);
    }

    [Fact]
    public async Task ACallerCarryingNoCredentialsStartsNothing()
    {
        SeatingAt seating = new(new HeldLiveSource());
        await using LiveFeature feature = Seating(seating);

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using StringContent content = new("{\"networkId\":32736,\"serviceId\":1024}", System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await feature.Anonymous.PostAsync(new Uri("/api/live/sessions", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(seating.StartedAhead);
        Assert.Empty(seating.Reached);
    }

    [Fact]
    public async Task ARequestFromAnotherOriginStartsNothing()
    {
        SeatingAt seating = new(new HeldLiveSource());
        await using LiveFeature feature = Seating(seating);

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using HttpRequestMessage asking = new(HttpMethod.Post, new Uri("/api/live/sessions", UriKind.Relative))
        {
            Content = new StringContent("{\"networkId\":32736,\"serviceId\":1024}", System.Text.Encoding.UTF8, "application/json"),
        };

        asking.Headers.Add(HeaderNames.Origin, "https://elsewhere.example");

        using HttpResponseMessage response = await feature.Client.SendAsync(asking);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(seating.StartedAhead);
    }

    [Fact]
    public async Task TheSessionIsStartedOnTheTunerAndTheTranscoderWithNoWireOpenAndAskingAgainStartsNothingMore()
    {
        Rig rig = new();
        await using AuthProbe probe = rig.Wiring();

        await probe.SignedInCookieAsync();

        using HttpResponseMessage first = await StartAsync(probe);

        Assert.True(first.StatusCode is HttpStatusCode.Accepted, await first.Content.ReadAsStringAsync());
        await Eventually.Happens(
            () => rig.Supply.Opened.Count is 1 && rig.Transcoders.Started is 1,
            "the tuner is asked for and the transcoder started with no wire open");

        using HttpResponseMessage again = await StartAsync(probe);
        using JsonDocument said = JsonDocument.Parse(await again.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal("alreadyRunning", said.RootElement.GetProperty("data").GetProperty("state").GetString());
        Assert.Equal(1, rig.Supply.Asked);
        Assert.Equal(1, rig.Transcoders.Started);
        Assert.Equal(0, Sessions(probe).Viewers(EveryFrame));
    }

    [Fact]
    public async Task TheWireOpenedAfterwardsJoinsTheSessionAlreadyStartingAndIsTheOnlyViewerCounted()
    {
        Rig rig = new();
        await using AuthProbe probe = rig.Wiring();
        string cookie = await probe.SignedInCookieAsync();

        using HttpResponseMessage started = await StartAsync(probe);

        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        await Eventually.Happens(() => rig.Supply.Asked is 1, "the tuner is asked for before the wire is opened");

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(
            new Uri($"ws://localhost{LiveWire.Path}?network=32736&service=1024&profile=720p30"),
            CancellationToken.None);

        await Eventually.Happens(() => Sessions(probe).Viewers(EveryFrame) is 1, "the wire is seated on the session");

        Assert.Equal(1, rig.Supply.Asked);
        Assert.Equal(1, rig.Transcoders.Started);
        Assert.Equal([EveryFrame], Sessions(probe).Keys);
    }

    [Fact]
    public async Task AWireOpenedForAnotherChannelHasTheSessionStartedAheadStoppedAndItsTunerGivenBack()
    {
        Rig rig = new();
        await using AuthProbe probe = rig.Wiring();
        string cookie = await probe.SignedInCookieAsync();

        using HttpResponseMessage started = await StartAsync(probe);

        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        await Eventually.Happens(() => rig.Supply.Opened.Count is 1, "the session started ahead secures its tuner");

        using WebSocket socket = await Carrying(probe, cookie).ConnectAsync(
            new Uri($"ws://localhost{LiveWire.Path}?network=32736&service=1032&profile=720p30"),
            CancellationToken.None);

        await Eventually.Happens(
            () => rig.Supply.Opened.Count is 2 && rig.Supply.Opened[0].Disposed && rig.Transcoders.Raised[0].Disposed,
            "the session started ahead is stopped once the channel the viewer turned to has its tuner");
        Assert.False(rig.Supply.Opened[1].Disposed);
        Assert.Equal([AnotherChannel], Sessions(probe).Keys);
    }

    [Fact]
    public async Task ASessionNobodyJoinsWithinTheWaitIsStoppedItsTunerGivenBackAndNoDepartureIsCounted()
    {
        Rig rig = new(new LiveSessionSettings(longestWaitForAViewer: TimeSpan.FromMilliseconds(200)));
        await using AuthProbe probe = rig.Wiring();

        await probe.SignedInCookieAsync();

        using HttpResponseMessage started = await StartAsync(probe);

        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        await Eventually.Happens(
            () => rig.Supply.Opened.Count is 1 && rig.Supply.Opened[0].Disposed && rig.Budget.Running is 0,
            "the tuner is given back and the transcoder stopped once nobody came within the wait");

        using HttpResponseMessage asked = await probe.Client.GetAsync(new Uri("/api/live/departures", UriKind.Relative));
        using JsonDocument departures = JsonDocument.Parse(await asked.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.All(
            departures.RootElement.GetProperty("data").GetProperty("departures").EnumerateArray(),
            counted => Assert.Equal(0L, counted.GetProperty("times").GetInt64()));
        Assert.Empty(Sessions(probe).Keys);
        Assert.True(rig.Transcoders.Raised[0].Disposed);
    }

    [Fact]
    public async Task ASessionStartedAheadIsListedWithNoViewer()
    {
        Rig rig = new();
        await using AuthProbe probe = rig.Wiring();

        await probe.SignedInCookieAsync();

        using HttpResponseMessage started = await StartAsync(probe);

        await Eventually.Happens(() => rig.Supply.Opened.Count is 1, "the session started ahead secures its tuner");

        using HttpResponseMessage asked = await probe.Client.GetAsync(new Uri("/api/live/sessions", UriKind.Relative));
        using JsonDocument listed = JsonDocument.Parse(await asked.Content.ReadAsStringAsync());
        JsonElement session = Assert.Single(listed.RootElement.GetProperty("data").EnumerateArray());

        Assert.Equal(1024, session.GetProperty("serviceId").GetInt32());
        Assert.Equal(0, session.GetProperty("viewers").GetInt32());
        Assert.Empty(session.GetProperty("watching").EnumerateArray());
    }

    private static LiveFeature Seating(
        SeatingAt seating,
        LiveEncoder encoder = LiveEncoder.Software,
        IReadOnlyList<VideoCodec>? formats = null)
        => new(
            encoder,
            formats,
            services =>
            {
                services.AddSingleton<ILiveSessionManager>(seating);
                services.AddSingleton<ISoundOnAir>(new HeldSoundOnAir());
            });

    private static Task<HttpResponseMessage> StartAsync(AuthProbe probe)
        => probe.Client.PostAsJsonAsync(
            new Uri("/api/live/sessions", UriKind.Relative),
            new { networkId = 32736, serviceId = 1024 });

    private static LiveSessionManager Sessions(AuthProbe probe)
        => Assert.IsType<LiveSessionManager>(probe.Wired.Services.GetRequiredService<ILiveSessionManager>());

    private static WebSocketClient Carrying(AuthProbe probe, string cookie)
    {
        WebSocketClient client = probe.Wired.Server.CreateWebSocketClient();

        client.ConfigureRequest += request => request.Headers[HeaderNames.Cookie] = cookie;

        return client;
    }

    private sealed class Rig
    {
        private readonly LiveSessionSettings? settings;

        private readonly HeldServices services = new();

        private readonly HeldCandidates candidates = new();

        public Rig(LiveSessionSettings? settings = null)
        {
            this.settings = settings;
            Transcoders = new HeldTranscoders(Budget);
            Seed(1024);
            Seed(1032);
        }

        public PipedSupply Supply { get; } = new();

        public TranscodeBudget Budget { get; } = new(new TranscodeBudgetSettings { AtOnce = 4 });

        public HeldTranscoders Transcoders { get; }

        public AuthProbe Wiring()
            => AuthProbe.OverHttp(wired =>
            {
                wired.AddSingleton<IBroadcastServiceRepository>(services);
                wired.AddSingleton<ICandidateChannelRepository>(candidates);
                wired.AddSingleton<ILiveSupply>(Supply);
                wired.AddSingleton<ITranscodeBudget>(Budget);
                wired.AddSingleton<ILiveTranscoderFactory>(Transcoders);
                wired.AddSingleton<ILiveEncoderSelector>(new AlreadyChosen(LiveEncoder.Software));

                if (settings is not null)
                {
                    wired.AddSingleton(settings);
                }
            });

        private void Seed(int serviceId)
        {
            NetworkId network = new(32736);
            ServiceId service = new(serviceId);
            CandidateChannel candidate = CandidateChannel.Discover(
                CandidateChannelId.New(),
                network,
                service,
                TuningParameters.Terrestrial(27),
                LiveFeature.At);

            candidate.Select(SelectionSource.Manual, null, LiveFeature.At);
            services.Services.Add(BroadcastService.Discover(network, service, "Watched", ServiceCategory.Television, LiveFeature.At));
            candidates.Candidates.Add(candidate);
        }
    }
}
