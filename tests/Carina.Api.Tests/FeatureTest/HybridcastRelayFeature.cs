using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

using Carina.Api.Live;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

/// <summary>
/// The Hybridcast relay as the HTTP surface sees it: it keeps what it was asked, answers what it is told to, and
/// carries a WebSocket by echoing what the application sends until the application closes.
/// </summary>
internal sealed class ScriptedHybridcastRelay : IHybridcastRelay
{
    public ConcurrentQueue<string> Asked { get; } = new();

    public RelayedAit Ait { get; set; } = new(RelayResult.Answered, 200, [HybridcastRelayFeature.Application()]);

    public Func<Uri, RelayedBody> Fetched { get; set; } = address => new RelayedBody(RelayResult.Answered, 200, "text/plain", "x"u8.ToArray(), address);

    public ConcurrentQueue<PostedBody> Posts { get; } = new();

    public Task<RelayedAit> ReadAitAsync(string channel, string uri, CancellationToken cancellationToken)
    {
        Asked.Enqueue($"ait {channel} {uri}");

        return Task.FromResult(Ait);
    }

    public Task<RelayedBody> FetchAsync(string channel, Uri address, HybridcastBoundary boundary, CancellationToken cancellationToken)
    {
        Asked.Enqueue($"fetch {channel} {address.AbsoluteUri}");

        return Task.FromResult(Fetched(address));
    }

    public Task<RelayedBody> PostAsync(string channel, Uri address, HybridcastBoundary boundary, PostedBody posted, CancellationToken cancellationToken)
    {
        Asked.Enqueue($"post {channel} {address.AbsoluteUri} {posted.ContentType ?? "untyped"} {Encoding.UTF8.GetString(posted.Bytes.Span)}");
        Posts.Enqueue(posted);

        return Task.FromResult(Fetched(address));
    }

    public Task<RelayedSocket> OpenSocketAsync(
        string channel,
        Uri address,
        HybridcastBoundary boundary,
        IReadOnlyList<string> subProtocols,
        CancellationToken cancellationToken)
    {
        Asked.Enqueue($"socket {channel} {address.AbsoluteUri} {string.Join(',', subProtocols)}");

        return Task.FromResult(new RelayedSocket(RelayResult.Answered, null, new EchoingSocket(subProtocols.FirstOrDefault())));
    }

    private sealed class EchoingSocket(string? chosen) : IHybridcastSocket
    {
        public string? SubProtocol => chosen;

        public async Task CarryAsync(WebSocket viewer, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[1024];

            while (true)
            {
                WebSocketReceiveResult received = await viewer.ReceiveAsync(buffer, cancellationToken);

                if (received.MessageType is WebSocketMessageType.Close)
                {
                    await viewer.CloseOutputAsync(viewer.CloseStatus!.Value, viewer.CloseStatusDescription, cancellationToken);

                    return;
                }

                await viewer.SendAsync((byte[])[.. "echo:"u8, .. buffer.AsSpan(0, received.Count)], received.MessageType, true, cancellationToken);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class HybridcastRelayFeature : IAsyncDisposable
{
    public const string Entry = "http://hc.station.example.jp/p/index.html";

    public const string Ait = "/api/live/32736-1024/data-broadcast/ait";

    public const string Start = "/api/live/32736-1024/data-broadcast/app/ticket";

    public const string AitAddress = "http://hc.station.example.jp/p/app.ait";

    public const string ReceiverScript = "/hybridcast/receiver.js";

    public static readonly DateTime Noon = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public HybridcastRelayFeature(RecordedStartup? heard = null)
    {
        WebApplicationFactory<Program> configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            if (heard is not null)
            {
                services.AddSingleton<ILoggerProvider>(heard);
            }

            services.RemoveAll<IHostedService>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IBroadcastServiceRepository>(Services);
            services.AddSingleton<ICandidateChannelRepository>(Candidates);
            services.AddSingleton<IHybridcastRelay>(Relay);
        }));

        if (heard is not null)
        {
            configured = configured.WithWebHostBuilder(builder => builder
                .UseSetting("Logging:LogLevel:Default", nameof(LogLevel.Trace))
                .UseSetting("Logging:LogLevel:Microsoft", nameof(LogLevel.Trace))
                .UseSetting("Logging:LogLevel:Microsoft.AspNetCore", nameof(LogLevel.Trace))
                .UseSetting($"Logging:{typeof(RecordedStartup).FullName}:LogLevel:Default", nameof(LogLevel.Trace))
                .UseSetting(
                    $"Logging:{typeof(RecordedStartup).FullName}:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics",
                    nameof(LogLevel.Trace))
                .UseSetting(
                    $"Logging:{typeof(RecordedStartup).FullName}:LogLevel:Microsoft.AspNetCore.Routing.Matching.DfaMatcher",
                    nameof(LogLevel.Trace)));
        }

        Served = configured.WithTestScheme();
        Viewer = Served.CreateClient();
        Viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "anything");
        Frame = Served.CreateClient();
        Seed(1024, ServiceCategory.Television);
        Seed(1025, ServiceCategory.Television);
        Seed(1026, ServiceCategory.Radio);
    }

