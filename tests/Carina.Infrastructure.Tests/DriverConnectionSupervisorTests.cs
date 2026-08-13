using System.Collections.Concurrent;

using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Driver;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests;

public sealed class DriverConnectionSupervisorTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private sealed class RecordingResyncHook : IDriverSessionResyncHook
    {
        private readonly List<IReadOnlyList<SessionSnapshot>> calls = [];
        private readonly Lock gate = new();

        public int CallCount
        {
            get
            {
                lock (gate)
                {
                    return calls.Count;
                }
            }
        }

        public IReadOnlyList<SessionSnapshot>? LastSessions
        {
            get
            {
                lock (gate)
                {
                    return calls.Count > 0 ? calls[^1] : null;
                }
            }
        }

        public Task ReadoptAsync(
            IReadOnlyList<SessionSnapshot> sessions,
            CancellationToken cancellationToken)
        {
            lock (gate)
            {
                calls.Add(sessions);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(string socketPath, DriverIpcClient client, DriverConnectionSupervisor supervisor)
        {
            SocketPath = socketPath;
            Client = client;
            Supervisor = supervisor;
        }

        public string SocketPath { get; }

        public DriverIpcClient Client { get; }

        public DriverConnectionSupervisor Supervisor { get; }

        public DriverConnectionMonitor Monitor { get; private init; } = null!;

        public DriverSignalRelay Signals { get; private init; } = null!;

        public RecordingResyncHook Hook { get; private init; } = null!;

        public static async Task<Harness> StartAsync(
            string? socketPath = null,
            string[]? expectedCapabilities = null)
        {
            socketPath ??= NewSocketPath();

            var client = new DriverIpcClient(
                Options.Create(new DriverOptions { SocketPath = socketPath }));
            var monitor = new DriverConnectionMonitor();
            var signals = new DriverSignalRelay(NullLogger<DriverSignalRelay>.Instance);
            var hook = new RecordingResyncHook();
            var settings = new DriverSupervisionSettings(
                TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(200),
                expectedCapabilities ?? ["recording", "live"],
                () => 1.0);

            var supervisor = new DriverConnectionSupervisor(
                client,
                monitor,
                signals,
                hook,
                settings,
                TimeProvider.System,
                NullLogger<DriverConnectionSupervisor>.Instance);

            var harness = new Harness(socketPath, client, supervisor)
            {
                Monitor = monitor,
                Signals = signals,
                Hook = hook,
            };

            await supervisor.StartAsync(CancellationToken.None);

            return harness;
        }

        public async ValueTask DisposeAsync()
        {
            using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await Supervisor.StopAsync(patience.Token);
            Supervisor.Dispose();
            Client.Dispose();
        }
    }

    private static string NewSocketPath()
        => Path.Combine(
            Directory.CreateTempSubdirectory("carina-supervisor-").FullName,
            "driver.sock");

    private static async Task Eventually(Func<bool> condition, string what)
    {
        var start = Environment.TickCount64;

        while (Environment.TickCount64 - start < Patience.TotalMilliseconds)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Did not happen within {Patience.TotalSeconds}s: {what}");
    }

    [Fact]
    public async Task ConnectsAndSurfacesTheHelloAndReadopts()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        driver.Sessions =
        [
            new SessionSnapshot(
                SessionId.Parse("rec-1"),
                SessionPurpose.Recording,
                "fake-terrestrial",
                SessionState.Active,
                DateTimeOffset.UtcNow),
        ];

        await using var harness = await Harness.StartAsync(socketPath);

        await Eventually(
            () => harness.Monitor.Current.Connection is DriverConnection.Connected,
            "the supervisor connects");

        Assert.Equal("instance-a", harness.Monitor.Current.Hello?.InstanceId);
        Assert.Empty(harness.Monitor.Current.MissingCapabilities);
        Assert.Equal(1, harness.Hook.CallCount);
        Assert.Equal("rec-1", Assert.Single(harness.Hook.LastSessions!).SessionId.Value);
    }

    [Fact]
    public async Task StartsDegradedWithoutADriverAndNeverThrows()
    {
        await using var harness = await Harness.StartAsync();

        await Task.Delay(200);

        Assert.Equal(DriverConnection.NotConnected, harness.Monitor.Current.Connection);
        Assert.Equal(0, harness.Hook.CallCount);
    }

    [Fact]
    public async Task ALostDriverFlipsToNotConnectedWithoutCrashing()
    {
        var socketPath = NewSocketPath();
        var driver = await FakeDriver.StartAsync(socketPath, FakeDriver.HelloFor("instance-a"));
        await using var harness = await Harness.StartAsync(socketPath);

        await Eventually(
            () => harness.Monitor.Current.Connection is DriverConnection.Connected,
            "the supervisor connects");

        await driver.DisposeAsync();

        await Eventually(
            () => harness.Monitor.Current.Connection is DriverConnection.NotConnected,
            "the loss is noticed");
    }

    [Fact]
    public async Task ANewInstanceOnReconnectTriggersReadoption()
    {
        var socketPath = NewSocketPath();
        var first = await FakeDriver.StartAsync(socketPath, FakeDriver.HelloFor("instance-a"));
        await using var harness = await Harness.StartAsync(socketPath);

        await Eventually(() => harness.Hook.CallCount == 1, "the first adoption");

        await first.DisposeAsync();
        await Eventually(
            () => harness.Monitor.Current.Connection is DriverConnection.NotConnected,
            "the loss is noticed");

        await using var second = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-b"));
        second.Sessions =
        [
            new SessionSnapshot(
                SessionId.Parse("rec-2"),
                SessionPurpose.Recording,
                "fake-satellite",
                SessionState.Stopping,
                DateTimeOffset.UtcNow),
        ];

        await Eventually(() => harness.Hook.CallCount == 2, "the re-adoption");

        Assert.Equal("rec-2", Assert.Single(harness.Hook.LastSessions!).SessionId.Value);
        Assert.Equal("instance-b", harness.Monitor.Current.Hello?.InstanceId);
    }

    [Fact]
    public async Task TheSameInstanceOnReconnectDoesNotReadoptAgain()
    {
        var socketPath = NewSocketPath();
        var first = await FakeDriver.StartAsync(socketPath, FakeDriver.HelloFor("instance-a"));
        await using var harness = await Harness.StartAsync(socketPath);

        await Eventually(() => harness.Hook.CallCount == 1, "the first adoption");

        await first.DisposeAsync();
        await Eventually(
            () => harness.Monitor.Current.Connection is DriverConnection.NotConnected,
            "the loss is noticed");

        await using var second = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));

        await Eventually(
            () => harness.Monitor.Current.Connection is DriverConnection.Connected,
            "the reconnection");

        Assert.Equal(1, harness.Hook.CallCount);
    }

    [Fact]
    public async Task ADrainingSignalFlipsTheConnectionState()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        await using var harness = await Harness.StartAsync(socketPath);

        var received = new ConcurrentQueue<string>();
        using var subscription = harness.Signals.Subscribe(received.Enqueue);

        await Eventually(
            () => harness.Monitor.Current.Connection is DriverConnection.Connected,
            "the supervisor connects");

        driver.Signal("draining");

        await Eventually(
            () => harness.Monitor.Current.Connection is DriverConnection.Draining,
            "the draining flip");

        Assert.Contains("draining", received);
        Assert.NotNull(harness.Monitor.Current.Hello);
    }

    [Fact]
    public async Task AHelloAlreadyDrainingReportsDraining()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a", draining: true));
        await using var harness = await Harness.StartAsync(socketPath);

        await Eventually(
            () => harness.Monitor.Current.Connection is DriverConnection.Draining,
            "the draining hello is surfaced");
    }

    [Fact]
    public async Task SignalsReachSubscribersAndUnknownNamesAreIgnored()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        await using var harness = await Harness.StartAsync(socketPath);

        var received = new ConcurrentQueue<string>();
        using var subscription = harness.Signals.Subscribe(received.Enqueue);

        await Eventually(
            () => harness.Monitor.Current.Connection is DriverConnection.Connected,
            "the supervisor connects");

        driver.Signal("somethingFromTheFuture");
        driver.Signal("tuners");
        driver.Signal("sessions");

        await Eventually(
            () => received.Contains("sessions"),
            "the signals arrive");

        Assert.Equal(["tuners", "sessions"], [.. received]);
    }

    [Fact]
    public async Task AMissingCapabilityIsSurfacedAsDegradation()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a", capabilities: ["recording"]));
        await using var harness = await Harness.StartAsync(socketPath);

        await Eventually(
            () => harness.Monitor.Current.Connection is DriverConnection.Connected,
            "the supervisor connects");

        Assert.Equal(["live"], harness.Monitor.Current.MissingCapabilities);
        Assert.True(harness.Monitor.Current.DriverUpdateRequired);
    }
}
