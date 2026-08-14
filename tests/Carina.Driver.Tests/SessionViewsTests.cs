using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Ipc;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Dvb;

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
            factory ?? new TunerDeviceFactory(configuration, TimeProvider.System),
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
                Tuning = new TuningRequest(kind, 55),
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
    public void AFaultedTunerSaysSoAndNamesItsFault()
    {
        var manager = Manager(new SelectiveTunerDeviceFactory("adapter0"));
        var doomed = Begin(manager, "doomed", "adapter0");

        doomed.WaitForEnd(TimeSpan.FromSeconds(5));

        var tuners = SessionViews.Tuners(Configuration, manager);
        var faulted = tuners.Single(tuner => tuner.DeviceId == "adapter0");

        Assert.Equal(TunerState.Faulted, faulted.State);
        Assert.NotNull(faulted.Detail);
        Assert.Contains("doomed", faulted.Detail, StringComparison.Ordinal);
        Assert.Equal(
            TunerState.Idle,
            tuners.Single(tuner => tuner.DeviceId == "adapter1").State
        );
    }

    [Fact]
    public void ATunerBeingReadCarriesTheQualityOfWhatItIsReceiving()
    {
        var factory = new PacedTunerDeviceFactory(
            new ScriptedQualitySource().Answer(
                Readings.Measured(
                    20.5,
                    new LayerBitErrors(0, 12, 1_000_000),
                    new LayerBitErrors(1, 3, 500_000)
                )
            )
        );
        var manager = Manager(factory);
        Begin(manager, "reading", "adapter0");
        ReadOneChunk(factory);

        var reading = Quality(manager, "adapter0");

        Assert.Equal(SignalLock.Locked, reading.Lock);
        Assert.Equal(20_500, reading.CnrMilliDecibels);
        Assert.Equal(
            [new LayerBitErrorCounts(0, 12, 1_000_000), new LayerBitErrorCounts(1, 3, 500_000)],
            reading.PostViterbiBitErrors
        );
    }

    [Fact]
    public void ATunerThatLostItsLockCarriesNoCarrierToNoiseForTheViewToShow()
    {
        var factory = new PacedTunerDeviceFactory(
            new ScriptedQualitySource().Answer(Readings.WithoutLock())
        );
        var manager = Manager(factory);
        Begin(manager, "unlocked", "adapter0");
        ReadOneChunk(factory);

        var reading = Quality(manager, "adapter0");

        Assert.Equal(SignalLock.NotLocked, reading.Lock);
        Assert.Null(reading.CnrMilliDecibels);
        Assert.Empty(reading.PostViterbiBitErrors);
    }

    [Fact]
    public void ALostLockIsCountedOnTheSessionItHappenedTo()
    {
        var factory = new PacedTunerDeviceFactory(
            new ScriptedQualitySource().Answer(Readings.WithoutLock())
        );
        var manager = Manager(factory);
        var session = Begin(manager, "unlocked", "adapter0");
        ReadOneChunk(factory);

        var snapshot = SessionViews.Of(session, Hello);

        Assert.Equal(1, snapshot.Counters.LockLosses);
    }

    [Fact]
    public void AMetricTheTunerDoesNotImplementIsNamedRatherThanLookingLikeAFailedReading()
    {
        var factory = new PacedTunerDeviceFactory(
            new ScriptedQualitySource().Answer(Readings.WithoutCarrierToNoise())
        );
        var manager = Manager(factory);
        Begin(manager, "partial", "adapter0");
        ReadOneChunk(factory);

        var reading = Quality(manager, "adapter0");

        Assert.Equal([SignalQualityMetrics.Cnr], reading.NotImplementedMetrics);
        Assert.False(reading.Implements(SignalQualityMetrics.Cnr));
        Assert.True(reading.Implements(SignalQualityMetrics.PostViterbiBitError));
        Assert.NotEmpty(reading.PostViterbiBitErrors);
    }

    [Fact]
    public void AReadingTheFrontendRefusedIsNeitherLockedNorUnlocked()
    {
        var factory = new PacedTunerDeviceFactory(
            new ScriptedQualitySource { RefuseFromReadNumber = 1 }
        );
        var manager = Manager(factory);
        Begin(manager, "refused", "adapter0");
        ReadOneChunk(factory);

        var reading = Quality(manager, "adapter0");

        Assert.Equal(SignalLock.Unspecified, reading.Lock);
        Assert.Null(reading.CnrMilliDecibels);
        Assert.NotNull(reading.MeasuredAt);
        Assert.Null(reading.LockReadAt);
    }

    [Fact]
    public void TheStatusAndTheStatisticsReachTheViewWithTheTimesTheyWereRead()
    {
        var factory = new PacedTunerDeviceFactory(
            () => new ScriptedQualitySource(clock) { ReadingTakes = TimeSpan.FromMilliseconds(4) }
        );
        var manager = Manager(factory);
        Begin(manager, "timed", "adapter0");
        ReadOneChunk(factory);

        var reading = Quality(manager, "adapter0");

        Assert.Equal(Start, reading.MeasuredAt);
        Assert.Equal(Start.AddMilliseconds(4), reading.LockReadAt);
    }

    [Fact]
    public void ATunerNobodyIsReadingCarriesNoReadingAtAll()
    {
        var manager = Manager();

        Assert.All(
            SessionViews.Tuners(Configuration, manager),
            tuner => Assert.Null(tuner.SignalQuality)
        );
    }

    [Fact]
    public void TheOverrunsCountedAtTheDeviceReachWhoeverAsksAboutTheSession()
    {
        var factory = new PacedTunerDeviceFactory();
        var manager = Manager(factory);
        var session = Begin(manager, "overrun", "adapter0");
        ReadOneChunk(factory);

        factory.Last.Overflows = 2;

        Assert.Equal(2, SessionViews.Of(session, Hello).Counters.DeviceOverflows);
    }

    private static SignalQualityDto Quality(TunerSessionManager manager, string deviceId)
    {
        var tuner = SessionViews
            .Tuners(
                new DriverConfiguration(
                    "/run/carina/driver.sock",
                    null,
                    6,
                    new TunerSettings(TunerBackend.Fake),
                    [new DeviceSettings(deviceId, DeviceKind.Terrestrial)]
                ),
                manager
            )
            .Single(tuner => tuner.DeviceId == deviceId);

        Assert.NotNull(tuner.SignalQuality);

        return tuner.SignalQuality;
    }

    private static void ReadOneChunk(PacedTunerDeviceFactory factory)
    {
        factory.Last.Allow(1);
        factory.Last.AwaitParkedBefore(2);
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
