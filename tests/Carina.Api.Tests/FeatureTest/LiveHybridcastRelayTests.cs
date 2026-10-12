using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    public RelayedAit Ait { get; set; } = new(RelayResult.Answered, 200, [LiveHybridcastRelayTests.Application()]);

    public Func<Uri, RelayedBody> Fetched { get; set; } = address => new RelayedBody(RelayResult.Answered, 200, "text/plain", "x"u8.ToArray(), address);

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

    public static string Opening(JsonElement started, string address)
    {
        string entered = started.GetProperty("address").GetString()!;

        return $"{entered[..entered.IndexOf('?', StringComparison.Ordinal)]}?uri={Uri.EscapeDataString(address)}";
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

public sealed class LiveHybridcastRelayTests
{
    private const string Entry = "http://hc.station.example.jp/p/index.html";

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

    [Theory(DisplayName = "BR-BD-011: an AIT is read only where the browser allows both communication and Hybridcast")]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AnAitIsReadOnlyWhereBothAreAllowed(bool line, bool hybridcast)
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow(line, hybridcast);

        using HttpResponseMessage response = await feature.Viewer.GetAsync(new Uri($"{HybridcastRelayFeature.Ait}?uri={Uri.EscapeDataString(HybridcastRelayFeature.AitAddress)}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(feature.Relay.Asked);
    }

    [Fact(DisplayName = "FR-015: an AIT is read for the channel in the path and answered with the applications it names")]
    public async Task AnAitIsAnsweredWithItsApplications()
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow();

        using HttpResponseMessage response = await feature.Viewer.GetAsync(new Uri($"{HybridcastRelayFeature.Ait}?uri={Uri.EscapeDataString(HybridcastRelayFeature.AitAddress)}", UriKind.Relative));
        (HttpStatusCode status, JsonElement body) = await DataBroadcastRelayFeature.ReadAsync(response);
        JsonElement application = Assert.Single(body.GetProperty("data").GetProperty("applications").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(16, application.GetProperty("orgId").GetInt64());
        Assert.Equal(1, application.GetProperty("appId").GetInt32());
        Assert.Equal(1, application.GetProperty("controlCode").GetInt32());
        Assert.Equal(Entry, application.GetProperty("entryUrl").GetString());
        Assert.Equal(1, application.GetProperty("profile").GetInt32());
        JsonElement pair = Assert.Single(application.GetProperty("boundary").EnumerateArray());
        Assert.Equal(0x1fff, pair.GetProperty("permissionBitmap").GetInt32());
        Assert.Equal(["http://station.example.jp", "ws://station.example.jp"], pair.GetProperty("managedUrls").EnumerateArray().Select(url => url.GetString()));
        Assert.Equal($"ait 32736-1024 {HybridcastRelayFeature.AitAddress}", Assert.Single(feature.Relay.Asked));
    }

    [Fact(DisplayName = "BR-BD-013: an answer that is not an AIT is 502 with the result and the status the server answered")]
    public async Task AnAnswerThatIsNotAnAitIs502()
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow();
        feature.Relay.Ait = new RelayedAit(RelayResult.Answered, 404, null);

        using HttpResponseMessage response = await feature.Viewer.GetAsync(new Uri($"{HybridcastRelayFeature.Ait}?uri={Uri.EscapeDataString(HybridcastRelayFeature.AitAddress)}", UriKind.Relative));
        (HttpStatusCode status, JsonElement body) = await DataBroadcastRelayFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("result").GetInt32());
        Assert.Equal(404, body.GetProperty("data").GetProperty("httpStatus").GetInt32());
    }

    [Theory(DisplayName = "an AIT asked for without its address, or for a channel that cannot be watched live here, is refused and read from nowhere")]
    [InlineData("/api/live/32736-1024/data-broadcast/ait", HttpStatusCode.BadRequest)]
    [InlineData("/api/live/32736-1026/data-broadcast/ait?uri=http%3A%2F%2Fhc.station.example.jp%2Fapp.ait", HttpStatusCode.NotFound)]
    public async Task AnAitAskedForBadlyIsRefused(string path, HttpStatusCode expected)
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow();

        using HttpResponseMessage response = await feature.Viewer.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(feature.Relay.Asked);
    }

    [Fact(DisplayName = "BR-BV-007: starting an application reads the AIT again and hands back a ticket and the path the frame opens")]
    public async Task StartingAnApplicationHandsBackATicket()
    {
        await using HybridcastRelayFeature feature = new();

        JsonElement started = await feature.StartedAsync();
        string ticket = started.GetProperty("inTheClear").GetString()!;

        Assert.Equal(43, ticket.Length);
        Assert.Equal(Entry, started.GetProperty("entryUrl").GetString());
        Assert.Equal(
            $"/api/live/32736-1024/data-broadcast/app/{ticket}?uri={Uri.EscapeDataString(Entry)}",
            started.GetProperty("address").GetString());
        Assert.Equal($"ait 32736-1024 {HybridcastRelayFeature.AitAddress}", Assert.Single(feature.Relay.Asked));
    }

    [Theory(DisplayName = "BR-BV-007: nothing is started where no application matches both identifiers and is to start by itself")]
    [InlineData(16, 2, HybridcastControlCode.Autostart)]
    [InlineData(17, 1, HybridcastControlCode.Autostart)]
    [InlineData(16, 1, HybridcastControlCode.Present)]
    public async Task NothingIsStartedWithoutAMatchingAutostart(int orgId, int appId, HybridcastControlCode controlCode)
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow();
        feature.Relay.Ait = new RelayedAit(RelayResult.Answered, 200, [Application(controlCode: controlCode)]);

        (HttpStatusCode status, _) = await feature.StartAsync(new { ait = HybridcastRelayFeature.AitAddress, orgId, appId, receiverScript = HybridcastRelayFeature.ReceiverScript });

        Assert.Equal(controlCode is HybridcastControlCode.Present || orgId != 16 || appId != 1 ? HttpStatusCode.NotFound : HttpStatusCode.OK, status);
    }

    [Fact(DisplayName = "BR-BD-013: nothing is started where no AIT could be read, and the answer is 502")]
    public async Task NothingIsStartedWithoutAnAit()
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow();
        feature.Relay.Ait = RelayedAit.Failed(RelayResult.NameNotResolved);

        (HttpStatusCode status, _) = await feature.StartAsync(new { ait = HybridcastRelayFeature.AitAddress, orgId = 16, appId = 1, receiverScript = HybridcastRelayFeature.ReceiverScript });

        Assert.Equal(HttpStatusCode.BadGateway, status);
    }

    [Fact(DisplayName = "BR-BD-011: nothing is started while Hybridcast is off")]
    public async Task NothingIsStartedWhileHybridcastIsOff()
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow(hybridcast: false);

        (HttpStatusCode status, JsonElement body) = await feature.StartAsync(new { ait = HybridcastRelayFeature.AitAddress, orgId = 16, appId = 1, receiverScript = HybridcastRelayFeature.ReceiverScript });

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Contains("Hybridcast", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(feature.Relay.Asked);
    }

    [Theory(DisplayName = "a start that names too little, or a receiver script anywhere but a plain path on this server, is refused")]
    [InlineData("""{"orgId":16,"appId":1,"receiverScript":"/r.js"}""")]
    [InlineData("""{"ait":"http://hc.station.example.jp/a.ait","orgId":0,"appId":1,"receiverScript":"/r.js"}""")]
    [InlineData("""{"ait":"http://hc.station.example.jp/a.ait","orgId":16,"appId":65536,"receiverScript":"/r.js"}""")]
    [InlineData("""{"ait":"http://hc.station.example.jp/a.ait","orgId":16,"appId":1}""")]
    [InlineData("""{"ait":"http://hc.station.example.jp/a.ait","orgId":16,"appId":1,"receiverScript":"//elsewhere.example/r.js"}""")]
    [InlineData("""{"ait":"http://hc.station.example.jp/a.ait","orgId":16,"appId":1,"receiverScript":"https://elsewhere.example/r.js"}""")]
    [InlineData("""{"ait":"http://hc.station.example.jp/a.ait","orgId":16,"appId":1,"receiverScript":"/r.js; script-src *"}""")]
    [InlineData("""{"ait":"http://hc.station.example.jp/a.ait","orgId":16,"appId":1,"receiverScript":"/a/../r.js"}""")]
    public async Task AStartThatNamesTooLittleIsRefused(string json)
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow();

        using StringContent content = new(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await feature.Viewer.PostAsync(new Uri(HybridcastRelayFeature.Start, UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(feature.Relay.Asked);
    }

    [Fact(DisplayName = "a start without a session is refused")]
    public async Task AStartWithoutASessionIsRefused()
    {
        await using HybridcastRelayFeature feature = new();

        using HttpResponseMessage response = await feature.Frame.PostAsJsonAsync(
            new Uri(HybridcastRelayFeature.Start, UriKind.Relative),
            new { ait = HybridcastRelayFeature.AitAddress, orgId = 16, appId = 1, receiverScript = HybridcastRelayFeature.ReceiverScript });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "BR-BA-004: the frame opens the entry with the ticket alone, and is handed it with the receiver script put first into the head once")]
    public async Task TheFrameOpensTheEntryWithTheTicketAlone()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        feature.Relay.Fetched = address => new RelayedBody(
            RelayResult.Answered,
            200,
            "text/html; charset=utf-8",
            "<!DOCTYPE html><html><head><script src=\"/common/jquery.js\"></script></head><body>天気</body></html>"u8.ToArray(),
            address);

        using HttpResponseMessage response = await feature.Frame.GetAsync(new Uri(started.GetProperty("address").GetString()!, UriKind.Relative));
        string page = await response.Content.ReadAsStringAsync();
        string relay = started.GetProperty("address").GetString()!.Split('?')[0];

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal(1, page.Split("data-hybridcast-relay").Length - 1);
        Assert.StartsWith($"<!DOCTYPE html><html><head><script src=\"{HybridcastRelayFeature.ReceiverScript}\" data-hybridcast-relay=\"{relay}\"", page, StringComparison.Ordinal);
        Assert.Contains($"<script src=\"{relay}?uri={Uri.EscapeDataString("http://hc.station.example.jp/common/jquery.js")}\"></script>", page, StringComparison.Ordinal);
        Assert.Contains("<body>天気</body>", page, StringComparison.Ordinal);
        Assert.Equal($"fetch 32736-1024 {Entry}", feature.Relay.Asked.Last());
    }

    [Fact(DisplayName = "BR-BA-004: what is handed to the frame is sandboxed, never stored, and may reach nothing but this application's relay")]
    public async Task WhatIsHandedIsSandboxed()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        string relay = started.GetProperty("address").GetString()!.Split('?')[0];

        using HttpResponseMessage response = await feature.Frame.GetAsync(new Uri(started.GetProperty("address").GetString()!, UriKind.Relative));
        string policy = response.Headers.GetValues(HeaderNames.ContentSecurityPolicy).Single();

        Assert.StartsWith("sandbox allow-scripts allow-forms; default-src 'none';", policy, StringComparison.Ordinal);
        Assert.Contains($"connect-src http://localhost{relay} ws://localhost{relay.Replace("/app/", "/ws/", StringComparison.Ordinal)};", policy, StringComparison.Ordinal);
        Assert.Contains($"script-src 'unsafe-inline' 'unsafe-eval' blob: http://localhost{relay} http://localhost{HybridcastRelayFeature.ReceiverScript};", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-same-origin", policy, StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues(HeaderNames.XContentTypeOptions).Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "FR-016: a resource is handed over byte for byte with its type and status, as often as the frame asks while the grant lasts")]
    public async Task AResourceIsHandedOverByteForByte()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        byte[] picture = [0x89, 0x50, 0x4E, 0x47, 0x3C, 0x68, 0x65, 0x61, 0x64, 0x3E];
        feature.Relay.Fetched = address => address.AbsolutePath == "/gone.png"
            ? new RelayedBody(RelayResult.Answered, 404, "text/plain", "gone"u8.ToArray(), address)
            : new RelayedBody(RelayResult.Answered, 200, "image/png", picture, address);

        using HttpResponseMessage entered = await feature.Frame.GetAsync(new Uri(started.GetProperty("address").GetString()!, UriKind.Relative));
        using HttpResponseMessage first = await feature.Frame.GetAsync(new Uri(HybridcastRelayFeature.Opening(started, "http://cdn.station.example.jp/a.png"), UriKind.Relative));
        using HttpResponseMessage gone = await feature.Frame.GetAsync(new Uri(HybridcastRelayFeature.Opening(started, "http://hc.station.example.jp/gone.png"), UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/png", first.Content.Headers.ContentType?.MediaType);
        Assert.Equal(picture, await first.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Theory(DisplayName = "BR-BA-004: an address outside the application's boundary is refused 403 and asked of nobody")]
    [InlineData("http://elsewhere.example/a.js")]
    [InlineData("https://station.example.jp/a.js")]
    [InlineData("http://10.0.0.1/a.js")]
    public async Task AnAddressOutsideTheBoundaryIsRefused(string address)
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();

        using HttpResponseMessage response = await feature.Frame.GetAsync(new Uri(HybridcastRelayFeature.Opening(started, address), UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(feature.Relay.Asked);
    }

    [Fact(DisplayName = "BR-BD-013: a server that could not be reached is 502")]
    public async Task AServerThatCouldNotBeReachedIs502()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        feature.Relay.Fetched = _ => RelayedBody.Failed(RelayResult.TimedOut);

        using HttpResponseMessage response = await feature.Frame.GetAsync(new Uri(started.GetProperty("address").GetString()!, UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("-3", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-BA-004: an application's ticket opens its own channel's relay and nothing else")]
    public async Task AnApplicationsTicketOpensItsOwnRelayOnly()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        string ticket = started.GetProperty("inTheClear").GetString()!;

        using HttpResponseMessage otherChannel = await feature.Frame.GetAsync(new Uri($"/api/live/32736-1025/data-broadcast/app/{ticket}?uri={Uri.EscapeDataString(Entry)}", UriKind.Relative));
        using HttpResponseMessage stream = await feature.Frame.GetAsync(new Uri($"/api/live/32736-1024/with-ticket/{ticket}/a.ts", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead);
        using HttpResponseMessage unknown = await feature.Frame.GetAsync(new Uri($"/api/live/32736-1024/data-broadcast/app/{new string('A', 43)}?uri={Uri.EscapeDataString(Entry)}", UriKind.Relative));
        using HttpResponseMessage malformed = await feature.Frame.GetAsync(new Uri($"/api/live/32736-1024/data-broadcast/app/short?uri={Uri.EscapeDataString(Entry)}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, otherChannel.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, stream.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, malformed.StatusCode);
        Assert.Single(feature.Relay.Asked);
    }

    [Fact(DisplayName = "BR-BA-004: a ticket to watch a channel opens no application's relay")]
    public async Task ALiveTicketOpensNoApplicationsRelay()
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow();
        using HttpResponseMessage issued = await feature.Viewer.PostAsJsonAsync(new Uri("/api/live/ticket", UriKind.Relative), new { networkId = 32736, serviceId = 1024 });
        (_, JsonElement body) = await DataBroadcastRelayFeature.ReadAsync(issued);
        string ticket = body.GetProperty("data").GetProperty("inTheClear").GetString()!;

        using HttpResponseMessage response = await feature.Frame.GetAsync(new Uri($"/api/live/32736-1024/data-broadcast/app/{ticket}?uri={Uri.EscapeDataString(Entry)}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(feature.Relay.Asked);
    }

    [Fact(DisplayName = "FR-016: a WebSocket inside the boundary is carried both ways, and the application's close is answered")]
    public async Task AWebSocketIsCarriedBothWays()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        WebSocketClient client = feature.Served.Server.CreateWebSocketClient();
        client.SubProtocols.Add("hc.v1");

        using WebSocket socket = await client.ConnectAsync(Socket(started, "ws://hc.station.example.jp/live?id=1"), CancellationToken.None);
        await socket.SendAsync("hello"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
        byte[] buffer = new byte[64];
        WebSocketReceiveResult echoed = await socket.ReceiveAsync(buffer, CancellationToken.None);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        WebSocketReceiveResult closed = await socket.ReceiveAsync(buffer, CancellationToken.None);

        Assert.Equal("hc.v1", socket.SubProtocol);
        Assert.Equal("echo:hello", Encoding.UTF8.GetString(buffer, 0, echoed.Count));
        Assert.Equal(WebSocketMessageType.Close, closed.MessageType);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, closed.CloseStatus);
        Assert.Equal("socket 32736-1024 ws://hc.station.example.jp/live?id=1 hc.v1", feature.Relay.Asked.Last());
    }

    [Fact(DisplayName = "BR-BA-004: a WebSocket outside the boundary is refused 403 and opened to nobody")]
    public async Task AWebSocketOutsideTheBoundaryIsRefused()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        WebSocketClient client = feature.Served.Server.CreateWebSocketClient();

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ConnectAsync(Socket(started, "wss://elsewhere.example/live"), CancellationToken.None));

        Assert.Contains("403", refused.Message, StringComparison.Ordinal);
        Assert.Single(feature.Relay.Asked);
    }

    [Fact(DisplayName = "the WebSocket relay asked for as a page is refused")]
    public async Task TheWebSocketRelayAskedForAsAPageIsRefused()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();

        using HttpResponseMessage response = await feature.Frame.GetAsync(new Uri(Socket(started, "ws://hc.station.example.jp/live").PathAndQuery, UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "no log line names the ticket an application's frame carries, however finely the logs are turned up")]
    public async Task NoLogLineNamesTheTicket()
    {
        RecordedStartup heard = new();
        await using HybridcastRelayFeature feature = new(heard);
        JsonElement started = await feature.StartedAsync();
        string ticket = started.GetProperty("inTheClear").GetString()!;

        using HttpResponseMessage entered = await feature.Frame.GetAsync(new Uri(started.GetProperty("address").GetString()!, UriKind.Relative));
        using HttpResponseMessage outside = await feature.Frame.GetAsync(new Uri(HybridcastRelayFeature.Opening(started, "http://elsewhere.example/a.js"), UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, entered.StatusCode);
        Assert.NotEmpty(heard.Everything);
        Assert.DoesNotContain(heard.Everything, line => line.Text.Contains(ticket, StringComparison.Ordinal)
            || line.Values.Any(value => value.Value?.ToString()?.Contains(ticket, StringComparison.Ordinal) is true));
    }

    private static Uri Socket(JsonElement started, string address)
    {
        string ticket = started.GetProperty("inTheClear").GetString()!;

        return new Uri($"ws://localhost/api/live/32736-1024/data-broadcast/ws/{ticket}?uri={Uri.EscapeDataString(address)}");
    }
}
