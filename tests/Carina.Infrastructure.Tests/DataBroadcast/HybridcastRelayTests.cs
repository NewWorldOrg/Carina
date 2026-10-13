using System.Net;
using System.Net.WebSockets;
using System.Text;

using Carina.Domain.DataBroadcast;
using Carina.Infrastructure.DataBroadcast;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Tests.DataBroadcast;

public sealed class HybridcastRelayTests
{
    private const string Channel = "32736-1024";

    private const string Host = "hc.station.example.jp";

    private static readonly IPAddress Public = IPAddress.Parse("203.0.113.7");

    private static readonly HybridcastBoundary Boundary = HybridcastBoundary.Of(
        new Uri($"http://{Host}/p/index.html"),
        [new HybridcastPermission(0x1fff, ["http://station.example.jp", "ws://station.example.jp"])]);

    private static readonly HybridcastBoundary ReachingInwards = HybridcastBoundary.Of(
        new Uri($"http://{Host}/p/index.html"),
        [
            new HybridcastPermission(
                0x1fff,
                ["http://station.example.jp", "http://carina.example", "https://carina.example", "http://日本.example", "http://mirror.example"]),
        ]);

    private static readonly OwnHost Own = OwnHost.Of([new Uri("https://carina.example"), new Uri("https://日本.example")]);

    private static readonly IPAddress Mine = IPAddress.Parse("198.51.100.9");

    private static readonly string Ait =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><isdb:ServiceDiscovery xmlns:isdb=\"urn:arib:isdb:2012\" xmlns:mhp=\"urn:dvb:mhp:2009\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
        + "<isdb:ApplicationDiscovery DomainName=\"station.example.jp\"><isdb:ApplicationList><isdb:Application>"
        + "<isdb:applicationIdentifier><isdb:orgId>16</isdb:orgId><isdb:appId>1</isdb:appId></isdb:applicationIdentifier>"
        + "<isdb:applicationDescriptor><isdb:type><isdb:IsdbApp>ISDB-HTML</isdb:IsdbApp></isdb:type><isdb:controlCode>AUTOSTART</isdb:controlCode>"
        + "<isdb:mhpVersion><mhp:profile>1</mhp:profile><mhp:versionMajor>1</mhp:versionMajor><mhp:versionMinor>1</mhp:versionMinor><mhp:versionMicro>1</mhp:versionMicro></isdb:mhpVersion></isdb:applicationDescriptor>"
        + $"<isdb:applicationTransport xsi:type=\"isdb:HTTPTransportType\"><isdb:URLBase>http://{Host}/</isdb:URLBase></isdb:applicationTransport>"
        + "<isdb:applicationLocation>p/index.html</isdb:applicationLocation>"
        + "<isdb:applicationBoundaryAndPermissionDescriptor><isdb:boundaryAndPermission><isdb:permissionBitmap>1fff</isdb:permissionBitmap><isdb:managedURL>http://station.example.jp</isdb:managedURL></isdb:boundaryAndPermission></isdb:applicationBoundaryAndPermissionDescriptor>"
        + "</isdb:Application></isdb:ApplicationList></isdb:ApplicationDiscovery></isdb:ServiceDiscovery>";

