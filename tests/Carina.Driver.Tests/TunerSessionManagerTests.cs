using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

namespace Carina.Driver.Tests;

public sealed class TunerSessionManagerTests : IDisposable
{
    private static readonly DateTimeOffset Start =
        new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private readonly string root = Directory
        .CreateTempSubdirectory("carina-manager-")
        .FullName;

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
            ]
        );

    private TunerSessionManager Manager() =>
        new(Configuration, new TunerDeviceFactory(Configuration), clock);

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

        var session = manager.Begin(
            SessionId.Parse("s-1"),
            Request(),
            "adapter0",
            Start.AddHours(1)
        );

        Assert.Equal(SessionState.Active, session.State);
        Assert.True(manager.TryGet(SessionId.Parse("s-1"), out _));

        StopAndWait(session);
    }

    [Fact]
    public void ARecordingSessionWritesUnderTheConfiguredDirectory()
    {
        var manager = Manager();

        var session = StopAndWait(
            manager.Begin(SessionId.Parse("s-1"), Request(), "adapter0", Start.AddHours(1))
        );

        Assert.True(File.Exists(Path.Combine(root, "s-1.ts")));
        Assert.True(session.BytesRecorded > 0);
    }

    [Fact]
    public void ALiveSessionWritesNoFile()
    {
        var manager = Manager();

        StopAndWait(
            manager.Begin(
                SessionId.Parse("s-2"),
                Request(SessionPurpose.Live),
                "adapter0",
                Start.AddHours(1)
            )
        );

        Assert.False(File.Exists(Path.Combine(root, "s-2.ts")));
    }

    [Fact]
    public void AnUnknownDeviceIsRefused()
    {
        var manager = Manager();

        Assert.Throws<ArgumentException>(
            () =>
                manager.Begin(
                    SessionId.Parse("s-1"),
                    Request(),
                    "adapter9",
                    Start.AddHours(1)
                )
        );
    }

    [Fact]
    public void ADisabledDeviceIsRefused()
    {
        var manager = Manager();

        Assert.Throws<ArgumentException>(
            () =>
                manager.Begin(
                    SessionId.Parse("s-1"),
                    Request(),
                    "adapter2",
                    Start.AddHours(1)
                )
        );
    }

    [Fact]
    public void ADeviceThatServesTheOtherSideIsRefused()
    {
        var manager = Manager();

        Assert.Throws<ArgumentException>(
            () =>
                manager.Begin(
                    SessionId.Parse("s-1"),
                    Request(kind: TunerKind.Satellite),
                    "adapter0",
                    Start.AddHours(1)
                )
        );
    }

    [Fact]
    public void TheSameIdentifierIsNotUsedTwice()
    {
        var manager = Manager();
        var first = manager.Begin(
            SessionId.Parse("s-1"),
            Request(),
            "adapter0",
            Start.AddHours(1)
        );

        Assert.Throws<ArgumentException>(
            () =>
                manager.Begin(
                    SessionId.Parse("s-1"),
                    Request(),
                    "adapter1",
                    Start.AddHours(1)
                )
        );

        StopAndWait(first);
    }

    [Fact]
    public void AnEndedSessionLeavesTheActiveSet()
    {
        var manager = Manager();
        var session = manager.Begin(
            SessionId.Parse("s-1"),
            Request(),
            "adapter0",
            Start.AddHours(1)
        );

        StopAndWait(session);

        Assert.False(manager.TryGet(SessionId.Parse("s-1"), out _));
        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public async Task StoppingTheHostAsksEverySessionToStopDeliberately()
    {
        var manager = Manager();
        var first = manager.Begin(
            SessionId.Parse("s-1"),
            Request(),
            "adapter0",
            Start.AddHours(1)
        );
        var second = manager.Begin(
            SessionId.Parse("s-2"),
            Request(kind: TunerKind.Satellite),
            "adapter1",
            Start.AddHours(1)
        );

        await manager.StopAsync(CancellationToken.None);

        first.WaitForEnd(TimeSpan.FromSeconds(10));
        second.WaitForEnd(TimeSpan.FromSeconds(10));

        Assert.Equal(SessionState.Stopped, first.State);
        Assert.Equal(SessionState.Stopped, second.State);
    }

    [Fact]
    public void OneDeviceFailingLeavesTheOtherSessionAlone()
    {
        var manager = Manager();
        var healthy = manager.Begin(
            SessionId.Parse("s-1"),
            Request(),
            "adapter0",
            Start.AddHours(1)
        );

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
        var configuration = Configuration with { Tuner = new TunerSettings(TunerBackend.Dvb) };
        var manager = new TunerSessionManager(
            configuration,
            new TunerDeviceFactory(configuration),
            clock
        );

        Assert.Throws<NotSupportedException>(
            () =>
                manager.Begin(
                    SessionId.Parse("s-1"),
                    Request(),
                    "adapter0",
                    Start.AddHours(1)
                )
        );
        Assert.Empty(manager.Sessions);
    }
}
