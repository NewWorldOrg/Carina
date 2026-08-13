using System.Collections.Concurrent;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class TunerSessionManagerTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

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
        new(
            configuration,
            new TunerDeviceFactory(configuration),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

    private static StartSessionRequest Request(
        string sessionId,
        string? deviceId = null,
        SessionPurpose purpose = SessionPurpose.Recording,
        TunerKind kind = TunerKind.Terrestrial,
        string? outputRoot = "primary",
        DateTimeOffset? endsAt = null
    ) =>
        new()
        {
            SessionId = SessionId.Parse(sessionId),
            Purpose = purpose,
            Tuning = new TuningRequest(kind, 27, 1024),
            DeviceId = deviceId,
            OutputRoot = purpose is SessionPurpose.Recording ? outputRoot : null,
            EndsAt = endsAt ?? (purpose is SessionPurpose.Recording ? Start.AddHours(1) : null),
        };

    private static TunerSession Begin(
        TunerSessionManager manager,
        string sessionId,
        string? deviceId = null,
        SessionPurpose purpose = SessionPurpose.Recording,
        TunerKind kind = TunerKind.Terrestrial
    )
    {
        var start = manager.Begin(Request(sessionId, deviceId, purpose, kind));

        Assert.Equal(SessionRefusal.None, start.Refusal);
        Assert.True(start.TryGetSession(out var session));

        return session;
    }

    private static SessionRefusal RefusalFor(
        TunerSessionManager manager,
        StartSessionRequest request
    )
    {
        var start = manager.Begin(request);

        Assert.False(start.TryGetSession(out _));
        Assert.NotEmpty(start.Detail);

        return start.Refusal;
    }

    private static TunerSession WaitForBytes(TunerSession session)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

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
        var manager = Manager();

        var session = Begin(manager, "s-1", "adapter0");

        Assert.Equal(SessionState.Active, session.State);
        Assert.True(manager.TryGet(SessionId.Parse("s-1"), out _));

        StopAndWait(session);
    }

    [Fact]
    public void ARecordingSessionWritesUnderTheNamedOutputRoot()
    {
        var manager = Manager();

        var session = StopAndWait(WaitForBytes(Begin(manager, "s-1", "adapter0")));

        Assert.True(session.BytesRecorded > 0);
        Assert.Equal("primary", session.OutputRoot);
        Assert.Equal(new FileInfo(Path.Combine(root, "s-1.ts")).Length, session.BytesRecorded);
    }

    [Fact]
    public void ARootThisDriverNeverDeclaredIsRefused()
    {
        var manager = Manager();

        Assert.Equal(
            SessionRefusal.UnknownOutputRoot,
            RefusalFor(manager, Request("s-1", "adapter0", outputRoot: "elsewhere"))
        );
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public void APathInPlaceOfARootNameIsRefusedBeforeAnythingIsOpened()
    {
        var manager = Manager();

        Assert.Equal(
            SessionRefusal.Rejected,
            RefusalFor(manager, Request("s-1", "adapter0", outputRoot: root))
        );
        Assert.Empty(Directory.GetFiles(root));
    }

    [Fact]
    public void ALiveSessionWritesNoFile()
    {
        var manager = Manager();

        StopAndWait(Begin(manager, "s-2", "adapter0", SessionPurpose.Live));

        Assert.False(File.Exists(Path.Combine(root, "s-2.ts")));
    }

    [Fact]
    public void ALiveSessionThatNamesNoEndTimeGetsTheConfiguredOne()
    {
        var manager = Manager(Configuration with { LiveSessionMinutes = 45 });

        var session = Begin(manager, "s-1", "adapter0", SessionPurpose.Live);

        Assert.Equal(Start.AddMinutes(45), session.EndsAt);

        StopAndWait(session);
    }

    [Fact]
    public void ARequestWithoutADeviceIsGivenAFreeOneOfTheRightKind()
    {
        var manager = Manager();

        var session = Begin(manager, "s-1", purpose: SessionPurpose.Live);

        Assert.Equal("adapter0", session.DeviceId);

        var second = Begin(manager, "s-2", purpose: SessionPurpose.Live);

        Assert.Equal("adapter3", second.DeviceId);

        StopAndWait(session);
        StopAndWait(second);
    }

    [Fact]
    public void ARequestWithoutADeviceNeverGetsADisabledOrMismatchedOne()
    {
        var manager = Manager();

        var first = Begin(manager, "s-1", purpose: SessionPurpose.Live);
        var second = Begin(manager, "s-2", purpose: SessionPurpose.Live);

        Assert.Equal(
            SessionRefusal.NoDeviceFree,
            RefusalFor(manager, Request("s-3", purpose: SessionPurpose.Live))
        );

        StopAndWait(first);
        StopAndWait(second);
    }

    [Fact]
    public void ARequestForAKindThisDriverDoesNotServeSaysSo()
    {
        var manager = Manager(
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
        var manager = Manager();

        Assert.Equal(
            SessionRefusal.UnknownDevice,
            RefusalFor(manager, Request("s-1", "adapter9"))
        );
    }

    [Fact]
    public void ADisabledDeviceIsRefused()
    {
        var manager = Manager();

        Assert.Equal(
            SessionRefusal.DisabledDevice,
            RefusalFor(manager, Request("s-1", "adapter2"))
        );
    }

    [Fact]
    public void ADeviceThatServesTheOtherSideIsRefused()
    {
        var manager = Manager();

        Assert.Equal(
            SessionRefusal.WrongDeviceKind,
            RefusalFor(manager, Request("s-1", "adapter0", kind: TunerKind.Satellite))
        );
    }

    [Fact]
    public void ADeviceAlreadyServingASessionIsNotHandedOutTwice()
    {
        var manager = Manager();
        var first = Begin(manager, "s-1", "adapter0");

        Assert.Equal(
            SessionRefusal.DeviceBusy,
            RefusalFor(manager, Request("s-2", "adapter0"))
        );

        StopAndWait(first);
    }

    [Fact]
    public void OnlyOneOfManySimultaneousRequestsGetsTheDevice()
    {
        var manager = Manager();
        var granted = new ConcurrentBag<TunerSession>();

        Parallel.For(
            0,
            16,
            index =>
            {
                if (manager.Begin(Request($"s-{index}", "adapter0")).TryGetSession(out var session))
                {
                    granted.Add(session);
                }
            }
        );

        Assert.Single(granted);
        Assert.Single(manager.Sessions);

        StopAndWait(granted.Single());
    }

    [Fact]
    public void ARefusedRequestLeavesTheDeviceFreeForTheNext()
    {
        var manager = Manager();

        File.WriteAllBytes(Path.Combine(root, "s-1.ts"), [0x47]);

        Assert.Equal(
            SessionRefusal.RecordingAlreadyExists,
            RefusalFor(manager, Request("s-1", "adapter0"))
        );

        StopAndWait(Begin(manager, "s-2", "adapter0"));
    }

    [Fact]
    public void ADeviceIsFreeAgainOnceItsSessionEnds()
    {
        var manager = Manager();

        StopAndWait(Begin(manager, "s-1", "adapter0"));

        StopAndWait(Begin(manager, "s-2", "adapter0"));
    }

    [Fact]
    public void AnEndTimeAlreadyBehindTheDriverIsRefused()
    {
        var manager = Manager();

        Assert.Equal(
            SessionRefusal.Rejected,
            RefusalFor(manager, Request("s-1", "adapter0", endsAt: Start.AddHours(-1)))
        );
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public void TheSameIdentifierIsNotUsedTwice()
    {
        var manager = Manager();
        var first = Begin(manager, "s-1", "adapter0");

        Assert.Equal(
            SessionRefusal.DuplicateSession,
            RefusalFor(manager, Request("s-1", "adapter1", kind: TunerKind.Satellite))
        );

        StopAndWait(first);
    }

    [Fact]
    public void TheIdentifierOfAFinishedSessionIsNotReused()
    {
        var manager = Manager();

        StopAndWait(Begin(manager, "s-1", "adapter0"));

        Assert.Equal(
            SessionRefusal.DuplicateSession,
            RefusalFor(manager, Request("s-1", "adapter3"))
        );
    }

    [Fact]
    public void AnEndedSessionLeavesTheActiveSetButStaysAvailable()
    {
        var manager = Manager();

        var session = StopAndWait(Begin(manager, "s-1", "adapter0"));

        Assert.Empty(manager.Sessions);
        Assert.True(manager.TryGet(SessionId.Parse("s-1"), out var found));
        Assert.Same(session, found);
        Assert.Equal(SessionStopReason.Requested, found.StopReason);
    }

    [Fact]
    public void StoppingTellsApartASessionThatIsGoneFromOneThatNeverWas()
    {
        var manager = Manager();
        var session = Begin(manager, "s-1", "adapter0");

        Assert.Equal(SessionStopOutcome.Stopping, manager.Stop(SessionId.Parse("s-1")));

        session.WaitForEnd(TimeSpan.FromSeconds(10));

        Assert.Equal(SessionStopOutcome.AlreadyEnded, manager.Stop(SessionId.Parse("s-1")));
        Assert.Equal(SessionStopOutcome.NoSuchSession, manager.Stop(SessionId.Parse("s-9")));
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

        var session = Begin(manager, "s-1", "adapter0", SessionPurpose.Live);

        session.WaitForEnd(TimeSpan.FromSeconds(10));

        Assert.Empty(manager.Sessions);
        Assert.True(manager.TryGet(SessionId.Parse("s-1"), out var found));
        Assert.Equal(SessionState.Failed, found.State);
        Assert.IsType<IOException>(found.FailureCause);
    }

    [Fact]
    public async Task TheDrainStopsAViewerButWaitsForARecordingWithItsStreamsAttached()
    {
        var manager = Manager();
        var recording = Begin(manager, "s-1", "adapter0");
        var live = Begin(manager, "s-2", "adapter1", SessionPurpose.Live, TunerKind.Satellite);

        var draining = manager.DrainAsync(CancellationToken.None);

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
        var manager = Manager(Configuration with { ShutdownGraceHours = 0 });
        var recording = Begin(manager, "s-1", "adapter0");

        await manager.DrainAsync(CancellationToken.None);

        Assert.Equal(SessionState.Failed, recording.State);
        Assert.Equal(SessionStopReason.DrainCapReached, recording.StopReason);
        Assert.NotNull(recording.FailureCause);
        Assert.False(manager.IsFaulted("adapter0", out _));
    }

    [Fact]
    public async Task TheDrainRunsOnceAndStopAsyncJoinsIt()
    {
        var manager = Manager(Configuration with { ShutdownGraceHours = 0 });
        var recording = Begin(manager, "s-1", "adapter0");

        var draining = manager.DrainAsync(CancellationToken.None);

        Assert.Same(draining, manager.DrainAsync(CancellationToken.None));
        Assert.Same(draining, manager.StopAsync(CancellationToken.None));

        await draining;

        Assert.Equal(SessionStopReason.DrainCapReached, recording.StopReason);
    }

    [Fact]
    public async Task ShutdownGivesUpOnASessionThatWillNotLetGo()
    {
        var manager = new TunerSessionManager(
            Configuration with { ShutdownGraceHours = 0 },
            new StubbornTunerDeviceFactory(TimeSpan.FromSeconds(20)),
            clock,
            NullLogger<TunerSessionManager>.Instance,
            hardStopLimit: TimeSpan.FromSeconds(1)
        );

        var recording = Begin(manager, "s-1", "adapter0");
        var started = DateTime.UtcNow;

        await manager.DrainAsync(CancellationToken.None);

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        Assert.False(recording.Completion.IsCompleted);
        Assert.False(recording.Concluded);
    }

    [Fact]
    public async Task ShutdownDoesNotReturnBeforeEverySessionOutcomeIsWrittenDown()
    {
        var log = new CapturingLogger<TunerSessionManager>();
        var manager = new TunerSessionManager(
            Configuration,
            new TunerDeviceFactory(Configuration),
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
        var manager = Manager();

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

        var doomed = Begin(manager, "s-1", "adapter0", SessionPurpose.Live);

        doomed.WaitForEnd(TimeSpan.FromSeconds(10));

        Assert.Equal(SessionState.Failed, doomed.State);
        Assert.True(manager.IsFaulted("adapter0", out var detail));
        Assert.Contains("s-1", detail, StringComparison.Ordinal);

        Assert.Equal(
            SessionRefusal.FaultedDevice,
            RefusalFor(manager, Request("s-2", "adapter0", purpose: SessionPurpose.Live))
        );

        var rerouted = Begin(manager, "s-3", purpose: SessionPurpose.Live);

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
        var manager = Manager();

        StopAndWait(Begin(manager, "s-1", "adapter0"));

        Assert.False(manager.IsFaulted("adapter0", out _));
    }

    [Fact]
    public void ARecordingWriteFailureDoesNotFaultTheDevice()
    {
        var manager = new TunerSessionManager(
            Configuration,
            new TunerDeviceFactory(Configuration),
            clock,
            NullLogger<TunerSessionManager>.Instance,
            recordingWriters: new BrittleRecordingWriterFactory()
        );

        var starved = Begin(manager, "s-1", "adapter0");

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
        Assert.True(manager.ClearFault("adapter0"));
        Assert.False(manager.ClearFault("adapter0"));
        Assert.False(manager.IsFaulted("adapter0", out _));

        Begin(manager, "s-2", "adapter0", SessionPurpose.Live).WaitForEnd(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void OneDeviceFailingLeavesTheOtherSessionAlone()
    {
        var manager = Manager();
        var healthy = Begin(manager, "s-1", "adapter0");

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
        var manager = Manager(Configuration with { Tuner = new TunerSettings(TunerBackend.Dvb) });

        Assert.Equal(
            SessionRefusal.DeviceUnavailable,
            RefusalFor(manager, Request("s-1", "adapter0"))
        );
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public void ARecordingIsNeverAppendedToAnExistingFile()
    {
        var manager = Manager();

        StopAndWait(WaitForBytes(Begin(manager, "s-1", "adapter0")));

        File.Copy(Path.Combine(root, "s-1.ts"), Path.Combine(root, "s-5.ts"));

        Assert.Equal(
            SessionRefusal.RecordingAlreadyExists,
            RefusalFor(manager, Request("s-5", "adapter0"))
        );
        Assert.Empty(manager.Sessions);
    }
}