    [Fact(DisplayName = "BR-BV-007: an AIT is fetched and read into the applications it names, whatever type the server gives it")]
    public async Task AnAitIsFetchedAndRead()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Of(200, Encoding.UTF8.GetBytes(Ait), "Content-Type: application/X-arib-ait+xml; charset=\"utf-8\""));

        RelayedAit read = await rig.Relay.ReadAitAsync(Channel, $"http://{Host}/p/app.ait", CancellationToken.None);

        Assert.Equal(RelayResult.Answered, read.Result);
        Assert.Equal(200, read.HttpStatus);
        HybridcastApplication application = Assert.Single(read.Applications!);
        Assert.Equal(16u, application.OrgId);
        Assert.Equal((ushort)1, application.AppId);
        Assert.Equal(HybridcastControlCode.Autostart, application.ControlCode);
        Assert.Equal(new Uri($"http://{Host}/p/index.html"), application.EntryUrl);
        Assert.Equal(1, application.Profile);
        Assert.Equal(["http://station.example.jp"], Assert.Single(application.Boundary).ManagedUrls);
        Assert.Equal(BroadcasterRelaySettings.Default.UserAgent, Assert.Single(rig.Server.Heard).Headers["User-Agent"]);
    }

    [Fact(DisplayName = "BR-BV-007: an answer that is not an AIT is a server that answered with no applications to read")]
    public async Task AnAnswerThatIsNotAnAitHasNoApplications()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "<html></html>"));

        RelayedAit read = await rig.Relay.ReadAitAsync(Channel, $"http://{Host}/p/app.ait", CancellationToken.None);

        Assert.Equal(new RelayedAit(RelayResult.Answered, 200, null), read);
    }

    [Fact(DisplayName = "BR-BD-013: an AIT the server answers 404 for carries its status and nothing to read")]
    public async Task AnAitAnswered404CarriesItsStatus()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(404, "gone"));

        RelayedAit read = await rig.Relay.ReadAitAsync(Channel, $"http://{Host}/p/app.ait", CancellationToken.None);

        Assert.Equal(new RelayedAit(RelayResult.Answered, 404, null), read);
    }

    [Fact(DisplayName = "BR-BV-007: an AIT larger than an AIT is allowed to be is refused as too large")]
    public async Task AnAitTooLargeIsRefused()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Of(200, new byte[(256 * 1024) + 1]));

        RelayedAit read = await rig.Relay.ReadAitAsync(Channel, $"http://{Host}/p/app.ait", CancellationToken.None);

        Assert.Equal(RelayedAit.Failed(RelayResult.RefusedOrTooLarge), read);
    }

    [Fact(DisplayName = "BR-BD-011: an AIT at an address inside is refused and connects to nothing")]
    public async Task AnAitInsideIsRefused()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, Ait));

        RelayedAit read = await rig.Relay.ReadAitAsync(Channel, "http://192.168.0.1/app.ait", CancellationToken.None);

        Assert.Equal(RelayedAit.Failed(RelayResult.RefusedOrTooLarge), read);
        Assert.Empty(rig.Network.Connected);
    }

    [Fact(DisplayName = "FR-016: a resource inside the boundary is relayed with its type and status, from where it was taken")]
    public async Task AResourceInsideTheBoundaryIsRelayed()
    {
        await using RelayRig rig = new(request => request.Target == "/p/old.js"
            ? CannedAnswer.Of(302, [], "Location: http://cdn.station.example.jp/p/new.js")
            : CannedAnswer.Of(200, "var a;"u8.ToArray(), "Content-Type: application/javascript; charset=utf-8"));

        RelayedBody fetched = await rig.Relay.FetchAsync(Channel, new Uri($"http://{Host}/p/old.js"), Boundary, CancellationToken.None);

        Assert.Equal(RelayResult.Answered, fetched.Result);
        Assert.Equal(200, fetched.HttpStatus);
        Assert.Equal("application/javascript; charset=utf-8", fetched.ContentType);
        Assert.Equal("var a;"u8.ToArray(), fetched.Body.ToArray());
        Assert.Equal(new Uri("http://cdn.station.example.jp/p/new.js"), fetched.At);
    }

    [Fact(DisplayName = "BR-BA-004: a resource outside the boundary is refused and connects to nothing")]
    public async Task AResourceOutsideTheBoundaryIsRefused()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "x"));

        RelayedBody fetched = await rig.Relay.FetchAsync(Channel, new Uri("http://elsewhere.example/a.js"), Boundary, CancellationToken.None);

        Assert.Equal(RelayResult.RefusedOrTooLarge, fetched.Result);
        Assert.Empty(rig.Network.Connected);
    }

    [Fact(DisplayName = "BR-BA-004: a redirect out of the boundary is not followed, and the answer that asked for it is handed back")]
    public async Task ARedirectOutOfTheBoundaryIsNotFollowed()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Of(302, [], "Location: http://elsewhere.example/a.js"));

        RelayedBody fetched = await rig.Relay.FetchAsync(Channel, new Uri($"http://{Host}/p/a.js"), Boundary, CancellationToken.None);

        Assert.Equal(302, fetched.HttpStatus);
        Assert.Single(rig.Server.Heard);
    }

    [Fact(DisplayName = "BR-BD-011: a name inside the boundary that resolves inwards is still refused")]
    public async Task ANameThatResolvesInwardsIsRefused()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "x"));
        rig.Network.Names[Host] = [IPAddress.Parse("10.0.0.5")];

        RelayedBody fetched = await rig.Relay.FetchAsync(Channel, new Uri($"http://{Host}/p/a.js"), Boundary, CancellationToken.None);

        Assert.Equal(RelayResult.RefusedOrTooLarge, fetched.Result);
        Assert.Empty(rig.Network.Connected);
    }

    [Fact(DisplayName = "BR-BD-011: a resource larger than the relay takes back is refused as too large")]
    public async Task AResourceTooLargeIsRefused()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Of(200, new byte[17]), settings => settings with { LargestReceived = 16 });

        RelayedBody fetched = await rig.Relay.FetchAsync(Channel, new Uri($"http://{Host}/p/a.png"), Boundary, CancellationToken.None);

        Assert.Equal(RelayedBody.Failed(RelayResult.RefusedOrTooLarge), fetched);
    }

    [Fact(DisplayName = "BR-BD-012: every exchange is written down by host and path, never by its query")]
    public async Task EveryExchangeIsWrittenDownWithoutItsQuery()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "x"));

        await rig.Relay.FetchAsync(Channel, new Uri($"http://{Host}/p/a.js?id=secret"), Boundary, CancellationToken.None);

        KeptLine line = Assert.Single(rig.Log.Lines);
        Assert.Equal(Channel, line.Values["Channel"]);
        Assert.Equal(Host, line.Values["Host"]);
        Assert.Equal("/p/a.js", line.Values["Path"]);
        Assert.Equal(1, line.Values["Result"]);
        Assert.Equal(200, line.Values["Status"]);
        Assert.DoesNotContain("secret", line.Said, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "FR-016: a WebSocket inside the boundary is carried both ways on the subprotocol the server chose")]
    public async Task AWebSocketIsCarriedBothWays()
    {
        await using SocketRig rig = new(BroadcasterSocketStandIn.EchoAsync);

        RelayedSocket opened = await rig.Relay.OpenSocketAsync(Channel, new Uri($"ws://{Host}/live?id=1"), Boundary, ["hc.v1", "hc.v0"], CancellationToken.None);

        await using IHybridcastSocket socket = opened.Socket!;
        Assert.Equal("hc.v1", socket.SubProtocol);
        (WebSocket viewer, WebSocket player) = Pair();
        Task carried = socket.CarryAsync(viewer, CancellationToken.None);

        await player.SendAsync("hello"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
        Assert.Equal("echo:hello", await ReceiveTextAsync(player));
        await player.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        await carried.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(WebSocketCloseStatus.NormalClosure, player.CloseStatus);
        IReadOnlyDictionary<string, string> heard = Assert.Single(rig.Server.Heard);
        Assert.Equal("GET /live?id=1 HTTP/1.1", heard["Request-Line"]);
        Assert.Equal(BroadcasterRelaySettings.Default.UserAgent, heard["User-Agent"]);
        Assert.False(heard.ContainsKey("Cookie"));
    }

    [Fact(DisplayName = "FR-016: a close from the broadcaster's server reaches the application with its status and reason")]
    public async Task ACloseFromTheServerReachesTheApplication()
    {
        await using SocketRig rig = new(async server =>
        {
            await server.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "over", CancellationToken.None);
            await server.ReceiveAsync(new byte[16], CancellationToken.None);
        });

        RelayedSocket opened = await rig.Relay.OpenSocketAsync(Channel, new Uri($"ws://{Host}/live"), Boundary, [], CancellationToken.None);
        await using IHybridcastSocket socket = opened.Socket!;
        (WebSocket viewer, WebSocket player) = Pair();
        Task carried = socket.CarryAsync(viewer, CancellationToken.None);

        WebSocketReceiveResult received = await player.ReceiveAsync(new byte[16], CancellationToken.None);
        await player.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "over", CancellationToken.None);
        await carried.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(WebSocketMessageType.Close, received.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, player.CloseStatus);
        Assert.Equal("over", player.CloseStatusDescription);
        KeptLine line = Assert.Single(rig.Log.Lines);
        Assert.Equal("/live", line.Values["Path"]);
    }

    [Theory(DisplayName = "BR-BA-004: a WebSocket outside the boundary, on another port or not a WebSocket at all is refused before connecting")]
    [InlineData("ws://elsewhere.example/live")]
    [InlineData("ws://station.example.jp:8080/live")]
    [InlineData("http://station.example.jp/live")]
    [InlineData("ws://10.0.0.1/live")]
    public async Task AWebSocketOutsideIsRefused(string address)
    {
        await using SocketRig rig = new(BroadcasterSocketStandIn.EchoAsync);

        RelayedSocket opened = await rig.Relay.OpenSocketAsync(Channel, new Uri(address), Boundary, [], CancellationToken.None);

        Assert.Equal(RelayedSocket.Failed(RelayResult.RefusedOrTooLarge), opened);
        Assert.Empty(rig.Network.Connected);
    }

    [Theory(DisplayName = "BR-BD-011: an AIT at this installation's own host is refused whatever its case, trailing dot or port, and nothing is resolved or connected to")]
    [InlineData("http://carina.example/app.ait")]
    [InlineData("https://Carina.Example./app.ait")]
    [InlineData("http://carina.example:443/app.ait")]
    [InlineData("http://xn--wgv71a.example/app.ait")]
    public async Task AnAitAtItsOwnHostIsRefused(string uri)
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, Ait));

        RelayedAit read = await rig.Relay.ReadAitAsync(Channel, uri, CancellationToken.None);

        Assert.Equal(RelayedAit.Failed(RelayResult.RefusedOrTooLarge), read);
        Assert.Empty(rig.Network.Resolved);
        Assert.Empty(rig.Network.Connected);
    }

    [Theory(DisplayName = "BR-BD-011: a resource at this installation's own host is refused even where the boundary takes it in, and nothing is resolved or connected to")]
    [InlineData("http://carina.example/api/health")]
    [InlineData("https://CARINA.example/p/a.js")]
    [InlineData("http://日本.example/p/a.js")]
    public async Task AResourceAtItsOwnHostIsRefusedInsideTheBoundary(string uri)
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "x"));

        RelayedBody fetched = await rig.Relay.FetchAsync(Channel, new Uri(uri), ReachingInwards, CancellationToken.None);

        Assert.Equal(RelayedBody.Failed(RelayResult.RefusedOrTooLarge), fetched);
        Assert.Empty(rig.Network.Resolved);
        Assert.Empty(rig.Network.Connected);
    }

    [Theory(DisplayName = "BR-BD-011: a redirect inside the boundary to this installation's own host is not followed, and the answer that asked for it is handed back")]
    [InlineData("http://carina.example/api/health")]
    [InlineData("https://Carina.Example.:443/api/health")]
    public async Task ARedirectToItsOwnHostIsNotFollowed(string location)
    {
        await using RelayRig rig = new(_ => CannedAnswer.Of(302, [], $"Location: {location}"));

        RelayedBody fetched = await rig.Relay.FetchAsync(Channel, new Uri($"http://{Host}/p/a.js"), ReachingInwards, CancellationToken.None);

        Assert.Equal(RelayResult.Answered, fetched.Result);
        Assert.Equal(302, fetched.HttpStatus);
        Assert.Single(rig.Server.Heard);
        Assert.Equal([Host], rig.Network.Resolved);
    }

    [Fact(DisplayName = "BR-BD-011: a redirect inside the boundary to a name that resolves to this machine is refused as the connection opens")]
    public async Task ARedirectToANameResolvingToThisMachineIsRefused()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Of(302, [], "Location: http://mirror.example/api/health"));
        rig.Network.Names["mirror.example"] = [Mine];

        RelayedBody fetched = await rig.Relay.FetchAsync(Channel, new Uri($"http://{Host}/p/a.js"), ReachingInwards, CancellationToken.None);

        Assert.Equal(RelayedBody.Failed(RelayResult.RefusedOrTooLarge), fetched);
        Assert.Single(rig.Server.Heard);
        Assert.Single(rig.Network.Connected);
    }

    [Theory(DisplayName = "BR-BD-011: a WebSocket to this installation's own host is refused before anything is resolved or connected to, even where the boundary takes it in")]
    [InlineData("ws://carina.example/live")]
    [InlineData("ws://Carina.Example/live")]
    [InlineData("wss://carina.example/live")]
    [InlineData("ws://日本.example/live")]
    public async Task AWebSocketToItsOwnHostIsRefused(string address)
    {
        await using SocketRig rig = new(BroadcasterSocketStandIn.EchoAsync);

        RelayedSocket opened = await rig.Relay.OpenSocketAsync(Channel, new Uri(address), ReachingInwards, [], CancellationToken.None);

        Assert.Equal(RelayedSocket.Failed(RelayResult.RefusedOrTooLarge), opened);
        Assert.Empty(rig.Network.Resolved);
        Assert.Empty(rig.Network.Connected);
        Assert.Empty(rig.Server.Heard);
    }

    [Fact(DisplayName = "BR-BD-011: a WebSocket to a name that resolves to this machine is refused as the connection opens, and gives its place back")]
    public async Task AWebSocketToANameResolvingToThisMachineIsRefused()
    {
        await using SocketRig rig = new(BroadcasterSocketStandIn.EchoAsync, settings => settings with { SocketsAtOnce = 1 });
        rig.Network.Names["mirror.example"] = [Mine];

        RelayedSocket refused = await rig.Relay.OpenSocketAsync(Channel, new Uri("ws://mirror.example/live"), ReachingInwards, [], CancellationToken.None);
        RelayedSocket after = await rig.Relay.OpenSocketAsync(Channel, new Uri($"ws://{Host}/live"), Boundary, [], CancellationToken.None);

        Assert.Equal(RelayedSocket.Failed(RelayResult.RefusedOrTooLarge), refused);
        Assert.Equal(["mirror.example", Host], rig.Network.Resolved);
        Assert.Single(rig.Network.Connected);
        Assert.NotNull(after.Socket);
        await after.Socket!.DisposeAsync();
    }

    [Fact(DisplayName = "BR-BD-011: a channel holds a bounded number of WebSockets open at once")]
    public async Task AChannelHoldsABoundedNumberOfWebSockets()
    {
        await using SocketRig rig = new(BroadcasterSocketStandIn.EchoAsync, settings => settings with { SocketsAtOnce = 1 });

        RelayedSocket first = await rig.Relay.OpenSocketAsync(Channel, new Uri($"ws://{Host}/live"), Boundary, [], CancellationToken.None);
        RelayedSocket second = await rig.Relay.OpenSocketAsync(Channel, new Uri($"ws://{Host}/live"), Boundary, [], CancellationToken.None);
        await first.Socket!.DisposeAsync();
        RelayedSocket third = await rig.Relay.OpenSocketAsync(Channel, new Uri($"ws://{Host}/live"), Boundary, [], CancellationToken.None);

        Assert.Equal(RelayedSocket.Failed(RelayResult.RefusedOrTooLarge), second);
        Assert.NotNull(third.Socket);
        await third.Socket!.DisposeAsync();
    }

    [Fact(DisplayName = "BR-BD-011: a WebSocket whose asking was called off while the server was being reached gives its place back")]
    public async Task ACalledOffWebSocketGivesItsPlaceBack()
    {
        await using SocketRig rig = new(BroadcasterSocketStandIn.EchoAsync, settings => settings with { SocketsAtOnce = 1 });
        using CancellationTokenSource calledOff = new();
        await calledOff.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => rig.Relay.OpenSocketAsync(Channel, new Uri($"ws://{Host}/live"), Boundary, [], calledOff.Token));
        RelayedSocket after = await rig.Relay.OpenSocketAsync(Channel, new Uri($"ws://{Host}/live"), Boundary, [], CancellationToken.None);

        Assert.NotNull(after.Socket);
        await after.Socket!.DisposeAsync();
    }

    [Theory(DisplayName = "BR-BD-011: a WebSocket asked for on a subprotocol that cannot be named gives its place back")]
    [InlineData("hc.v1", "hc.v1")]
    [InlineData(" ", null)]
    [InlineData("", null)]
    public async Task AnUnnamableSubprotocolGivesItsPlaceBack(string first, string? second)
    {
        await using SocketRig rig = new(BroadcasterSocketStandIn.EchoAsync, settings => settings with { SocketsAtOnce = 1 });
        string[] offered = second is null ? [first] : [first, second];

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => rig.Relay.OpenSocketAsync(Channel, new Uri($"ws://{Host}/live"), Boundary, offered, CancellationToken.None));
        RelayedSocket after = await rig.Relay.OpenSocketAsync(Channel, new Uri($"ws://{Host}/live"), Boundary, [], CancellationToken.None);

        Assert.NotNull(after.Socket);
        await after.Socket!.DisposeAsync();
    }

    [Fact(DisplayName = "BR-BD-011: a message larger than the relay carries closes both sides as too big")]
    public async Task AMessageTooLargeClosesBothSides()
    {
        await using SocketRig rig = new(BroadcasterSocketStandIn.EchoAsync, settings => settings with { LargestMessage = 4 });

        RelayedSocket opened = await rig.Relay.OpenSocketAsync(Channel, new Uri($"ws://{Host}/live"), Boundary, [], CancellationToken.None);
        await using IHybridcastSocket socket = opened.Socket!;
        (WebSocket viewer, WebSocket player) = Pair();
        Task carried = socket.CarryAsync(viewer, CancellationToken.None);

        await player.SendAsync("hello"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
        WebSocketReceiveResult received = await player.ReceiveAsync(new byte[16], CancellationToken.None);
        await player.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig, null, CancellationToken.None);
        await carried.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(WebSocketMessageType.Close, received.MessageType);
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, player.CloseStatus);
    }

    private static (WebSocket Viewer, WebSocket Player) Pair()
    {
        (Stream one, Stream other) = DuplexPipe.Pair();

        return (
            WebSocket.CreateFromStream(one, new WebSocketCreationOptions { IsServer = true }),
            WebSocket.CreateFromStream(other, new WebSocketCreationOptions { IsServer = false }));
    }

    private static async Task<string> ReceiveTextAsync(WebSocket socket)
    {
        byte[] buffer = new byte[256];
        WebSocketReceiveResult received = await socket.ReceiveAsync(buffer, CancellationToken.None);

        return Encoding.UTF8.GetString(buffer, 0, received.Count);
    }

    private sealed class RelayRig : IAsyncDisposable
    {
        public RelayRig(Func<HeardRequest, CannedAnswer> answer, Func<HybridcastRelaySettings, HybridcastRelaySettings>? adjust = null)
        {
            Server = new BroadcasterServerStandIn(answer);
            Network = new PointedNetwork(Server.Port);
            Network.Names[Host] = [Public];
            Network.Names["cdn.station.example.jp"] = [Public];
            Network.Names["elsewhere.example"] = [IPAddress.Parse("198.51.100.20")];
            Network.Names["carina.example"] = [Public];
            Network.Names["xn--wgv71a.example"] = [Public];
            Network.Mine.Add(Mine);
            Relay = new HybridcastRelay(Network, Own, (adjust ?? (given => given))(HybridcastRelaySettings.Default), TimeProvider.System, Log);
        }

        public BroadcasterServerStandIn Server { get; }

        public PointedNetwork Network { get; }

        public KeptHybridcastLog Log { get; } = new();

        public HybridcastRelay Relay { get; }

        public async ValueTask DisposeAsync()
        {
            Relay.Dispose();
            await Server.DisposeAsync();
        }
    }

    private sealed class SocketRig : IAsyncDisposable
    {
        public SocketRig(Func<WebSocket, Task> serve, Func<HybridcastRelaySettings, HybridcastRelaySettings>? adjust = null)
        {
            Server = new BroadcasterSocketStandIn(serve);
            Network = new PointedNetwork(Server.Port);
            Network.Names[Host] = [Public];
            Network.Names["carina.example"] = [Public];
            Network.Names["xn--wgv71a.example"] = [Public];
            Network.Mine.Add(Mine);
            Relay = new HybridcastRelay(Network, Own, (adjust ?? (given => given))(HybridcastRelaySettings.Default), TimeProvider.System, Log);
        }

        public BroadcasterSocketStandIn Server { get; }

        public PointedNetwork Network { get; }

        public KeptHybridcastLog Log { get; } = new();

        public HybridcastRelay Relay { get; }

        public async ValueTask DisposeAsync()
        {
            Relay.Dispose();
            await Server.DisposeAsync();
        }
    }
}

internal sealed class KeptHybridcastLog : ILogger<HybridcastRelay>
{
    public System.Collections.Concurrent.ConcurrentQueue<KeptLine> Lines { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        IReadOnlyDictionary<string, object?> values = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : new Dictionary<string, object?>();

        Lines.Enqueue(new KeptLine(logLevel, formatter(state, exception), values));
    }
}