    public WebApplicationFactory<Program> Served { get; }

    public HttpClient Viewer { get; }

    public HttpClient Frame { get; }

    public MovingClock Clock { get; } = new(Noon);

    public HeldServices Services { get; } = new();

    public HeldCandidates Candidates { get; } = new();

    public ScriptedHybridcastRelay Relay { get; } = new();

    public void Allow(bool line = true, bool hybridcast = true)
    {
        List<string> cookies = [];

        if (line)
        {
            cookies.Add($"{DataBroadcastLineCookie.Name}={DataBroadcastLineCookie.Allows}");
        }

        if (hybridcast)
        {
            cookies.Add($"{DataBroadcastHybridcastCookie.Name}={DataBroadcastHybridcastCookie.Allows}");
        }

        Viewer.DefaultRequestHeaders.Remove(HeaderNames.Cookie);

        if (cookies.Count > 0)
        {
            Viewer.DefaultRequestHeaders.Add(HeaderNames.Cookie, string.Join("; ", cookies));
        }
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> StartAsync(object body, string path = Start)
    {
        using HttpResponseMessage response = await Viewer.PostAsJsonAsync(new Uri(path, UriKind.Relative), body);

        return await DataBroadcastRelayFeature.ReadAsync(response);
    }

    public async Task<JsonElement> StartedAsync()
    {
        Allow();
        (HttpStatusCode status, JsonElement body) = await StartAsync(new { ait = AitAddress, orgId = 16, appId = 1, receiverScript = ReceiverScript });

        Assert.Equal(HttpStatusCode.OK, status);

        return body.GetProperty("data");
    }

    public static HybridcastApplication Application(
        uint orgId = 16,
        ushort appId = 1,
        HybridcastControlCode controlCode = HybridcastControlCode.Autostart)
        => new(
            orgId,
            appId,
            controlCode,
            new Uri(Entry),
            1,
            [new HybridcastPermission(0x1fff, ["http://station.example.jp", "ws://station.example.jp"])]);

    public static string Opening(JsonElement started, string address)
    {
        string entered = started.GetProperty("address").GetString()!;

        return $"{entered[..entered.IndexOf('?', StringComparison.Ordinal)]}?uri={Uri.EscapeDataString(address)}";
    }

    public static Uri Relayed(JsonElement started, string relay, string address)
        => new(
            $"/api/live/32736-1024/data-broadcast/{relay}/{started.GetProperty("inTheClear").GetString()}?uri={Uri.EscapeDataString(address)}",
            UriKind.Relative);

    public static HttpRequestMessage Preflight(Uri target, string method, string headers)
    {
        HttpRequestMessage asking = new(HttpMethod.Options, target);
        asking.Headers.TryAddWithoutValidation(HeaderNames.Origin, "null");
        asking.Headers.TryAddWithoutValidation(HeaderNames.AccessControlRequestMethod, method);

        if (headers.Length > 0)
        {
            asking.Headers.TryAddWithoutValidation(HeaderNames.AccessControlRequestHeaders, headers);
        }

        return asking;
    }

    public static Uri Socket(JsonElement started, string address)
    {
        string ticket = started.GetProperty("inTheClear").GetString()!;

        return new Uri($"ws://localhost/api/live/32736-1024/data-broadcast/ws/{ticket}?uri={Uri.EscapeDataString(address)}");
    }

    public async ValueTask DisposeAsync()
    {
        Viewer.Dispose();
        Frame.Dispose();
        await factory.DisposeAsync();
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
