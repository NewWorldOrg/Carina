using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Carina.Api.Live;
using Carina.Domain.Auth;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class HeldLiveLedger : ILiveSessionLedger
{
    public List<LiveSessionView> Sessions { get; } = [];

    public IReadOnlyList<LiveSessionView> Running => [.. Sessions];
}

internal sealed class LiveFeature : IAsyncDisposable
{
    public static readonly DateTime At = new(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public LiveFeature()
    {
        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IBroadcastServiceRepository>(Services);
                services.AddSingleton<ICandidateChannelRepository>(Candidates);
                services.AddSingleton<ILiveSessionLedger>(Ledger);
                services.AddSingleton<IPlaybackTicketStore>(Tickets);
            }));

        Client = configured.CreateAuthenticatedClient();
        Anonymous = configured.WithTestScheme().CreateClient();
    }

    public HttpClient Client { get; }

    public HttpClient Anonymous { get; }

    public HeldServices Services { get; } = new();

    public HeldCandidates Candidates { get; } = new();

    public HeldLiveLedger Ledger { get; } = new();

    public HeldPlaybackTickets Tickets { get; } = new();

    public BroadcastService Seed(
        int serviceId,
        string name,
        int? remoteControlKey = null,
        bool selected = true,
        ServiceCategory category = ServiceCategory.Television)
    {
        BroadcastService service = BroadcastService.Discover(new NetworkId(32736), new ServiceId(serviceId), name, category, At);

        service.RemoteControlledBy(remoteControlKey);
        Services.Services.Add(service);

        CandidateChannel candidate = CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(32736),
            new ServiceId(serviceId),
            TuningParameters.Terrestrial(27),
            At);

        if (selected)
        {
            candidate.Select(SelectionSource.Manual, null, At);
        }

        Candidates.Candidates.Add(candidate);

        return service;
    }

    public void Watching(int serviceId, LiveProfile profile, int viewers, long dropped = 0L)
        => Watching(
            serviceId,
            profile,
            viewers,
            LiveStartup.NotStarted.Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromMilliseconds(9)),
            dropped);

    public void Watching(int serviceId, LiveProfile profile, int viewers, LiveStartup startup, long dropped = 0L)
        => Ledger.Sessions.Add(new LiveSessionView(
            new LiveSessionKey(new NetworkId(32736), new ServiceId(serviceId), profile),
            viewers,
            startup,
            dropped));

    public async Task<(HttpStatusCode Status, JsonDocument Body)> GetAsync(string path)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri(path, UriKind.Relative));

        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()));
    }

    public async Task<HttpResponseMessage> AskForATicketAsync(object body, string mediaType = "application/json")
    {
        using StringContent content = new(JsonSerializer.Serialize(body), Encoding.UTF8, mediaType);

        return await Client.PostAsync(new Uri("/api/live/ticket", UriKind.Relative), content);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Anonymous.Dispose();
        await factory.DisposeAsync();
    }
}

internal sealed class HeldPlaybackTickets : IPlaybackTicketStore
{
    public List<(Subject Subject, PlaybackTarget Target)> Issued { get; } = [];

    public bool Full { get; set; }

    public IssuedPlaybackTicket? Issue(Subject subject, PlaybackTarget target)
    {
        if (Full)
        {
            return null;
        }

        Issued.Add((subject, target));

        return new IssuedPlaybackTicket(Unguessable.Issue(), LiveFeature.At.AddSeconds(30));
    }

    public Subject? Spend(string? offered, PlaybackTarget target) => null;
}

