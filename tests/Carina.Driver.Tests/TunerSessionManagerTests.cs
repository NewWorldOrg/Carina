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
            root,
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
        SessionPurpose purpose = SessionPurpose.Recording,
        TunerKind kind = TunerKind.Terrestrial
    ) =>
        new()
        {
            Purpose = purpose,
            Tuning = new TuningRequest(kind, 27, 1024),
            EndsAt = Start.AddHours(1),
        };

    private TunerSession Begin(
        TunerSessionManager manager,
        string sessionId,
        string deviceId,
        SessionPurpose purpose = SessionPurpose.Recording,
        TunerKind kind = TunerKind.Terrestrial
    ) =>
        manager.Begin(
            SessionId.Parse(sessionId),
            Request(purpose, kind),
            deviceId,
            Start.AddHours(1)
        );

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
    public void ARecordingSessionWritesUnderTheConfiguredDirectory()
    {
        var manager = Manager();

        var session = StopAndWait(WaitForBytes(Begin(manager, "s-1", "adapter0")));

        Assert.True(session.BytesRecorded > 0);
        Assert.Equal(new FileInfo(Path.Combine(root, "s-1.ts")).Length, session.BytesRecorded);
    }

    [Fact]
    public void ALiveSessionWritesNoFile()
    {
        var manager = Manager();

        StopAndWait(Begin(manager, "s-2", "adapter0", SessionPurpose.Live));

        Assert.False(File.Exists(Path.Combine(root, "s-2.ts")));
    }

    [Fact]
    public void AnUnknownDeviceIsRefused()
    {
        var manager = Manager();

        Assert.Throws<ArgumentException>(() => Begin(manager, "s-1", "adapter9"));
    }

    [Fact]
    public void ADisabledDeviceIsRefused()
    {
        var manager = Manager();

        Assert.Throws<ArgumentException>(() => Begin(manager, "s-1", "adapter2"));
    }

    [Fact]
    public void ADeviceThatServesTheOtherSideIsRefused()
    {
        var manager = Manager();

        Assert.Throws<ArgumentException>(
            () => Begin(manager, "s-1", "adapter0", kind: TunerKind.Satellite)
        );
    }

    [Fact]
    public void ADeviceAlreadyServingASessionIsNotHandedOutTwice()
    {
        var manager = Manager();
        var first = Begin(manager, "s-1", "adapter0");

        Assert.Throws<ArgumentException>(() => Begin(manager, "s-2", "adapter0"));

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
                try
                {
                    granted.Add(Begin(manager, $"s-{index}", "adapter0"));
                }
                catch (ArgumentException) { }
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

        Assert.Throws<IOException>(() => Begin(manager, "s-1", "adapter0"));

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
    public void AnEndTimeThatContradictsTheRequestIsRefused()
    {
        var manager = Manager();

        Assert.Throws<ArgumentException>(
            () => manager.Begin(SessionId.Parse("s-1"), Request(), "adapter0", Start.AddHours(3))
        );
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public void TheSameIdentifierIsNotUsedTwice()
    {
        var manager = Manager();
        var first = Begin(manager, "s-1", "adapter0");

        Assert.Throws<ArgumentException>(
            () => Begin(manager, "s-1", "adapter1", kind: TunerKind.Satellite)
        );

        StopAndWait(first);
    }

    [Fact]
    public void TheIdentifierOfAFinishedSessionIsNotReused()
    {
        var manager = Manager();

        StopAndWait(Begin(manager, "s-1", "adapter0"));

        Assert.Throws<ArgumentException>(() => Begin(manager, "s-1", "adapter3"));
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
    public async Task ShutdownStopsAViewerButWaitsForARecording()
    {
        var manager = Manager();
        var recording = Begin(manager, "s-1", "adapter0");
        var live = Begin(manager, "s-2", "adapter1", SessionPurpose.Live, TunerKind.Satellite);

        var shuttingDown = manager.StopAsync(CancellationToken.None);

        live.WaitForEnd(TimeSpan.FromSeconds(10));

        Assert.Equal(SessionState.Stopped, live.State);
        Assert.False(shuttingDown.IsCompleted);
        Assert.Equal(SessionState.Active, recording.State);

        clock.Advance(TimeSpan.FromHours(2));

        await shuttingDown;

        Assert.Equal(SessionState.Stopped, recording.State);
        Assert.Equal(SessionStopReason.EndTimeReached, recording.StopReason);
    }

    [Fact]
    public async Task ARecordingThatOutlastsTheGraceCapIsStoppedAndSaysWhy()
    {
        var manager = Manager(Configuration with { ShutdownGraceHours = 0 });
        var recording = Begin(manager, "s-1", "adapter0");

        await manager.StopAsync(CancellationToken.None);

        Assert.Equal(SessionState.Failed, recording.State);
        Assert.Equal(SessionStopReason.DrainCapReached, recording.StopReason);
        Assert.NotNull(recording.FailureCause);
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

        await manager.StopAsync(CancellationToken.None);

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        Assert.False(recording.Completion.IsCompleted);
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

        Assert.Throws<InvalidOperationException>(() => Begin(manager, "s-1", "adapter0"));
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

        Assert.Throws<NotSupportedException>(() => Begin(manager, "s-1", "adapter0"));
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public void ARecordingIsNeverAppendedToAnExistingFile()
    {
        var manager = Manager();

        StopAndWait(WaitForBytes(Begin(manager, "s-1", "adapter0")));

        File.Copy(Path.Combine(root, "s-1.ts"), Path.Combine(root, "s-5.ts"));

        Assert.Throws<IOException>(() => Begin(manager, "s-5", "adapter0"));
        Assert.Empty(manager.Sessions);
    }
}
