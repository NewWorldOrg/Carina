using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Ipc;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class SessionViewsTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly DriverHello Hello = new(DriverProtocol.Version, "instance-1", []);

    private readonly string root = Directory.CreateTempSubdirectory("carina-views-").FullName;
    private readonly ManualTimeProvider clock = new(Start);
    private readonly List<TunerSessionManager> managers = [];

    public void Dispose()
    {
        foreach (var manager in managers)
        {
            foreach (var session in manager.Sessions)
            {
                session.Dispose();
            }
        }

        Directory.Delete(root, recursive: true);
    }

    private DriverConfiguration Configuration =>
        new(
            "/run/carina/driver.sock",
            [new OutputRootSettings("primary", root)],
            6,
            new TunerSettings(TunerBackend.Fake),
            [
                new DeviceSettings("adapter0", DeviceKind.Terrestrial),
                new DeviceSettings("adapter1", DeviceKind.Satellite),
                new DeviceSettings("adapter2", DeviceKind.Terrestrial, Enabled: false),
            ]
        );

    private TunerSessionManager Manager(ITunerDeviceFactory? factory = null)
    {
        var configuration = Configuration;
        var manager = new TunerSessionManager(
            configuration,
            factory ?? new TunerDeviceFactory(configuration),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

        managers.Add(manager);

        return manager;
    }

    private static TunerSession Begin(
        TunerSessionManager manager,
        string sessionId,
        string? deviceId = null,
        TunerKind kind = TunerKind.Terrestrial
    )
    {
        var start = manager.Begin(
            new StartSessionRequest
            {
                SessionId = SessionId.Parse(sessionId),
                Purpose = SessionPurpose.Live,
                Tuning = new TuningRequest(kind, 27),
                DeviceId = deviceId,
            }
        );

        Assert.True(start.TryGetSession(out var session), start.Detail);

        return session;
    }

    [Fact]
    public void ASessionCarriesItsStateAndItsCounters()
    {
        var manager = Manager();
        var session = Begin(manager, "counted");

        var snapshot = SessionViews.Of(session, Hello);

        Assert.Equal(session.SessionId, snapshot.SessionId);
        Assert.Equal(SessionPurpose.Live, snapshot.Purpose);
        Assert.Equal(session.DeviceId, snapshot.DeviceId);
        Assert.Equal("instance-1", snapshot.InstanceId);
        Assert.Equal(session.StartedAt, snapshot.StartedAt);
        Assert.NotNull(snapshot.Counters);
    }

    [Fact]
    public void AFaultedMeasurementIsNeverHiddenFromTheSnapshot()
    {
        var manager = Manager();
        var session = Begin(manager, "faulted");

        session.Broadcaster.Close(new IOException("the reader went away"));

        var subscription = session.Broadcaster.Subscribe(SubscriberKind.Viewer);
        Assert.True(subscription.IsDisconnected);

        session.Stop();
        session.WaitForEnd(TimeSpan.FromSeconds(5));

        var snapshot = SessionViews.Of(session, Hello);

        Assert.Equal(session.FaultCount, snapshot.FaultCount);
        Assert.Equal(session.StopReason, snapshot.StopReason);
        Assert.Equal(session.BytesRecorded, snapshot.BytesRecorded);
    }

    [Fact]
    public async Task TheFaultCountAndTheFirstFaultTravelTogether()
    {
        var manager = Manager(new ScriptedTunerDeviceFactory(failAfterReads: 1));
        var session = Begin(manager, "broken");

        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var snapshot = SessionViews.Of(session, Hello);

        Assert.Equal(SessionState.Failed, snapshot.State);
        Assert.Equal(SessionStopReason.DeviceFailed, snapshot.StopReason);
        Assert.NotNull(snapshot.FailureCause);
        Assert.True(snapshot.Concluded);
    }

    [Fact]
    public void EverySessionAppearsOnceEvenAfterItEnds()
    {
        var manager = Manager();
        var running = Begin(manager, "running", "adapter0");
        var ending = Begin(manager, "ending", "adapter1", TunerKind.Satellite);

        ending.Stop();
        ending.WaitForEnd(TimeSpan.FromSeconds(5));

        var snapshots = SessionViews.All(manager, Hello);

        Assert.Equal(2, snapshots.Count);
        Assert.Single(snapshots, snapshot => snapshot.SessionId == running.SessionId);
        Assert.Single(snapshots, snapshot => snapshot.SessionId == ending.SessionId);
    }

    [Fact]
    public void AnIdleDriverShowsEveryDeclaredTuner()
    {
        var manager = Manager();

        var tuners = SessionViews.Tuners(Configuration, manager);

        Assert.Equal(3, tuners.Count);
        Assert.Equal(TunerState.Idle, tuners[0].State);
        Assert.Equal(TunerKind.Terrestrial, tuners[0].Kind);
        Assert.Equal(TunerKind.Satellite, tuners[1].Kind);
        Assert.Equal(TunerState.Disabled, tuners[2].State);
        Assert.NotNull(tuners[2].Detail);
    }

    [Fact]
    public void ATunerServingASessionNamesIt()
    {
        var manager = Manager();
        var session = Begin(manager, "busy-one", "adapter0");

        var tuners = SessionViews.Tuners(Configuration, manager);
        var busy = tuners.Single(tuner => tuner.DeviceId == "adapter0");

        Assert.Equal(TunerState.Busy, busy.State);
        Assert.Equal(session.SessionId, busy.SessionId);
    }

    [Fact]
    public void ATunerIsIdleAgainOnceItsSessionEnds()
    {
        var manager = Manager();
        var session = Begin(manager, "short-one", "adapter0");

        session.Stop();
        session.WaitForEnd(TimeSpan.FromSeconds(5));

        var busy = SessionViews
            .Tuners(Configuration, manager)
            .Single(tuner => tuner.DeviceId == "adapter0");

        Assert.Equal(TunerState.Idle, busy.State);
    }
}