[Collection(FeatureTestCollection.Name)]
public sealed class LiveEndpointTests
{
    [Theory]
    [InlineData("GET", "/api/live/channels")]
    [InlineData("GET", "/api/live/profiles")]
    [InlineData("GET", "/api/live/sessions")]
    [InlineData("POST", "/api/live/ticket")]
    public async Task EveryLiveSurfaceRefusesACallerCarryingNoCredentialsWithoutABodyAndWithoutRedirecting(string method, string path)
    {
        await using LiveFeature feature = new();

        using HttpRequestMessage asking = new(new HttpMethod(method), new Uri(path, UriKind.Relative));

        if (method is "POST")
        {
            asking.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await feature.Anonymous.SendAsync(asking);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task TheChannelsThatCanBeWatchedAreTheTelevisionServicesWithASelectedChannelInRemoteControlOrder()
    {
        await using LiveFeature feature = new();

        feature.Seed(1032, "Second", remoteControlKey: 2);
        feature.Seed(1024, "First", remoteControlKey: 1);
        feature.Seed(1040, "Unselected", remoteControlKey: 3, selected: false);
        feature.Seed(1048, "Radio", remoteControlKey: 4, category: ServiceCategory.Radio);
        feature.Seed(1056, "Data", remoteControlKey: 5, category: ServiceCategory.Data);
        feature.Seed(1064, "Keyless");

        (HttpStatusCode status, JsonDocument body) = await feature.GetAsync("/api/live/channels");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement data = body.RootElement.GetProperty("data");

        Assert.Equal(["First", "Second", "Keyless"], Names(data));
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        Assert.Equal(1, data.GetProperty("currentPage").GetInt32());
        Assert.Equal(LiveChannelQuery.DefaultPerPage, data.GetProperty("perPage").GetInt32());
    }

    [Fact]
    public async Task AChannelSaysHowManyAreWatchingItAcrossEveryProfile()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Watched", remoteControlKey: 1);
        feature.Seed(1032, "Quiet", remoteControlKey: 2);
        feature.Watching(1024, LiveProfile.Hd30, 2);
        feature.Watching(1024, LiveProfile.Hd60, 1);

        (_, JsonDocument body) = await feature.GetAsync("/api/live/channels");

        JsonElement[] items = [.. body.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()];

        Assert.Equal(3, items[0].GetProperty("viewers").GetInt32());
        Assert.Equal(0, items[1].GetProperty("viewers").GetInt32());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("sessions").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("tuning").ValueKind);
    }

    [Fact]
    public async Task TheFieldsAskedForAreAnsweredAndNoOthers()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Watched", remoteControlKey: 1);
        feature.Watching(1024, LiveProfile.Hd30, 2, dropped: 28L);

        (_, JsonDocument body) = await feature.GetAsync("/api/live/channels?fields=sessions&fields=tuning");

        JsonElement item = body.RootElement.GetProperty("data").GetProperty("items")[0];
        JsonElement session = item.GetProperty("sessions")[0];

