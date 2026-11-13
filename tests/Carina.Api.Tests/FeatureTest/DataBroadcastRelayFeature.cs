using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using Carina.Api.Authentication;
using Carina.Api.Live;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Driver;
using Carina.Infrastructure.DataBroadcast;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

using Puppis.Contracts;

namespace Carina.Api.Tests.FeatureTest;

internal sealed record AskedOfTheRelay(string Channel, string Uri, string? Text, string? Charset);

/// <summary>
/// The relay as the HTTP surface sees it: it keeps what it was asked and answers what it is told to.
/// </summary>
internal sealed class ScriptedRelay : IBroadcasterRelay
{
    public ConcurrentQueue<AskedOfTheRelay> Asked { get; } = new();

    public RelayedText Transmitted { get; set; } = new(RelayResult.Answered, 200, "1");

    public RelayedResource Fetched { get; set; } = RelayedResource.Failed(RelayResult.NameNotResolved);

    public Task<RelayedText> TransmitAsync(string channel, string uri, string text, string charset, CancellationToken cancellationToken)
    {
        Asked.Enqueue(new AskedOfTheRelay(channel, uri, text, charset));

        return Task.FromResult(Transmitted);
    }

    public Task<RelayedResource> FetchAsync(string channel, string uri, CancellationToken cancellationToken)
    {
        Asked.Enqueue(new AskedOfTheRelay(channel, uri, null, null));

        return Task.FromResult(Fetched);
    }
}

/// <summary>
/// The driver as the HTTP surface asks it which card answered: it counts the asking and answers what it is told to.
/// </summary>
internal sealed class ScriptedCardReader : IConditionalAccessCardReader
{
    private int asked;

    public int Asked => Volatile.Read(ref asked);

    public DriverCall<ConditionalAccessCardDto> Answer { get; set; } =
        DriverCall<ConditionalAccessCardDto>.Reached(new ConditionalAccessCardDto { CaSystemId = 5, CardId = "0A0B0C0D0E0F" });

    public Task<DriverCall<ConditionalAccessCardDto>> GetConditionalAccessCardAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref asked);

        return Task.FromResult(Answer);
    }
}

/// <summary>
/// The network as the application's own relay sees it: it keeps every name it is asked to resolve and every address
/// it is asked to connect to, and reaches nothing.
/// </summary>
internal sealed class UnreachedNetwork : IRelayNetwork
{
    public ConcurrentQueue<string> Asked { get; } = new();

    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        Asked.Enqueue(host);

        return Task.FromException<IReadOnlyList<IPAddress>>(new SocketException((int)SocketError.HostNotFound));
    }

    public IReadOnlyCollection<IPAddress> OwnAddresses() => [];

    public Task<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        Asked.Enqueue(endpoint.ToString());

        return Task.FromException<Stream>(new SocketException((int)SocketError.ConnectionRefused));
    }
}

internal sealed class DataBroadcastRelayFeature : IAsyncDisposable
{
    public const string ReceiverId = "/api/live/32736-1024/data-broadcast/receiver-id";

    public const string Transmit = "/api/live/32736-1024/data-broadcast/transmit";

    public const string Fetch = "/api/live/32736-1024/data-broadcast/fetch";

    public static readonly DateTime Noon = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public DataBroadcastRelayFeature()
        : this(null)
    {
    }

    private DataBroadcastRelayFeature(string? publicOrigin)
    {
        Built = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(PublicOrigin.Key, publicOrigin ?? string.Empty);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<IBroadcastServiceRepository>(Services);
                services.AddSingleton<ICandidateChannelRepository>(Candidates);
                services.AddSingleton<IConditionalAccessCardReader>(Cards);
                services.AddSingleton<IRelayNetwork>(Network);

                if (publicOrigin is null)
                {
                    services.AddSingleton<IBroadcasterRelay>(Relay);
                }
            });
        });

        Client = Built.CreateAuthenticatedClient();
        Seed(1024, ServiceCategory.Television);
        Seed(1025, ServiceCategory.Radio);
    }

    public WebApplicationFactory<Program> Built { get; }

    public HttpClient Client { get; }

    public MovingClock Clock { get; } = new(Noon);

    public HeldServices Services { get; } = new();

    public HeldCandidates Candidates { get; } = new();

    public ScriptedRelay Relay { get; } = new();

    public UnreachedNetwork Network { get; } = new();

    public ScriptedCardReader Cards { get; } = new();

    public static DataBroadcastRelayFeature ThroughTheRelay(string publicOrigin) => new(publicOrigin);

    public HttpClient Anonymous() => Built.WithTestScheme().CreateClient();

    public void Allow(string said = DataBroadcastLineCookie.Allows)
        => Client.DefaultRequestHeaders.Add(HeaderNames.Cookie, $"{DataBroadcastLineCookie.Name}={said}");

    public void AllowAndHand(string line = DataBroadcastLineCookie.Allows, string id = DataBroadcastReceiverIdCookie.Hands)
        => Client.DefaultRequestHeaders.Add(
            HeaderNames.Cookie,
            $"{DataBroadcastLineCookie.Name}={line}; {DataBroadcastReceiverIdCookie.Name}={id}");

    public async Task<(HttpStatusCode Status, JsonElement Body)> TransmitAsync(object body, string path = Transmit)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body);

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> TransmitAsync(string json)
    {
        using StringContent content = new(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Client.PostAsync(new Uri(Transmit, UriKind.Relative), content);

        return await ReadAsync(response);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }

    public static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        if (!body.StartsWith('{'))
        {
            return (response.StatusCode, default);
        }

        using JsonDocument document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.Clone());
    }

    private void Seed(int serviceId, ServiceCategory category)
    {
        BroadcastService service = BroadcastService.Discover(new NetworkId(32736), new ServiceId(serviceId), $"Channel {serviceId}", category, Noon);
        CandidateChannel candidate = CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(32736),
            new ServiceId(serviceId),
            TuningParameters.Terrestrial(27),
            Noon);

        candidate.Select(SelectionSource.Manual, null, Noon);
        Services.Services.Add(service);
        Candidates.Candidates.Add(candidate);
    }
}
