using System.Collections.Concurrent;

using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Driver;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests;

public sealed class DriverConnectionSupervisorTests
{
    private static readonly TimeSpan DrainPoll = TimeSpan.FromSeconds(1);

    private sealed class HurriedClock : TimeProvider
    {
        private readonly ConcurrentQueue<TimeSpan> waits = new();

        public IReadOnlyCollection<TimeSpan> Waits => waits;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
            => new Hurried(this, base.CreateTimer(callback, state, Remember(dueTime), period));

        private TimeSpan Remember(TimeSpan dueTime)
        {
            if (dueTime <= TimeSpan.Zero)
            {
                return dueTime;
            }

            waits.Enqueue(dueTime);

            return TimeSpan.Zero;
        }

        private sealed class Hurried(HurriedClock clock, ITimer inner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
                => inner.Change(clock.Remember(dueTime), period);

            public void Dispose() => inner.Dispose();

            public ValueTask DisposeAsync() => inner.DisposeAsync();
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
            string[]? expectedCapabilities = null,
            Exception? resyncFailure = null,
            TimeProvider? clock = null)
        {
            socketPath ??= NewSocketPath();

            var client = new DriverIpcClient(
                Options.Create(new DriverOptions { SocketPath = socketPath }));
            var monitor = new DriverConnectionMonitor();
            var signals = new DriverSignalRelay(NullLogger<DriverSignalRelay>.Instance);
            var hook = new RecordingResyncHook { Failure = resyncFailure };
            var settings = new DriverSupervisionSettings(
                TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(200),
                expectedCapabilities ?? ["recording", "live"],
                () => 1.0)
            {
                DrainPoll = DrainPoll,
            };

            var supervisor = new DriverConnectionSupervisor(
                client,
                monitor,
                signals,
                hook,
                settings,
                clock ?? TimeProvider.System,
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

        await Eventually.Happens(
            () => harness.Monitor.Current.Connection is DriverConnection.Connected,
            "the supervisor connects");
        await Eventually.Happens(() => harness.Hook.CallCount == 1, "the readoption");

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

        await Eventually.Happens(
            () => harness.Monitor.Current.Connection is DriverConnection.Connected,
            "the supervisor connects");

        await driver.DisposeAsync();

        await Eventually.Happens(
            () => harness.Monitor.Current.Connection is DriverConnection.NotConnected,
            "the loss is noticed");
    }

    [Fact]
    public async Task ANewInstanceOnReconnectTriggersReadoption()
    {
        var socketPath = NewSocketPath();
        var first = await FakeDriver.StartAsync(socketPath, FakeDriver.HelloFor("instance-a"));
        await using var harness = await Harness.StartAsync(socketPath);

        await Eventually.Happens(() => harness.Hook.CallCount == 1, "the first adoption");

        await first.DisposeAsync();
        await Eventually.Happens(
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

        await Eventually.Happens(() => harness.Hook.CallCount == 2, "the re-adoption");

        Assert.Equal("rec-2", Assert.Single(harness.Hook.LastSessions!).SessionId.Value);
        Assert.Equal("instance-b", harness.Monitor.Current.Hello?.InstanceId);
    }

    [Fact]
    public async Task TheSameInstanceOnReconnectDoesNotReadoptAgain()
    {
        var socketPath = NewSocketPath();
        var first = await FakeDriver.StartAsync(socketPath, FakeDriver.HelloFor("instance-a"));
        await using var harness = await Harness.StartAsync(socketPath);

        await Eventually.Happens(() => harness.Hook.CallCount == 1, "the first adoption");

        await first.DisposeAsync();
        await Eventually.Happens(
            () => harness.Monitor.Current.Connection is DriverConnection.NotConnected,
            "the loss is noticed");

        await using var second = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));

        await Eventually.Happens(
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

        await Eventually.Happens(
            () => harness.Monitor.Current.Connection is DriverConnection.Connected,
            "the supervisor connects");
        await Eventually.Happens(() => driver.ListenerCount > 0, "the event feed is subscribed");

        driver.Signal("draining");

        await Eventually.Happens(
            () => harness.Monitor.Current.Connection is DriverConnection.Draining,
            "the draining flip");
        await Eventually.Happens(
            () => received.Contains("draining"),
            "the draining signal reaches subscribers");

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

        await Eventually.Happens(
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

        await Eventually.Happens(
            () => harness.Monitor.Current.Connection is DriverConnection.Connected,
            "the supervisor connects");
        await Eventually.Happens(() => driver.ListenerCount > 0, "the event feed is subscribed");

        driver.Signal("somethingFromTheFuture");
        driver.Signal("tuners");
        driver.Signal("sessions");

        await Eventually.Happens(
            () => received.Contains("sessions"),
            "the signals arrive");

        Assert.Equal(["tuners", "sessions"], [.. received]);
    }

    [Fact]
    public async Task TheTuningLockAndHealthEventNamesAreRecognisedAndReachSubscribers()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        await using var harness = await Harness.StartAsync(socketPath);

        var received = new ConcurrentQueue<string>();
        using var subscription = harness.Signals.Subscribe(received.Enqueue);

        await Eventually.Happens(
            () => harness.Monitor.Current.Connection is DriverConnection.Connected,
            "the supervisor connects");
        await Eventually.Happens(() => driver.ListenerCount > 0, "the event feed is subscribed");

        driver.Signal(DriverEvents.SessionTuned);
        driver.Signal(DriverEvents.SessionLockLost);
        driver.Signal(DriverEvents.TunerHealthChanged);

        await Eventually.Happens(
            () => received.Contains(DriverEvents.TunerHealthChanged),
            "the signals arrive");

        Assert.Equal(
            [DriverEvents.SessionTuned, DriverEvents.SessionLockLost, DriverEvents.TunerHealthChanged],
            [.. received]);
    }

    [Fact]
    public async Task AReconnectToADifferentInstanceIsAnnouncedToSubscribers()
    {
        var socketPath = NewSocketPath();
        var first = await FakeDriver.StartAsync(socketPath, FakeDriver.HelloFor("instance-a"));
        await using var harness = await Harness.StartAsync(socketPath);

        await Eventually.Happens(() => harness.Hook.CallCount == 1, "the first adoption");

        var received = new ConcurrentQueue<string>();
        using var subscription = harness.Signals.Subscribe(received.Enqueue);

        await first.DisposeAsync();
        await Eventually.Happens(
            () => harness.Monitor.Current.Connection is DriverConnection.NotConnected,
            "the loss is noticed");

        await using var second = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-b"));

        await Eventually.Happens(
            () => received.Contains(DriverClientSignals.InstanceChanged),
            "the instance change is announced");
    }

    [Fact]
    public async Task TheFirstAdoptionIsNotAnnouncedAsAnInstanceChange()
    {
        var socketPath = NewSocketPath();
        await using var harness = await Harness.StartAsync(socketPath);

        var received = new ConcurrentQueue<string>();
        using var subscription = harness.Signals.Subscribe(received.Enqueue);

        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));