        Assert.Equal("720p30", session.GetProperty("profile").GetString());
        Assert.Equal(2, session.GetProperty("viewers").GetInt32());
        Assert.Equal(28L, session.GetProperty("dropped").GetInt64());
        Assert.True(session.GetProperty("startup").GetProperty("inProgress").GetBoolean());
        Assert.Equal(27, item.GetProperty("tuning").GetProperty("physicalChannel").GetInt32());
    }

    [Theory]
    [InlineData("sort=name", new[] { "Alpha", "Beta", "Gamma" })]
    [InlineData("sort=name&descending=true", new[] { "Gamma", "Beta", "Alpha" })]
    [InlineData("sort=viewers&descending=true", new[] { "Beta", "Gamma", "Alpha" })]
    [InlineData("sort=remoteControlKey", new[] { "Gamma", "Alpha", "Beta" })]
    public async Task TheSortsOnTheListOrderTheChannels(string query, string[] names)
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 2);
        feature.Seed(1032, "Beta", remoteControlKey: 3);
        feature.Seed(1040, "Gamma", remoteControlKey: 1);
        feature.Watching(1032, LiveProfile.Hd30, 3);
        feature.Watching(1040, LiveProfile.Hd30, 1);

        (HttpStatusCode status, JsonDocument body) = await feature.GetAsync("/api/live/channels?" + query);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(names, Names(body.RootElement.GetProperty("data")));
    }

    [Theory]
    [InlineData("sort=99")]
    [InlineData("sort=alphabetically")]
    [InlineData("fields=99")]
    [InlineData("fields=everything")]
    [InlineData("fields=sessions&fields=everything")]
    [InlineData("page=0")]
    [InlineData("page=abc")]
    [InlineData("perPage=abc")]
    [InlineData("descending=perhaps")]
    public async Task AValueOffTheListIsRefusedRatherThanIgnored(string query)
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        (HttpStatusCode status, _) = await feature.GetAsync("/api/live/channels?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task APageSizeOverTheCeilingIsCutDownToItAndAnsweredAsTheSizeThatWasUsed()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        (HttpStatusCode status, JsonDocument body) = await feature.GetAsync("/api/live/channels?perPage=5000");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(LiveChannelQuery.MostPerPage, body.RootElement.GetProperty("data").GetProperty("perPage").GetInt32());
    }

    [Fact]
    public async Task ChannelsArePagedInTheOrderAsked()
    {
        await using LiveFeature feature = new();

        for (int key = 1; key <= 5; key++)
        {
            feature.Seed(1024 + key, $"Channel {key}", remoteControlKey: key);
        }

        (_, JsonDocument body) = await feature.GetAsync("/api/live/channels?perPage=2&page=2");

        JsonElement data = body.RootElement.GetProperty("data");

        Assert.Equal(["Channel 3", "Channel 4"], Names(data));
        Assert.Equal(5, data.GetProperty("total").GetInt32());
        Assert.Equal(3, data.GetProperty("lastPage").GetInt32());
    }

    [Fact]
    public async Task TheProfilesAreTheOnesTheWireAcceptsAndNothingElse()
    {
        await using LiveFeature feature = new();

        (HttpStatusCode status, JsonDocument body) = await feature.GetAsync("/api/live/profiles");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement[] profiles = [.. body.RootElement.GetProperty("data").EnumerateArray()];

        Assert.Equal(
            LiveProfile.All.Select(profile => profile.Name),
            profiles.Select(profile => profile.GetProperty("name").GetString()));
        Assert.All(profiles, profile => Assert.NotNull(LiveProfile.Find(profile.GetProperty("name").GetString())));

        JsonElement hd30 = profiles.Single(profile => profile.GetProperty("name").GetString() == "720p30");

        Assert.Equal("h264", hd30.GetProperty("codec").GetString());
        Assert.Equal(1280, hd30.GetProperty("width").GetInt32());
        Assert.Equal(720, hd30.GetProperty("height").GetInt32());
        Assert.Equal(30000, hd30.GetProperty("frameRate").GetProperty("numerator").GetInt32());
        Assert.Equal(1001, hd30.GetProperty("frameRate").GetProperty("denominator").GetInt32());
        Assert.Equal(3000, hd30.GetProperty("softwareKilobitsPerSecond").GetInt32());
        Assert.Equal(24, hd30.GetProperty("vaapiQuantiser").GetInt32());
    }

    [Fact]
    public async Task TheSessionsAreTheOnesRunningWithTheirViewersTheirStartupAndWhatTheyThrewAway()
    {
        await using LiveFeature feature = new();

        feature.Watching(1032, LiveProfile.Hd60, 1, dropped: 3L);
        feature.Watching(1024, LiveProfile.Hd30, 2, dropped: 28L);

        (HttpStatusCode status, JsonDocument body) = await feature.GetAsync("/api/live/sessions");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement[] sessions = [.. body.RootElement.GetProperty("data").EnumerateArray()];

        Assert.Equal([1024, 1032], sessions.Select(session => session.GetProperty("serviceId").GetInt32()));
        Assert.Equal(["720p30", "720p60"], sessions.Select(session => session.GetProperty("profile").GetString()));
        Assert.Equal([2, 1], sessions.Select(session => session.GetProperty("viewers").GetInt32()));
        Assert.Equal([28L, 3L], sessions.Select(session => session.GetProperty("dropped").GetInt64()));

        JsonElement startup = sessions[0].GetProperty("startup");
        JsonElement[] marks = [.. startup.GetProperty("marks").EnumerateArray()];

        Assert.True(startup.GetProperty("inProgress").GetBoolean());
        Assert.Equal(
            ["tunerSecured", "channelLocked", "transcoderStarted", "initReached", "firstPicture"],
            marks.Select(mark => mark.GetProperty("segment").GetString()));
        Assert.Equal(9L, marks[2].GetProperty("reachedAtMs").GetInt64());
        Assert.Equal(JsonValueKind.Null, marks[4].GetProperty("reachedAtMs").ValueKind);
    }

    [Fact]
    public async Task AStartupWhoseChannelLockedAfterTheTranscoderStartedReportsNoNegativeIntervalAndSaysWhatEachWaitedFor()
    {
        await using LiveFeature feature = new();

        feature.Watching(
            1024,
            LiveProfile.Hd30,
            1,
            LiveStartup.NotStarted
                .Reaching(LiveStartupSegment.TunerSecured, TimeSpan.FromMilliseconds(485))
                .Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromMilliseconds(495))
                .Reaching(LiveStartupSegment.ChannelLocked, TimeSpan.FromMilliseconds(733))
                .Reaching(LiveStartupSegment.InitReached, TimeSpan.FromMilliseconds(4366))
                .Reaching(LiveStartupSegment.FirstPicture, TimeSpan.FromMilliseconds(4368)));

        (HttpStatusCode status, JsonDocument body) = await feature.GetAsync("/api/live/sessions");

        Assert.Equal(HttpStatusCode.OK, status);

        JsonElement startup = body.RootElement.GetProperty("data")[0].GetProperty("startup");
        JsonElement[] marks = [.. startup.GetProperty("marks").EnumerateArray()];

        Assert.False(startup.GetProperty("inProgress").GetBoolean());
        Assert.Equal([485L, 248L, 10L, 3633L, 2L], marks.Select(mark => mark.GetProperty("tookMs").GetInt64()));
        Assert.All(marks, mark => Assert.True(mark.GetProperty("tookMs").GetInt64() >= 0));
        Assert.Equal(JsonValueKind.Null, marks[0].GetProperty("tookFrom").ValueKind);
        Assert.Equal("tunerSecured", marks[1].GetProperty("tookFrom").GetString());
        Assert.Equal("tunerSecured", marks[2].GetProperty("tookFrom").GetString());
        Assert.Equal("channelLocked", marks[3].GetProperty("tookFrom").GetString());
        Assert.Equal("initReached", marks[4].GetProperty("tookFrom").GetString());
    }

    [Fact]
    public async Task NoSessionsIsAnEmptyListRatherThanARefusal()
    {
        await using LiveFeature feature = new();

        (HttpStatusCode status, JsonDocument body) = await feature.GetAsync("/api/live/sessions");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(body.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task ATicketIsIssuedForAChannelThatCanBeWatchedAndNamesThatChannelAlone()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using HttpResponseMessage response = await feature.AskForATicketAsync(new { networkId = 32736, serviceId = 1024 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement data = body.RootElement.GetProperty("data");

        Assert.False(string.IsNullOrEmpty(data.GetProperty("inTheClear").GetString()));
        Assert.Equal(["inTheClear", "lapsesAt"], data.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        (Subject subject, PlaybackTarget target) = Assert.Single(feature.Tickets.Issued);

        Assert.Equal(new Subject(TestAuthenticationHandler.Tester), subject);
        Assert.Equal(PlaybackTarget.LiveChannel("32736-1024"), target);
        Assert.Equal(PlaybackTargetKind.LiveChannel, target.Kind);
        Assert.NotEqual(PlaybackTarget.Recording("32736-1024"), target);
    }

    [Theory]
    [InlineData(32736, 1040)]
    [InlineData(32736, 9999)]
    [InlineData(32737, 1024)]
    public async Task AChannelThatCannotBeWatchedGetsNoTicket(int network, int service)
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);
        feature.Seed(1040, "Unselected", remoteControlKey: 2, selected: false);

        using HttpResponseMessage response = await feature.AskForATicketAsync(new { networkId = network, serviceId = service });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(feature.Tickets.Issued);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"networkId\":32736}")]
    [InlineData("{\"networkId\":-1,\"serviceId\":1024}")]
    [InlineData("{\"networkId\":32736,\"serviceId\":65536}")]
    [InlineData("{\"networkId\":\"thirty\",\"serviceId\":1024}")]
    public async Task ATicketAskedForWithoutAWholeChannelIsRefused(string body)
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using StringContent content = new(body, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await feature.Client.PostAsync(new Uri("/api/live/ticket", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(feature.Tickets.Issued);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data")]
    public async Task ATicketAskedForInAnythingButJsonIsRefusedAsUnsupportedMedia(string mediaType)
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using HttpResponseMessage response = await feature.AskForATicketAsync(new { networkId = 32736, serviceId = 1024 }, mediaType);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(feature.Tickets.Issued);
    }

    [Fact]
    public async Task TooManyOutstandingTicketsIsSaidAsTooManyRequests()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);
        feature.Tickets.Full = true;

        using HttpResponseMessage response = await feature.AskForATicketAsync(new { networkId = 32736, serviceId = 1024 });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task ATicketIsNotHandedToTheWire()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using HttpResponseMessage ticket = await feature.AskForATicketAsync(new { networkId = 32736, serviceId = 1024 });
        using JsonDocument body = JsonDocument.Parse(await ticket.Content.ReadAsStringAsync());
        string inTheClear = body.RootElement.GetProperty("data").GetProperty("inTheClear").GetString()!;

        using HttpRequestMessage asking = new(
            HttpMethod.Get,
            new Uri(LiveWire.Path + "?network=32736&service=1024&profile=720p30", UriKind.Relative));

        asking.Headers.TryAddWithoutValidation("Authorization", $"Bearer {inTheClear}");

        using HttpResponseMessage response = await feature.Anonymous.SendAsync(asking);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string[] Names(JsonElement data)
        => [.. data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!)];
}
