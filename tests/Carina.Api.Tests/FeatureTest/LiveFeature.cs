using System.Net;
using System.Text;
using System.Text.Json;

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

    public Task<IReadOnlyList<LiveSessionView>> RunningAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LiveSessionView>>([.. Sessions]);
}

internal sealed class HeldDepartures : ILiveDepartureLedger
{
    public LiveDepartureTally Tally { get; set; } = new(
        LiveFeature.At,
        [.. Enum.GetValues<LiveDeparture>().Select(departure => new LiveDepartureCount(departure, 0L, null))]);

    public void Note(LiveSessionKey key, LiveDeparture departure, TimeSpan carried)
    {
    }

    public LiveDepartureTally Read() => Tally;

    public void Counted(
        LiveDeparture departure,
        long times,
        DateTime? lastAt,
        TimeSpan? shortest = null,
        TimeSpan? longest = null)
        => Tally = new LiveDepartureTally(
            Tally.Since,
            [
                .. Tally.Counted.Select(counted => counted.Departure == departure
                    ? new LiveDepartureCount(departure, times, lastAt, shortest, longest)
                    : counted),
            ]);
}

internal sealed class LiveFeature : IAsyncDisposable
{
    public static readonly DateTime At = new(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public LiveFeature(LiveEncoder encoder = LiveEncoder.Software, IReadOnlyList<VideoCodec>? formats = null)
    {
        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IBroadcastServiceRepository>(Services);
                services.AddSingleton<ICandidateChannelRepository>(Candidates);
                services.AddSingleton<ILiveSessionLedger>(Ledger);
                services.AddSingleton<IPlaybackTicketStore>(Tickets);
                services.AddSingleton<ILiveEncoderSelector>(new AlreadyChosen(encoder, formats));
                services.AddSingleton<ILiveDepartureLedger>(Departures);
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

    public HeldDepartures Departures { get; } = new();

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

    public void Watching(int serviceId, LiveProfile profile, VideoCodec format, int viewers)
        => Ledger.Sessions.Add(new LiveSessionView(
            new LiveSessionKey(new NetworkId(32736), new ServiceId(serviceId), profile, SoundTrack.Main, format),
            viewers,
            LiveStartup.NotStarted,
            0L,
            0,
            null,
            null));

    public void Watching(int serviceId, LiveProfile profile, int viewers, long dropped = 0L)
        => Watching(
            serviceId,
            profile,
            viewers,
            LiveStartup.NotStarted.Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromMilliseconds(9)),
            dropped);

    public void Watching(
        int serviceId,
        LiveProfile profile,
        int viewers,
        LiveStartup startup,
        long dropped = 0L,
        int queued = 0,
        IReadOnlyList<LiveBacklog>? watching = null,
        long? chunksDroppedSinceTheSupplyOpened = null)
        => Ledger.Sessions.Add(new LiveSessionView(
            new LiveSessionKey(new NetworkId(32736), new ServiceId(serviceId), profile),
            viewers,
            startup,
            dropped,
            queued,
            watching,
            chunksDroppedSinceTheSupplyOpened));

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

    public PlaybackTicket? Take(string? offered, PlaybackTarget target) => null;
}