        await Eventually.Happens(() => harness.Hook.CallCount == 1, "the first adoption");

        Assert.DoesNotContain(DriverClientSignals.InstanceChanged, received);
    }

    [Fact]
    public async Task AReconnectToTheSameInstanceIsNotAnnouncedAsAnInstanceChange()
    {
        var socketPath = NewSocketPath();
        var first = await FakeDriver.StartAsync(socketPath, FakeDriver.HelloFor("instance-a"));
        await using var harness = await Harness.StartAsync(socketPath);

        await Eventually.Happens(() => harness.Hook.CallCount == 1, "the first adoption");

        var received = new ConcurrentQueue<string>();
        using var subscription = harness.Signals.Subscribe(received.Enqueue);

        await first.DisposeAsync();
        await Eventually.Happens(
            () => harness.Monitor.Current.Connection is DriverConnection.NotConnected,
            "the loss is noticed");

        await using var second = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));

        await Eventually.Happens(
            () => harness.Monitor.Current.Connection is DriverConnection.Connected,
            "the reconnection");

        await Task.Delay(200);

        Assert.DoesNotContain(DriverClientSignals.InstanceChanged, received);
        Assert.Equal(1, harness.Hook.CallCount);
    }

    [Fact]
    public async Task AMissingCapabilityIsSurfacedAsDegradation()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a", capabilities: ["recording"]));
        await using var harness = await Harness.StartAsync(socketPath);

        await Eventually.Happens(
            () => harness.Monitor.Current.Connection is DriverConnection.Connected,
            "the supervisor connects");

        Assert.Equal(["live"], harness.Monitor.Current.MissingCapabilities);
        Assert.True(harness.Monitor.Current.DriverUpdateRequired);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(503)]
    public async Task ADriverThatRefusesItsSessionListStaysConnected(int status)
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        driver.RefusalsByPath[DriverEndpoints.Sessions] = new FakeDriver.Refusal(
            status,
            new DriverProblem("sessionsUnavailable", ["The session list is not being served."]));

        await using var harness = await Harness.StartAsync(socketPath);

        await Eventually.Happens(
            () => harness.Monitor.Current.Connection is DriverConnection.Connected,
            "the driver that answers its hello is reported as connected");

        await Task.Delay(200);

        Assert.Equal(DriverConnection.Connected, harness.Monitor.Current.Connection);
        Assert.Equal("instance-a", harness.Monitor.Current.Hello?.InstanceId);
        Assert.Equal(0, harness.Hook.CallCount);
    }

    [Fact]
    public async Task AFailingResyncHookIsNotReportedAsAMissingDriver()
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

        await using var harness = await Harness.StartAsync(
            socketPath,
            resyncFailure: new InvalidOperationException("The recording store is unavailable."));

        await Eventually.Happens(
            () => harness.Monitor.Current.Connection is DriverConnection.Connected,
            "the driver stays connected while readoption fails");

        await Task.Delay(200);

        Assert.Equal(DriverConnection.Connected, harness.Monitor.Current.Connection);
        Assert.NotNull(harness.Monitor.Current.Hello);
        Assert.Equal(0, harness.Hook.CallCount);

        harness.Hook.Failure = null;

        await Eventually.Happens(() => harness.Hook.CallCount == 1, "the readoption retries and succeeds");

        Assert.Equal(DriverConnection.Connected, harness.Monitor.Current.Connection);
    }

    [Fact]
    public async Task ADrainingDriverIsPolledOnItsOwnCadenceAndItsFeedIsLeftAlone()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a", draining: true));
        driver.RefusalsByPath[DriverEndpoints.Events] = new FakeDriver.Refusal(
            503,
            new DriverProblem("draining", ["The driver is shutting down and sends no further events."]));

        var clock = new HurriedClock();
        await using var harness = await Harness.StartAsync(socketPath, clock: clock);

        await Eventually.Happens(() => clock.Waits.Count >= 5, "five supervision rounds");

        Assert.Equal(0, driver.RequestsFor(DriverEndpoints.Events));
        Assert.All(clock.Waits.Take(5), wait => Assert.Equal(DrainPoll, wait));
    }

    [Fact]
    public async Task ARefusedEventFeedBacksOffInsteadOfRestartingAtTheFirstDelay()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        driver.RefusalsByPath[DriverEndpoints.Events] = new FakeDriver.Refusal(
            429,
            new DriverProblem("tooManyListeners", ["This driver carries 1 event listener at a time."]));

        var clock = new HurriedClock();
        await using var harness = await Harness.StartAsync(socketPath, clock: clock);

        await Eventually.Happens(() => clock.Waits.Count >= 5, "five supervision rounds");

        Assert.Equal([20d, 40, 80, 160, 200], MillisecondsOf(clock.Waits, 5));
        Assert.Equal(DriverConnection.Connected, harness.Monitor.Current.Connection);
    }

    [Fact]
    public async Task AnEventFeedThatIsDroppedAtOnceBacksOffInsteadOfRestarting()
    {
        var socketPath = NewSocketPath();
        await using var driver = await FakeDriver.StartAsync(
            socketPath,
            FakeDriver.HelloFor("instance-a"));
        driver.DropEventFeed = true;

        var clock = new HurriedClock();
        await using var harness = await Harness.StartAsync(socketPath, clock: clock);

        await Eventually.Happens(() => clock.Waits.Count >= 5, "five supervision rounds");

        Assert.Equal([20d, 40, 80, 160, 200], MillisecondsOf(clock.Waits, 5));
    }

    private static double[] MillisecondsOf(IReadOnlyCollection<TimeSpan> waits, int count)
        => [.. waits.Take(count).Select(wait => wait.TotalMilliseconds)];
}
