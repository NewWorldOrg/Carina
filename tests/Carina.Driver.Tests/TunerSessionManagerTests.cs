using System.Collections.Concurrent;
using System.Threading.Channels;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Events;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class TunerSessionManagerTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);

    private readonly string root = Directory.CreateTempSubdirectory("carina-manager-").FullName;

    private readonly ManualTimeProvider clock = new(Start);

    public void Dispose() => Directory.Delete(root, recursive: true);

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
                new DeviceSettings("adapter3", DeviceKind.Terrestrial),
            ]
        );

    private TunerSessionManager Manager() => Manager(Configuration);

    private TunerSessionManager Manager(DriverConfiguration configuration) =>
        Manager(configuration, new TunerDeviceFactory(configuration, TimeProvider.System));

    private TunerSessionManager Manager(
        DriverConfiguration configuration,
        ITunerDeviceFactory factory,
        DriverEventHub? events = null
    ) =>
        new(
            configuration,
            factory,
            clock,
            NullLogger<TunerSessionManager>.Instance,
            events: events
        );

    private static StartSessionRequest Request(
        string sessionId,
        string? deviceId = null,
        SessionPurpose purpose = SessionPurpose.Recording,
        TunerKind kind = TunerKind.Terrestrial,
        string? outputRoot = "primary",
        DateTimeOffset? endsAt = null,
        int channel = 55
    ) =>
        new()
        {
            SessionId = SessionId.Parse(sessionId),
            Purpose = purpose,
            Tuning = new TuningRequest(kind, channel, 50001),
            DeviceId = deviceId,
            OutputRoot = purpose is SessionPurpose.Recording ? outputRoot : null,
            EndsAt = endsAt ?? (purpose is SessionPurpose.Recording ? Start.AddHours(1) : null),
            RecordingId = purpose is SessionPurpose.Recording ? $"k-{sessionId}" : null,
        };

    private static TunerSession Begin(
        TunerSessionManager manager,
        string sessionId,
        string? deviceId = null,
        SessionPurpose purpose = SessionPurpose.Recording,
        TunerKind kind = TunerKind.Terrestrial,
        int channel = 55
    )
    {
        SessionStart start = manager.Begin(
            Request(sessionId, deviceId, purpose, kind, channel: channel)
        );

        Assert.Equal(SessionRefusal.None, start.Refusal);
        Assert.True(start.TryGetSession(out TunerSession? session));

        return session;
    }

    private static SessionRefusal RefusalFor(
        TunerSessionManager manager,
        StartSessionRequest request
    )
    {
        SessionStart start = manager.Begin(request);

        Assert.False(start.TryGetSession(out _));
        Assert.NotEmpty(start.Detail);

        return start.Refusal;
    }

    private static TunerSession WaitForBytes(TunerSession session)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);

        while (session.BytesRecorded is 0 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(1);
        }

        Assert.True(session.BytesRecorded > 0);

        return session;
    }

    private static TunerSession StopAndWait(TunerSession session)
    {
        session.Stop();
        session.WaitForEnd(TimeSpan.FromSeconds(10));

        return session;
    }

    [Fact]
    public void BeginStartsAndTracksASession()
    {
        TunerSessionManager manager = Manager();

        TunerSession session = Begin(manager, "s-1", "adapter0");

        Assert.Equal(SessionState.Active, session.State);
        Assert.True(manager.TryGet(SessionId.Parse("s-1"), out _));

        StopAndWait(session);
    }

    [Fact]
    public void ARecordingSessionWritesUnderTheNamedOutputRoot()
    {
        TunerSessionManager manager = Manager();

        TunerSession session = StopAndWait(WaitForBytes(Begin(manager, "s-1", "adapter0")));

        Assert.True(session.BytesRecorded > 0);
        Assert.Equal("primary", session.OutputRoot);
        Assert.Equal(new FileInfo(Path.Combine(root, "k-s-1.ts")).Length, session.BytesRecorded);
    }

    [Fact]
    public void ARootThisDriverNeverDeclaredIsRefused()
    {
        TunerSessionManager manager = Manager();

        Assert.Equal(
            SessionRefusal.UnknownOutputRoot,
            RefusalFor(manager, Request("s-1", "adapter0", outputRoot: "elsewhere"))
        );
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public void APathInPlaceOfARootNameIsRefusedBeforeAnythingIsOpened()
    {
        TunerSessionManager manager = Manager();

        Assert.Equal(
            SessionRefusal.Rejected,
            RefusalFor(manager, Request("s-1", "adapter0", outputRoot: root))
        );
        Assert.Empty(Directory.GetFiles(root));
    }

    [Fact]
    public void ALiveSessionWritesNoFile()
    {
        TunerSessionManager manager = Manager();

        StopAndWait(Begin(manager, "s-2", "adapter0", SessionPurpose.Live));

        Assert.Empty(Directory.GetFiles(root));
    }

    [Fact]
    public void ALiveSessionThatNamesNoEndTimeGetsTheConfiguredOne()
    {
        TunerSessionManager manager = Manager(Configuration with { LiveSessionMinutes = 45 });

        TunerSession session = Begin(manager, "s-1", "adapter0", SessionPurpose.Live);

        Assert.Equal(Start.AddMinutes(45), session.EndsAt);

        StopAndWait(session);
    }

    [Fact]
    public void ARequestWithoutADeviceIsGivenAFreeOneOfTheRightKind()
    {
        TunerSessionManager manager = Manager();

        TunerSession session = Begin(manager, "s-1", purpose: SessionPurpose.Live);

        Assert.Equal("adapter0", session.DeviceId);

        TunerSession second = Begin(manager, "s-2", purpose: SessionPurpose.Live, channel: 57);

        Assert.Equal("adapter3", second.DeviceId);

        StopAndWait(session);
        StopAndWait(second);
    }

    [Fact]
    public void ARequestWithoutADeviceNeverGetsADisabledOrMismatchedOne()
    {
        TunerSessionManager manager = Manager();

        TunerSession first = Begin(manager, "s-1", purpose: SessionPurpose.Live);
        TunerSession second = Begin(manager, "s-2", purpose: SessionPurpose.Live, channel: 57);

        Assert.Equal(
            SessionRefusal.NoDeviceFree,
            RefusalFor(manager, Request("s-3", purpose: SessionPurpose.Live, channel: 53))
        );

        StopAndWait(first);
        StopAndWait(second);
    }

    [Fact]
    public void ARequestForAKindThisDriverDoesNotServeSaysSo()
    {
        TunerSessionManager manager = Manager(
            Configuration with
            {
                Devices = [new DeviceSettings("adapter0", DeviceKind.Terrestrial)],
            }
        );

        Assert.Equal(
            SessionRefusal.NoDeviceOfThatKind,
            RefusalFor(
                manager,
                Request("s-1", purpose: SessionPurpose.Live, kind: TunerKind.Satellite)
            )
        );
    }

    [Fact]
    public void AnUnknownDeviceIsRefused()
    {
        TunerSessionManager manager = Manager();

        Assert.Equal(
            SessionRefusal.UnknownDevice,
            RefusalFor(manager, Request("s-1", "adapter9"))
        );
    }

    [Fact]
    public void ADisabledDeviceIsRefused()
    {
        TunerSessionManager manager = Manager();

        Assert.Equal(
            SessionRefusal.DisabledDevice,
            RefusalFor(manager, Request("s-1", "adapter2"))
        );
    }

    [Fact]
    public void ADeviceThatServesTheOtherSideIsRefused()
    {
        TunerSessionManager manager = Manager();

        Assert.Equal(
            SessionRefusal.WrongDeviceKind,
            RefusalFor(manager, Request("s-1", "adapter0", kind: TunerKind.Satellite))
        );
    }

    [Fact]
    public void ADeviceAlreadyServingASessionIsNotHandedOutTwice()
    {
        TunerSessionManager manager = Manager();
        TunerSession first = Begin(manager, "s-1", "adapter0");

        Assert.Equal(
            SessionRefusal.DeviceBusy,
            RefusalFor(manager, Request("s-2", "adapter0", channel: 57))
        );

        StopAndWait(first);
    }

    [Fact]
    public void OnlyOneTuningWinsWhenManySimultaneousRequestsRaceForOneDevice()
    {
        TunerSessionManager manager = Manager();
        var granted = new ConcurrentBag<TunerSession>();

        Parallel.For(
            0,
            16,
            index =>
            {
                StartSessionRequest request = Request($"s-{index}", "adapter0", channel: index % 2 is 0 ? 55 : 57);

                if (manager.Begin(request).TryGetSession(out TunerSession? session))
                {
                    granted.Add(session);
                }
            }
        );

        Assert.Equal(8, granted.Count);
        Assert.All(granted, session => Assert.Equal("adapter0", session.DeviceId));

        foreach (TunerSession session in granted)
        {
            StopAndWait(session);
        }
    }

    [Fact]
    public void ManySimultaneousRequestsForOneTuningAllEndUpOnTheOneDevice()
    {
        TunerSessionManager manager = Manager();
        var granted = new ConcurrentBag<TunerSession>();

        Parallel.For(
            0,
            16,
            index =>
            {
                if (manager.Begin(Request($"s-{index}", "adapter0")).TryGetSession(out TunerSession? session))
                {
                    granted.Add(session);
                }
            }
        );

        Assert.Equal(1 + SessionBroadcaster.DefaultSubscriberLimit, granted.Count);
        Assert.All(granted, session => Assert.Equal("adapter0", session.DeviceId));

        foreach (TunerSession session in granted)
        {
            StopAndWait(session);
        }
    }

    [Fact]
    public void ARefusedRequestLeavesTheDeviceFreeForTheNext()
    {
        TunerSessionManager manager = Manager();

        Directory.CreateDirectory(Path.Combine(root, "k-s-1.ts"));

        Assert.Equal(
            SessionRefusal.OutputUnavailable,
            RefusalFor(manager, Request("s-1", "adapter0"))
        );

        StopAndWait(Begin(manager, "s-2", "adapter0"));
    }

    [Fact]
    public void ADeviceIsFreeAgainOnceItsSessionEnds()
    {
        TunerSessionManager manager = Manager();

        StopAndWait(Begin(manager, "s-1", "adapter0"));

        StopAndWait(Begin(manager, "s-2", "adapter0"));
    }

    [Fact]
    public void AnEndTimeAlreadyBehindTheDriverIsRefused()
    {
        TunerSessionManager manager = Manager();

        Assert.Equal(
            SessionRefusal.Rejected,
            RefusalFor(manager, Request("s-1", "adapter0", endsAt: Start.AddHours(-1)))
        );
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public void TheSameIdentifierIsNotUsedTwice()
    {
        TunerSessionManager manager = Manager();
        TunerSession first = Begin(manager, "s-1", "adapter0");

        Assert.Equal(
            SessionRefusal.DuplicateSession,
            RefusalFor(manager, Request("s-1", "adapter1", kind: TunerKind.Satellite))
        );

        StopAndWait(first);
    }

    [Fact]
    public void TheIdentifierOfAFinishedSessionIsNotReused()
    {
        TunerSessionManager manager = Manager();

        StopAndWait(Begin(manager, "s-1", "adapter0"));

        Assert.Equal(
            SessionRefusal.DuplicateSession,
            RefusalFor(manager, Request("s-1", "adapter3"))
        );
    }

    [Fact]
    public void AnEndedSessionLeavesTheActiveSetButStaysAvailable()
    {
        TunerSessionManager manager = Manager();

        TunerSession session = StopAndWait(Begin(manager, "s-1", "adapter0"));

        Assert.Empty(manager.Sessions);
        Assert.True(manager.TryGet(SessionId.Parse("s-1"), out TunerSession? found));
        Assert.Same(session, found);
        Assert.Equal(SessionStopReason.Requested, found.StopReason);
    }

    [Fact]
    public async Task StoppingTellsApartASessionThatIsGoneFromOneThatNeverWas()
    {
        TunerSessionManager manager = Manager();
        TunerSession session = Begin(manager, "s-1", "adapter0");

        Assert.Equal(
            SessionStopOutcome.Stopped,
            await manager.StopAsync(SessionId.Parse("s-1"), "test", CancellationToken.None)
        );
        Assert.True(session.Concluded);
        Assert.Equal(
            SessionStopOutcome.AlreadyEnded,
            await manager.StopAsync(SessionId.Parse("s-1"), "test", CancellationToken.None)
        );
        Assert.Equal(
            SessionStopOutcome.NoSuchSession,
            await manager.StopAsync(SessionId.Parse("s-9"), "test", CancellationToken.None)
        );
    }

    [Fact]
    public void AFailedSessionKeepsItsCauseWhereItCanBeRead()
    {
        var manager = new TunerSessionManager(
            Configuration,
            new ScriptedTunerDeviceFactory(failAfterReads: 1),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

        TunerSession session = Begin(manager, "s-1", "adapter0", SessionPurpose.Live);

        session.WaitForEnd(TimeSpan.FromSeconds(10));

        Assert.Empty(manager.Sessions);
        Assert.True(manager.TryGet(SessionId.Parse("s-1"), out TunerSession? found));
        Assert.Equal(SessionState.Failed, found.State);
        Assert.IsType<IOException>(found.FailureCause);
    }

    [Fact]
    public async Task TheDrainStopsAViewerButWaitsForARecordingWithItsStreamsAttached()
    {
        TunerSessionManager manager = Manager();
        TunerSession recording = Begin(manager, "s-1", "adapter0");
        TunerSession live = Begin(manager, "s-2", "adapter1", SessionPurpose.Live, TunerKind.Satellite);

        Task draining = manager.DrainAsync(CancellationToken.None);

        live.WaitForEnd(TimeSpan.FromSeconds(10));

        Assert.Equal(SessionState.Stopped, live.State);
        Assert.False(draining.IsCompleted);
        Assert.Equal(SessionState.Active, recording.State);
        Assert.False(recording.Broadcaster.IsClosed);

        clock.Advance(TimeSpan.FromHours(2));

        await draining;

        Assert.Equal(SessionState.Stopped, recording.State);
        Assert.Equal(SessionStopReason.EndTimeReached, recording.StopReason);
    }

    [Fact]
    public async Task ARecordingThatOutlastsTheGraceCapIsStoppedAndSaysWhy()
    {
        TunerSessionManager manager = Manager(Configuration with { ShutdownGraceHours = 0 });
        TunerSession recording = Begin(manager, "s-1", "adapter0");

        await manager.DrainAsync(CancellationToken.None);

        Assert.Equal(SessionState.Failed, recording.State);
        Assert.Equal(SessionStopReason.DrainCapReached, recording.StopReason);
        Assert.NotNull(recording.FailureCause);
        Assert.False(manager.IsFaulted("adapter0", out _));
    }

    [Fact]
    public async Task TheDrainRunsOnceAndStopAsyncJoinsIt()
    {
        TunerSessionManager manager = Manager(Configuration with { ShutdownGraceHours = 0 });
        TunerSession recording = Begin(manager, "s-1", "adapter0");

        Task draining = manager.DrainAsync(CancellationToken.None);

        Assert.Same(draining, manager.DrainAsync(CancellationToken.None));
        Assert.Same(draining, manager.StopAsync(CancellationToken.None));

        await draining;

        Assert.Equal(SessionStopReason.DrainCapReached, recording.StopReason);
    }

    [Fact]
    public async Task ShutdownGivesUpOnASessionThatWillNotLetGo()
    {
        TimeSpan hardStop = TimeSpan.FromSeconds(1);
        var clockOfItsOwn = new SteppedTimeProvider(Start);
        var device = new HeldOpenTunerDevice();
        var manager = new TunerSessionManager(
            Configuration with { ShutdownGraceHours = 0 },
            new OneTunerDeviceFactory(device),
            clockOfItsOwn,
            NullLogger<TunerSessionManager>.Instance,
            hardStopLimit: hardStop
        );

        TunerSession recording = Begin(manager, "s-1", "adapter0");

        Assert.True(
            device.Reading.Wait(Deadlock),
            "The session never reached the device that will not give it back, so there was nothing for the drain to give up on."
        );

        Task draining = manager.DrainAsync(CancellationToken.None);

        clockOfItsOwn.AwaitSomethingWaitingOnTheClock(Deadlock);

        Assert.False(
            draining.IsCompleted,
            "The drain let go of a session that still held its device, before the hard stop it promised had run out."
        );

        clockOfItsOwn.Advance(hardStop);

        await draining;

        Assert.False(recording.Completion.IsCompleted);
        Assert.False(recording.Concluded);

        device.LetGo();
        recording.WaitForEnd(Deadlock);
    }

    [Fact]
    public async Task AWedgedLiveSessionDoesNotHoldTheDrainOnceEveryRecordingIsDone()
    {
        var manager = new TunerSessionManager(
            Configuration,
            new StubbornForOneDeviceFactory("adapter1", TimeSpan.FromSeconds(10)),
            clock,
            NullLogger<TunerSessionManager>.Instance,
            hardStopLimit: TimeSpan.FromSeconds(1)
        );

        TunerSession recording = Begin(manager, "s-1", "adapter0");
        TunerSession wedged = Begin(manager, "s-2", "adapter1", SessionPurpose.Live, TunerKind.Satellite);

        Task draining = manager.DrainAsync(CancellationToken.None);
        DateTime started = DateTime.UtcNow;

        recording.Stop();

        await draining;

        Assert.True(
            DateTime.UtcNow - started < TimeSpan.FromSeconds(15),
            $"The drain took {DateTime.UtcNow - started} although the only recording had finished."
        );
        Assert.Equal(SessionState.Stopped, recording.State);
        Assert.False(wedged.Completion.IsCompleted);
    }

    [Fact]
    public async Task ASessionThatBeginsWhileTheDrainSnapshotsIsRefusedNotOrphaned()
    {
        TunerSessionManager manager = Manager();
        int refusals = 0;
        var granted = new ConcurrentBag<TunerSession>();

        var beginning = Task.Run(() =>
        {
            for (int index = 0; index < 200; index++)
            {
                SessionStart start = manager.Begin(
                    new StartSessionRequest
                    {
                        SessionId = SessionId.Parse($"s-{index}"),
                        Purpose = SessionPurpose.Live,
                        Tuning = new TuningRequest(TunerKind.Terrestrial, 55),
                    }
                );

                if (start.TryGetSession(out TunerSession? session))
                {
                    granted.Add(session);

                    if (!manager.IsDraining)
                    {
                        session.Stop();
                    }
                }
                else
                {
                    Interlocked.Increment(ref refusals);

                    if (start.Refusal is SessionRefusal.Draining)
                    {
                        break;
                    }
                }
            }
        });

        await Task.Delay(TimeSpan.FromMilliseconds(30));
        await manager.DrainAsync(CancellationToken.None);
        await beginning;

        foreach (TunerSession session in granted)
        {
            session.WaitForEnd(TimeSpan.FromSeconds(10));

            Assert.True(
                session.Concluded,
                $"'{session.SessionId}' was granted but nobody concluded it."
            );
        }

        Assert.True(refusals > 0);
    }

    [Fact]
    public async Task ShutdownDoesNotReturnBeforeEverySessionOutcomeIsWrittenDown()
    {
        var log = new CapturingLogger<TunerSessionManager>();
        var manager = new TunerSessionManager(
            Configuration,
            new TunerDeviceFactory(Configuration, TimeProvider.System),
            clock,
            log
        );

        Begin(manager, "s-1", "adapter0", SessionPurpose.Live);
        Begin(manager, "s-2", "adapter1", SessionPurpose.Live, TunerKind.Satellite);

        await manager.StopAsync(CancellationToken.None);

        Assert.Contains(log.Lines, line => line.Contains("s-1") && line.Contains("ended"));
        Assert.Contains(log.Lines, line => line.Contains("s-2") && line.Contains("ended"));
    }

    [Fact]
    public async Task NoSessionStartsOnceShutdownHasBegun()
    {
        TunerSessionManager manager = Manager();

        await manager.StopAsync(CancellationToken.None);

        Assert.Equal(
            SessionRefusal.Draining,
            RefusalFor(manager, Request("s-1", "adapter0"))
        );
        Assert.True(manager.IsDraining);
    }

    [Fact]
    public void ADeviceThatFailedItsSessionIsFaultedAndNotHandedOutAgain()
    {
        var manager = new TunerSessionManager(
            Configuration,
            new SelectiveTunerDeviceFactory("adapter0"),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

        TunerSession doomed = Begin(manager, "s-1", "adapter0", SessionPurpose.Live);

        doomed.WaitForEnd(TimeSpan.FromSeconds(10));

        Assert.Equal(SessionState.Failed, doomed.State);
        Assert.True(manager.IsFaulted("adapter0", out string? detail));
        Assert.Contains("s-1", detail, StringComparison.Ordinal);

        Assert.Equal(
            SessionRefusal.FaultedDevice,
            RefusalFor(manager, Request("s-2", "adapter0", purpose: SessionPurpose.Live))
        );

        TunerSession rerouted = Begin(manager, "s-3", purpose: SessionPurpose.Live);

        Assert.Equal("adapter3", rerouted.DeviceId);

        StopAndWait(rerouted);
    }

    [Fact]
    public void WhenEveryDeviceOfAKindIsFaultedTheCallerHearsWhy()
    {
        var manager = new TunerSessionManager(
            Configuration,
            new ScriptedTunerDeviceFactory(failAfterReads: 1),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

        Begin(manager, "s-1", "adapter0", SessionPurpose.Live).WaitForEnd(TimeSpan.FromSeconds(10));
        Begin(manager, "s-2", "adapter3", SessionPurpose.Live).WaitForEnd(TimeSpan.FromSeconds(10));

        Assert.Equal(
            SessionRefusal.FaultedDevice,
            RefusalFor(manager, Request("s-3", purpose: SessionPurpose.Live))
        );
    }

    [Fact]
    public void ADeliberateStopDoesNotFaultTheDevice()
    {
        TunerSessionManager manager = Manager();

        StopAndWait(Begin(manager, "s-1", "adapter0"));

        Assert.False(manager.IsFaulted("adapter0", out _));
    }

    [Fact]
    public void ARecordingWriteFailureDoesNotFaultTheDevice()
    {
        var manager = new TunerSessionManager(
            Configuration,
            new TunerDeviceFactory(Configuration, TimeProvider.System),
            clock,
            NullLogger<TunerSessionManager>.Instance,
            recordingWriters: new BrittleRecordingWriterFactory()
        );

        TunerSession starved = Begin(manager, "s-1", "adapter0");

        starved.WaitForEnd(TimeSpan.FromSeconds(10));

        Assert.Equal(SessionStopReason.RecordingFailed, starved.StopReason);
        Assert.False(manager.IsFaulted("adapter0", out _));

        StopAndWait(Begin(manager, "s-2", "adapter0", SessionPurpose.Live));
    }

    [Fact]
    public void ClearingAFaultMakesTheDeviceUsableAgain()
    {
        var manager = new TunerSessionManager(
            Configuration,
            new SelectiveTunerDeviceFactory("adapter0"),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

        Begin(manager, "s-1", "adapter0", SessionPurpose.Live).WaitForEnd(TimeSpan.FromSeconds(10));

        Assert.True(manager.IsFaulted("adapter0", out _));

        TunerSessionManager restarted = Manager();

        Assert.False(restarted.IsFaulted("adapter0", out _));

        StopAndWait(Begin(restarted, "s-2", "adapter0", SessionPurpose.Live));
    }

    [Fact]
    public void OneDeviceFailingLeavesTheOtherSessionAlone()
    {
        TunerSessionManager manager = Manager();
        TunerSession healthy = Begin(manager, "s-1", "adapter0");

        using var failing = new TunerSession(
            SessionId.Parse("s-9"),
            SessionPurpose.Live,
            "adapter1",
            new ScriptedTunerDevice(failAfterReads: 1),
            Start,
            Start.AddHours(1),
            clock
        );

        failing.Start();
        failing.WaitForEnd(TimeSpan.FromSeconds(10));

        Assert.Equal(SessionState.Failed, failing.State);
        Assert.Equal(SessionState.Active, healthy.State);

        StopAndWait(healthy);
    }

    [Fact]
    public void ADriverConfiguredForRealHardwareSaysSoRatherThanPretending()
    {
        TunerSessionManager manager = Manager(Configuration with { Tuner = new TunerSettings(TunerBackend.Dvb) });

        Assert.Equal(
            SessionRefusal.DeviceUnavailable,
            RefusalFor(manager, Request("s-1", "adapter0"))
        );
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public void ARecordingThatComesBackCarriesOnInTheFileItLeftBehind()
    {
        TunerSessionManager manager = Manager();
        string file = Path.Combine(root, "k-s-1.ts");

        TunerSession interrupted = StopAndWait(WaitForBytes(Begin(manager, "s-1", "adapter0")));
        long written = new FileInfo(file).Length;

        Assert.Equal(interrupted.BytesRecorded, written);

        SessionStart resumed = manager.Begin(
            Request("s-5", "adapter0") with { RecordingId = "k-s-1" }
        );

        Assert.True(resumed.TryGetSession(out TunerSession? carrying), resumed.Detail);

        StopAndWait(WaitForBytes(carrying));

        Assert.Equal(file, carrying.RecordingPath);
        Assert.Equal(written + carrying.BytesRecorded, new FileInfo(file).Length);
        Assert.Single(Directory.GetFiles(root));
    }

    [Fact]
    public void AnEndThatTheDriverHasAlreadyPassedIsNoExtensionEvenThoughItFollowsTheCurrentOne()
    {
        var device = new PacedTunerDevice();
        var manager = new TunerSessionManager(
            Configuration,
            new OneTunerDeviceFactory(device),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

        SessionStart start = manager.Begin(
            Request("s-1", "adapter0", endsAt: Start.AddMinutes(5))
        );

        Assert.True(start.TryGetSession(out TunerSession? session), start.Detail);

        device.AwaitParkedBefore(1);
        clock.Advance(TimeSpan.FromMinutes(10));

        SessionExtension refused = manager.Extend(
            session.SessionId,
            new ExtendSessionRequest { EndsAt = Start.AddMinutes(7) }
        );

        Assert.Equal(SessionExtendOutcome.NotAnExtension, refused.Outcome);
        Assert.Equal(Start.AddMinutes(5), session.EndsAt);

        SessionExtension accepted = manager.Extend(
            session.SessionId,
            new ExtendSessionRequest { EndsAt = Start.AddMinutes(20) }
        );

        Assert.Equal(SessionExtendOutcome.Extended, accepted.Outcome);
        Assert.Equal(Start.AddMinutes(20), session.EndsAt);

        session.Dispose();
    }

    [Fact]
    public void TwoSessionsAreNeverBothWritingTheOneRecording()
    {
        TunerSessionManager manager = Manager();

        Begin(manager, "s-1", "adapter0");

        Assert.Equal(
            SessionRefusal.RecordingAlreadyExists,
            RefusalFor(manager, Request("s-5", "adapter3") with { RecordingId = "k-s-1" })
        );
        Assert.Single(manager.Sessions);
    }

    [Fact]
    public void AChannelTheTypedParametersDoNotNameNeverReachesTheHardware()
    {
        TunerSessionManager manager = Manager();

        var request = new StartSessionRequest
        {
            SessionId = SessionId.Parse("s-9"),
            Purpose = SessionPurpose.Live,
            Tuning = new TuningRequest(TunerKind.Terrestrial, 900, -5),
            Tune = TuneParams.Terrestrial(55),
        };

        Assert.Equal(SessionRefusal.Rejected, RefusalFor(manager, request));
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public void ARequestWhoseOlderFieldIsNullIsRefusedInsteadOfEndingTheProcess()
    {
        TunerSessionManager manager = Manager();

        var request = new StartSessionRequest
        {
            SessionId = SessionId.Parse("s-9"),
            Purpose = SessionPurpose.Live,
            Tuning = null!,
            Tune = TuneParams.Terrestrial(55),
        };

        Assert.Equal(SessionRefusal.Rejected, RefusalFor(manager, request));
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public void ATunePhrasedOnlyInTypedParametersReachesTheDeviceThatServesThatSystem()
    {
        TunerSessionManager manager = Manager();

        var tune = TuneParams.Bs(15, 50001);
        var request = new StartSessionRequest
        {
            SessionId = SessionId.Parse("s-9"),
            Purpose = SessionPurpose.Scan,
            Tuning = tune.ToLegacyRequest(),
            Tune = tune,
        };

        SessionStart start = manager.Begin(request);

        Assert.True(start.TryGetSession(out TunerSession? session));
        Assert.Equal(SessionRefusal.None, start.Refusal);
        Assert.Equal("adapter1", session.DeviceId);

        StopAndWait(session);
    }

    [Fact]
    public void ATypedTuneOnASystemNoDeviceServesIsRefusedForTheDevicesRatherThanTheProtocol()
    {
        TunerSessionManager manager = Manager(
            Configuration with
            {
                Devices = [new DeviceSettings("adapter0", DeviceKind.Terrestrial)],
            }
        );

        var tune = TuneParams.Cs110(24);
        var request = new StartSessionRequest
        {
            SessionId = SessionId.Parse("s-9"),
            Purpose = SessionPurpose.Scan,
            Tuning = tune.ToLegacyRequest(),
            Tune = tune,
        };

        SessionStart start = manager.Begin(request);

        Assert.Equal(SessionRefusal.NoDeviceOfThatKind, start.Refusal);
        Assert.NotEqual(SessionRefusal.CapabilityMissing, start.Refusal);
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public void ATunerTurnedOffWhileRunningIsNotHandedToATypedTune()
    {
        TunerSessionManager manager = Manager();

        Assert.True(manager.Turn("adapter1", disabled: true));

        Assert.Equal(
            SessionRefusal.NoDeviceOfThatKind,
            RefusalFor(manager, TypedSatellite("s-9"))
        );
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public void AFaultedTunerIsRefusedAsFaultedRatherThanAsAKindNoDeviceServes()
    {
        TunerSessionManager manager = Manager();

        manager.Fault("adapter1", "the delivery systems it reports are not the ones recorded");

        SessionStart start = manager.Begin(TypedSatellite("s-9"));

        Assert.Equal(SessionRefusal.FaultedDevice, start.Refusal);
        Assert.NotEqual(SessionRefusal.NoDeviceOfThatKind, start.Refusal);
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public void ANamedTunerTurnedOffWhileRunningSaysSoRatherThanBlamingTheTypedTune()
    {
        TunerSessionManager manager = Manager();

        Assert.True(manager.Turn("adapter1", disabled: true));

        SessionStart start = manager.Begin(TypedSatellite("s-9", "adapter1"));

        Assert.Equal(SessionRefusal.DisabledDevice, start.Refusal);
        Assert.NotEqual(SessionRefusal.WrongDeviceKind, start.Refusal);
    }

    [Fact]
    public void ANamedTunerOfTheOtherKindIsJudgedAgainstTheSystemTheTypedParametersName()
    {
        TunerSessionManager manager = Manager();

        SessionStart start = manager.Begin(TypedSatellite("s-9", "adapter0"));

        Assert.Equal(SessionRefusal.WrongDeviceKind, start.Refusal);
        Assert.Contains(
            TunerKind.Satellite.ToString(),
            start.Detail,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ASessionThatTunesRingsTheSignalTheAppListensFor()
    {
        var hub = new DriverEventHub();
        TunerSessionManager manager = Manager(
            Configuration,
            new TunerDeviceFactory(Configuration, TimeProvider.System),
            hub
        );

        Assert.True(hub.TryListen(out DriverEventListener? listener));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        TunerSession session = Begin(manager, "tuning", "adapter0");

        var signalled = new List<string>();
        while (!signalled.Contains(DriverEvents.SessionTuned, StringComparer.Ordinal))
        {
            signalled.AddRange(await listener.Take(deadline.Token));
        }

        listener.Dispose();
        session.Stop();
    }

    [Fact]
    public async Task ASessionRidingATunerAlreadyOnItsChannelDoesNotClaimToHaveTunedIt()
    {
        var hub = new DriverEventHub();
        TunerSessionManager manager = Manager(
            Configuration,
            new TunerDeviceFactory(Configuration, TimeProvider.System),
            hub
        );

        TunerSession held = Begin(manager, "holding", purpose: SessionPurpose.Live);

        Assert.True(hub.TryListen(out DriverEventListener? listener));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        TunerSession riding = Begin(manager, "riding", purpose: SessionPurpose.Live);

        Assert.Equal(held.DeviceId, riding.DeviceId);

        var signalled = new List<string>();
        while (!signalled.Contains(DriverEvents.Sessions, StringComparer.Ordinal))
        {
            signalled.AddRange(await listener.Take(deadline.Token));
        }

        Assert.DoesNotContain(DriverEvents.SessionTuned, signalled);

        listener.Dispose();
        riding.Stop();
        held.Stop();
    }

    [Fact]
    public async Task ASessionThatLosesItsLockRingsTheSignalTheAppListensFor()
    {
        var hub = new DriverEventHub();
        var factory = new PacedTunerDeviceFactory(
            new ScriptedQualitySource().Answer(Readings.WithoutLock())
        );
        TunerSessionManager manager = Manager(Configuration, factory, hub);

        Assert.True(hub.TryListen(out DriverEventListener? listener));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        TunerSession session = Begin(manager, "losing", "adapter0");

        factory.Last.Allow(1);
        factory.Last.AwaitParkedBefore(2);

        var signalled = new List<string>();
        while (!signalled.Contains(DriverEvents.SessionLockLost, StringComparer.Ordinal))
        {
            signalled.AddRange(await listener.Take(deadline.Token));
        }

        Assert.Equal(1, session.LockLosses);

        listener.Dispose();
        session.Stop();
    }

    [Fact]
    public void TheIntervalTheConfigurationNamesIsTheOneASessionReadsOn()
    {
        var signal = new ScriptedQualitySource();
        var factory = new PacedTunerDeviceFactory(signal);
        TunerSessionManager manager = Manager(
            Configuration with
            {
                Tuner = new TunerSettings(TunerBackend.Fake, SignalQualitySeconds: 30),
            },
            factory
        );
        TunerSession session = Begin(manager, "spaced", "adapter0");

        factory.Last.Allow(3);
        factory.Last.AwaitParkedBefore(4);

        Assert.Equal(1, signal.Reads);

        clock.Advance(TimeSpan.FromSeconds(30));
        factory.Last.Allow(1);
        factory.Last.AwaitParkedBefore(5);

        Assert.Equal(2, signal.Reads);

        session.Stop();
    }

    [Fact]
    public void AChannelWalkReadsTheSignalOftenEnoughForOneShortVisit()
    {
        var signal = new ScriptedQualitySource();
        var factory = new PacedTunerDeviceFactory(signal);
        TunerSessionManager manager = Manager(
            Configuration with
            {
                Tuner = new TunerSettings(TunerBackend.Fake, SignalQualitySeconds: 30),
            },
            factory
        );
        TunerSession session = Begin(manager, "walking", "adapter0", SessionPurpose.Scan);

        factory.Last.Allow(3);
        factory.Last.AwaitParkedBefore(4);

        Assert.Equal(1, signal.Reads);

        clock.Advance(TimeSpan.FromSeconds(2));
        factory.Last.Allow(1);
        factory.Last.AwaitParkedBefore(5);

        Assert.Equal(2, signal.Reads);

        session.Stop();
    }

    [Fact]
    public void AChannelWalkDoesNotReadTheSignalMoreOftenThanTheConfigurationAsksFor()
    {
        var signal = new ScriptedQualitySource();
        var factory = new PacedTunerDeviceFactory(signal);
        TunerSessionManager manager = Manager(
            Configuration with
            {
                Tuner = new TunerSettings(TunerBackend.Fake, SignalQualitySeconds: 1),
            },
            factory
        );
        TunerSession session = Begin(manager, "walking-fast", "adapter0", SessionPurpose.Scan);

        factory.Last.Allow(3);
        factory.Last.AwaitParkedBefore(4);

        Assert.Equal(1, signal.Reads);

        clock.Advance(TimeSpan.FromSeconds(1));
        factory.Last.Allow(1);
        factory.Last.AwaitParkedBefore(5);

        Assert.Equal(2, signal.Reads);

        session.Stop();
    }

    [Fact]
    public void GatheringTheGuideKeepsTheIntervalTheConfigurationNames()
    {
        var signal = new ScriptedQualitySource();
        var factory = new PacedTunerDeviceFactory(signal);
        TunerSessionManager manager = Manager(
            Configuration with
            {
                Tuner = new TunerSettings(TunerBackend.Fake, SignalQualitySeconds: 30),
            },
            factory
        );
        TunerSession session = Begin(manager, "surveying", "adapter0", SessionPurpose.Survey);

        factory.Last.Allow(3);
        factory.Last.AwaitParkedBefore(4);

        Assert.Equal(1, signal.Reads);

        clock.Advance(TimeSpan.FromSeconds(2));
        factory.Last.Allow(1);
        factory.Last.AwaitParkedBefore(5);

        Assert.Equal(1, signal.Reads);

        session.Stop();
    }

    [Fact]
    public void ADriverThatTunesFromTypedParametersSaysSo()
    {
        Assert.Contains(
            DriverCapabilities.TypedTuning,
            Carina.Driver.Ipc.DriverGreeting.Capabilities
        );
    }

    [Fact]
    public void TheGreetingAdvertisesEverythingThisDriverCanDo()
    {
        Assert.Equal(
            [
                DriverCapabilities.Recording,
                DriverCapabilities.Live,
                DriverCapabilities.QualityMetering,
                DriverCapabilities.DeviceDetection,
                DriverCapabilities.SessionStopReason,
                DriverCapabilities.TunerLedger,
                DriverCapabilities.LiveTunerToggle,
                DriverCapabilities.TypedTuning,
                DriverCapabilities.SignalQuality,
                DriverCapabilities.GracefulRestart,
                DriverCapabilities.RecordingExtension,
                DriverCapabilities.CcMeasurement,
                DriverCapabilities.ScrambleMeasurement,
                DriverCapabilities.DropPositions,
                DriverCapabilities.Storage,
                DriverCapabilities.RecordingErasure,
                "signalQuality.cnr",
                "signalQuality.postViterbiBitError",
                "sessionPurpose.surveyNow",
            ],
            Carina.Driver.Ipc.DriverGreeting.Capabilities
        );
    }

    [Fact]
    public void TheGreetingNamesTheMetricsItCanReadRatherThanQualityAsOneThing()
    {
        DriverHello hello = Carina.Driver.Ipc.DriverGreeting.ForThisProcess();

        Assert.Equal(SignalQualityMetrics.All, hello.DeclaredSignalQualityMetrics());
        Assert.True(hello.SupportsSignalQualityMetric(SignalQualityMetrics.Cnr));
        Assert.True(
            hello.SupportsSignalQualityMetric(SignalQualityMetrics.PostViterbiBitError)
        );
    }

    [Fact]
    public void TheGreetingDoesNotClaimAMetricNoTunerHereReports()
    {
        Assert.False(
            Carina.Driver.Ipc.DriverGreeting.ForThisProcess()
                .SupportsSignalQualityMetric("signalStrength")
        );
    }

    [Fact]
    public async Task DrainingIsSignalledOnceHoweverOftenItIsEntered()
    {
        var hub = new DriverEventHub();
        TunerSessionManager manager = Manager(
            Configuration,
            new TunerDeviceFactory(Configuration, TimeProvider.System),
            hub
        );

        Assert.True(hub.TryListen(out DriverEventListener? listener));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        manager.EnterDraining();

        Assert.Equal([DriverEvents.Draining], await listener!.Take(deadline.Token));

        manager.EnterDraining();
        Assert.True(manager.TryEnterDrainingUnlessRecording(out _));
        await manager.DrainAsync(CancellationToken.None);

        hub.CloseAll();

        await Assert.ThrowsAsync<ChannelClosedException>(() => listener.Take(deadline.Token));
    }

    [Fact]
    public void ADrainingManagerStillNamesTheRecordingItIsWaitingFor()
    {
        TunerSessionManager manager = Manager();
        TunerSession held = Begin(manager, "still-going", "adapter0");

        manager.EnterDraining();

        Assert.False(manager.TryEnterDrainingUnlessRecording(out IReadOnlyList<TunerSession>? recordings));
        Assert.Equal([held.SessionId], recordings.Select(session => session.SessionId));

        StopAndWait(held);
    }

    [Fact]
    public void TheBudgetForAStopWithNothingToWaitForIsTheHardStopAlone()
    {
        TunerSessionManager manager = Manager();

        Assert.Equal(TunerSessionManager.DefaultHardStopLimit, manager.HardStopBudget);
        Assert.True(manager.ShutdownBudget > manager.HardStopBudget);
    }

    private static StartSessionRequest TypedSatellite(string sessionId, string? deviceId = null)
    {
        var tune = TuneParams.Bs(15, 50001);

        return new StartSessionRequest
        {
            SessionId = SessionId.Parse(sessionId),
            Purpose = SessionPurpose.Scan,
            Tuning = tune.ToLegacyRequest(),
            Tune = tune,
            DeviceId = deviceId,
        };
    }
}
