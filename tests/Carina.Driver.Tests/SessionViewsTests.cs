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
        TunerKind kind = TunerKind.Terrestrial,
        SessionPurpose purpose = SessionPurpose.Live,
        TuneParams? tune = null,
        DateTimeOffset? endsAt = null
    )
    {
        var start = manager.Begin(
            new StartSessionRequest
            {
                SessionId = SessionId.Parse(sessionId),
                Purpose = purpose,
                Tuning = new TuningRequest(kind, 55),
                Tune = tune,
                DeviceId = deviceId,
                EndsAt = endsAt,
            }
        );

        Assert.True(start.TryGetSession(out var session), start.Detail);

        return session;
    }

    [Theory]
    [InlineData(SessionPurpose.Scan)]
    [InlineData(SessionPurpose.Survey)]
    [InlineData(SessionPurpose.SurveyNow)]
    public void AWalkingSessionEndsAtTheDriversOwnLimitWhenTheAppNamesNoEnd(SessionPurpose purpose)
    {
        var session = Begin(Manager(), "walk", purpose: purpose);

        Assert.Equal(
            Start.AddMinutes(DriverConfiguration.DefaultWalkSessionMinutes),
            session.EndsAt);
    }

    [Fact]
    public void AWalkingSessionAskingForLongerThanTheDriverAllowsIsCutToTheLimit()
    {
        var session = Begin(
            Manager(),
            "walk",
            purpose: SessionPurpose.Scan,
            endsAt: Start.AddHours(4));

        Assert.Equal(
            Start.AddMinutes(DriverConfiguration.DefaultWalkSessionMinutes),
            session.EndsAt);
    }

    [Fact]
    public void AWalkingSessionAskingForLessThanTheLimitKeepsItsOwnEnd()
    {
        var session = Begin(
            Manager(),
            "walk",
            purpose: SessionPurpose.Survey,
            endsAt: Start.AddMinutes(5));

        Assert.Equal(Start.AddMinutes(5), session.EndsAt);
    }

    [Fact]
    public void ALiveSessionIsNotCutByTheWalkingLimit()
    {
        var session = Begin(Manager(), "live", endsAt: Start.AddHours(4));

        Assert.Equal(Start.AddHours(4), session.EndsAt);
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
    public void AMetricTheTunerReportsOnAnotherScaleIsNamedApartFromOneItDoesNotImplement()
    {
        var factory = new PacedTunerDeviceFactory(
            new ScriptedQualitySource().Answer(Readings.WithCarrierToNoiseOnAnotherScale())
        );
        var manager = Manager(factory);
        Begin(manager, "other-scale", "adapter0");
        ReadOneChunk(factory);

        var reading = Quality(manager, "adapter0");

        Assert.Equal([SignalQualityMetrics.Cnr], reading.MetricsOnAnotherScale);
        Assert.Empty(reading.NotImplementedMetrics);
        Assert.True(reading.Implements(SignalQualityMetrics.Cnr));
        Assert.Null(reading.CnrMilliDecibels);
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
    public void ATunerServingASessionSaysWhatItIsForAndWhatItIsTunedTo()
    {
        var manager = Manager();
        var session = Begin(
            manager,
            "scanning-one",
            "adapter0",
            purpose: SessionPurpose.Scan,
            tune: TuneParams.Terrestrial(55)
        );

        var busy = Tuner(manager, "adapter0");

        Assert.NotNull(busy.CurrentSession);
        Assert.Equal(session.SessionId, busy.CurrentSession.SessionId);
        Assert.Equal(SessionPurpose.Scan, busy.CurrentSession.Purpose);
        Assert.Equal(session.StartedAt, busy.CurrentSession.StartedAt);
        Assert.Equal(TuneParams.Terrestrial(55), busy.CurrentSession.Tune);
    }

    [Fact]
    public void TheMomentATunerComesFreeAgainTravelsWithTheSessionHoldingIt()
    {
        var manager = Manager();
        var session = Begin(
            manager,
            "until-then",
            "adapter0",
            tune: TuneParams.Terrestrial(55),
            endsAt: Start.AddMinutes(30)
        );

        var busy = Tuner(manager, "adapter0");

        Assert.Equal(Start.AddMinutes(30), session.EndsAt);
        Assert.Equal(session.EndsAt, busy.CurrentSession?.EndsAt);
    }

    [Fact]
    public void ASessionThatNamedNoEndStillTellsTheSubtreeTheCapItWasGiven()
    {
        var manager = Manager();
        var session = Begin(manager, "open-ended", "adapter0", tune: TuneParams.Terrestrial(55));

        var held = Tuner(manager, "adapter0").CurrentSession;

        Assert.NotNull(held?.EndsAt);
        Assert.Equal(session.EndsAt, held.EndsAt);
    }

    [Fact]
    public void TheSessionInTheSubtreeIsTheOneNamedBesideIt()
    {
        var manager = Manager();
        Begin(manager, "matched", "adapter0", tune: TuneParams.Terrestrial(55));

        var busy = Tuner(manager, "adapter0");

        Assert.Equal(busy.SessionId, busy.CurrentSession?.SessionId);
    }

    [Fact]
    public void ASessionStartedWithoutTypedParametersStillSaysWhoHoldsTheTunerAndWhatFor()
    {
        var manager = Manager();
        var session = Begin(manager, "legacy", "adapter0");

        var busy = Tuner(manager, "adapter0");

        Assert.NotNull(busy.CurrentSession);
        Assert.Equal(session.SessionId, busy.CurrentSession.SessionId);
        Assert.Equal(SessionPurpose.Live, busy.CurrentSession.Purpose);
        Assert.Null(busy.CurrentSession.Tune);
    }

    [Fact]
    public void ATunerNobodyIsUsingCarriesNoSessionSubtree()
    {
        var manager = Manager();

        Assert.All(
            SessionViews.Tuners(Configuration, manager),
            tuner => Assert.Null(tuner.CurrentSession)
        );
    }

    [Fact]
    public void ATunerNothingHasHappenedToIsHealthyAndSaysNothingChanged()
    {
        var manager = Manager();

        var health = Tuner(manager, "adapter0").Health;

        Assert.NotNull(health);
        Assert.Equal(TunerHealthLevel.Healthy, health.Level);
        Assert.False(health.DisablePending);
        Assert.Null(health.Detail);
        Assert.Null(health.ChangedAt);
    }

    [Fact]
    public void AFaultedTunerCarriesTheFaultAndWhenItWasNoticed()
    {
        var manager = Manager();

        manager.Fault("adapter0", "the frontend stopped answering");

        var health = Tuner(manager, "adapter0").Health;

        Assert.NotNull(health);
        Assert.Equal(TunerHealthLevel.Faulted, health.Level);
        Assert.Equal("the frontend stopped answering", health.Detail);
        Assert.Equal(Start, health.ChangedAt);
    }

    [Fact]
    public void ATunerTurnedOffWhileItHoldsASessionSaysTheDisableIsStillPending()
    {
        var manager = Manager();
        Begin(manager, "draining-one", "adapter0");

        Assert.True(manager.Turn("adapter0", disabled: true));

        var tuner = Tuner(manager, "adapter0");

        Assert.Equal(TunerState.Draining, tuner.State);
        Assert.NotNull(tuner.Health);
        Assert.True(tuner.Health.DisablePending);
        Assert.Equal(Start, tuner.Health.ChangedAt);
    }

    [Fact]
    public void ATunerTurnedOffWhileItHoldsNothingIsAlreadyOffRatherThanPending()
    {
        var manager = Manager();

        Assert.True(manager.Turn("adapter0", disabled: true));

        var tuner = Tuner(manager, "adapter0");

        Assert.Equal(TunerState.Disabled, tuner.State);
        Assert.NotNull(tuner.Health);
        Assert.False(tuner.Health.DisablePending);
    }

    [Fact]
    public void OnlyASatelliteTunerConfiguredToFeedTheAntennaReportsItsSupply()
    {
        var manager = Manager();
        var configuration = Configuration with
        {
            Devices =
            [
                new DeviceSettings("adapter0", DeviceKind.Terrestrial),
                new DeviceSettings("adapter1", DeviceKind.Satellite, LnbPower: true),
                new DeviceSettings("adapter2", DeviceKind.Satellite),
            ],
        };

        var tuners = SessionViews.Tuners(configuration, manager);

        Assert.False(tuners[0].Health?.LnbPowered);
        Assert.True(tuners[1].Health?.LnbPowered);
        Assert.False(tuners[2].Health?.LnbPowered);
    }

    private static TunerSnapshot Tuner(TunerSessionManager manager, string deviceId) =>
        SessionViews
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
