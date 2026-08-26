using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Events;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class RecordingProgressSignalTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly string root = Directory.CreateTempSubdirectory("carina-progress-").FullName;
    private readonly SteppedTimeProvider clock = new(Start);
    private readonly DriverEventHub hub = new();

    public void Dispose() => Directory.Delete(root, recursive: true);

    private DriverConfiguration Configuration =>
        new(
            "/run/carina/driver.sock",
            [new OutputRootSettings("primary", root)],
            6,
            new TunerSettings(TunerBackend.Fake),
            [new DeviceSettings("adapter0", DeviceKind.Terrestrial)]
        );

    private TunerSessionManager Manager()
    {
        DriverConfiguration configuration = Configuration;

        return new TunerSessionManager(
            configuration,
            new TunerDeviceFactory(configuration, TimeProvider.System),
            clock,
            NullLogger<TunerSessionManager>.Instance,
            events: hub,
            progressInterval: Interval
        );
    }

    private TunerSession Begin(TunerSessionManager manager, SessionPurpose purpose)
    {
        SessionStart start = manager.Begin(
            new StartSessionRequest
            {
                SessionId = SessionId.Parse("rec-1"),
                Purpose = purpose,
                Tuning = new TuningRequest(TunerKind.Terrestrial, 55, 50001),
                DeviceId = "adapter0",
                OutputRoot = purpose is SessionPurpose.Recording ? "primary" : null,
                EndsAt = Start.AddHours(1),
                RecordingId = purpose is SessionPurpose.Recording ? "k-90210" : null,
            }
        );

        Assert.Equal(SessionRefusal.None, start.Refusal);
        Assert.True(start.TryGetSession(out TunerSession? session));

        return session;
    }

    [Fact]
    public async Task ARecordingInFlightIsSpokenForBeforeItEnds()
    {
        TunerSessionManager manager = Manager();

        await manager.StartAsync(CancellationToken.None);

        Assert.True(hub.TryListen(out DriverEventListener? listener));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        TunerSession session = Begin(manager, SessionPurpose.Recording);
        clock.Advance(Interval);

        var signalled = new List<string>();
        while (!signalled.Contains(DriverEvents.RecordingProgress, StringComparer.Ordinal))
        {
            signalled.AddRange(await listener.Take(deadline.Token));
        }

        listener.Dispose();
        session.Stop();
        session.Dispose();
    }

    [Fact]
    public async Task ADriverWatchingWithoutRecordingSaysNothingAboutProgress()
    {
        TunerSessionManager manager = Manager();

        await manager.StartAsync(CancellationToken.None);

        TunerSession session = Begin(manager, SessionPurpose.Live);

        Assert.True(hub.TryListen(out DriverEventListener? listener));

        clock.Advance(Interval);
        clock.Advance(Interval);
        clock.Advance(Interval);

        using var quiet = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var signalled = new List<string>();

        try
        {
            signalled.AddRange(await listener.Take(quiet.Token));
        }
        catch (OperationCanceledException)
        {
            signalled.Clear();
        }

        Assert.DoesNotContain(DriverEvents.RecordingProgress, signalled);

        listener.Dispose();
        session.Stop();
        session.Dispose();
    }

    [Fact]
    public async Task TheClockIsLetGoOfWhenTheDriverStops()
    {
        TunerSessionManager manager = Manager();

        await manager.StartAsync(CancellationToken.None);

        Assert.Equal(1, clock.Waiting);

        await manager.StopAsync(CancellationToken.None);

        Assert.Equal(0, clock.Waiting);
    }
}
