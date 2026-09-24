using System.Collections.Concurrent;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Events;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class FaultedTunerRecheckTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    private readonly SteppedTimeProvider clock = new(Start);
    private readonly ScriptedTunerDeviceCheck check = new();
    private readonly DriverEventHub hub = new();

    private static DriverConfiguration Configuration =>
        new(
            "/run/carina/driver.sock",
            [],
            6,
            new TunerSettings(TunerBackend.Fake),
            [
                new DeviceSettings("adapter0", DeviceKind.Terrestrial),
                new DeviceSettings("adapter3", DeviceKind.Terrestrial),
            ]
        );

    private TunerSessionManager Manager() =>
        new(
            Configuration,
            new SelectiveTunerDeviceFactory("adapter0"),
            clock,
            NullLogger<TunerSessionManager>.Instance,
            events: hub,
            deviceCheck: check
        );

    private static StartSessionRequest Watching(string sessionId) =>
        new()
        {
            SessionId = SessionId.Parse(sessionId),
            Purpose = SessionPurpose.Live,
            Tuning = new TuningRequest(TunerKind.Terrestrial, 55, 50001),
            DeviceId = "adapter0",
        };

    private static void FailOn(TunerSessionManager manager, string sessionId)
    {
        SessionStart start = manager.Begin(Watching(sessionId));

        Assert.Equal(SessionRefusal.None, start.Refusal);
        Assert.True(start.TryGetSession(out TunerSession? session));

        session.WaitForEnd(TimeSpan.FromSeconds(10));

        Assert.Equal(SessionStopReason.DeviceFailed, session.StopReason);
        Assert.True(manager.IsFaulted("adapter0", out _));
    }

    private void Wait(TimeSpan span, int triedBefore, TunerSessionManager manager)
    {
        clock.Advance(span - OneSecond);

        Assert.Equal(triedBefore, check.Tried.Count);
        Assert.True(manager.IsFaulted("adapter0", out _));

        clock.Advance(OneSecond);

        Assert.Equal(triedBefore + 1, check.Tried.Count);
    }

    [Fact]
    public void AFailedTunerThatOpensWhenTriedAMinuteLaterIsHandedOutAgain()
    {
        TunerSessionManager manager = Manager();

        FailOn(manager, "s-1");
        Wait(TimeSpan.FromMinutes(1), 0, manager);

        Assert.Equal(["adapter0"], check.Tried);
        Assert.False(manager.IsFaulted("adapter0", out _));
        Assert.Equal(clock.GetUtcNow(), manager.HealthChangedAt("adapter0"));
    }

    [Fact]
    public void ATunerThatStillWillNotOpenIsTriedAfterFiveAndFifteenMinutesAndThenEveryHour()
    {
        TunerSessionManager manager = Manager();
        check.RefuseTheNext(5);

        FailOn(manager, "s-1");

        TimeSpan[] waits =
        [
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(60),
            TimeSpan.FromMinutes(60),
        ];

        for (int tried = 0; tried < waits.Length; tried++)
        {
            Wait(waits[tried], tried, manager);
            Assert.True(manager.IsFaulted("adapter0", out _));
        }

        Wait(TimeSpan.FromMinutes(60), waits.Length, manager);

        Assert.False(manager.IsFaulted("adapter0", out _));
    }

    [Fact]
    public void ATunerThatFailsAgainWithinHalfAnHourOfComingBackIsNotTriedAgain()
    {
        TunerSessionManager manager = Manager();

        FailOn(manager, "s-1");
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(manager.IsFaulted("adapter0", out _));

        clock.Advance(TimeSpan.FromMinutes(30));
        FailOn(manager, "s-2");

        foreach (int minutes in new[] { 1, 5, 15, 60, 60 })
        {
            clock.Advance(TimeSpan.FromMinutes(minutes));
        }

        Assert.Single(check.Tried);
        Assert.True(manager.IsFaulted("adapter0", out _));

        SessionStart refused = manager.Begin(Watching("s-3"));

        Assert.Equal(SessionRefusal.FaultedDevice, refused.Refusal);
        Assert.Contains("until the driver restarts", refused.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ATunerThatFailsAgainMoreThanHalfAnHourAfterComingBackIsTriedAgain()
    {
        TunerSessionManager manager = Manager();

        FailOn(manager, "s-1");
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(manager.IsFaulted("adapter0", out _));

        clock.Advance(TimeSpan.FromMinutes(30) + OneSecond);
        FailOn(manager, "s-2");
        Wait(TimeSpan.FromMinutes(1), 1, manager);

        Assert.False(manager.IsFaulted("adapter0", out _));
    }

    [Fact]
    public void ATunerFaultedForDisagreeingWithTheLedgerIsNeverTriedAgain()
    {
        TunerSessionManager manager = Manager();

        manager.Fault("adapter0", "the delivery systems it reports are not the ones recorded");

        foreach (int minutes in new[] { 1, 5, 15, 60, 60 })
        {
            clock.Advance(TimeSpan.FromMinutes(minutes));
        }

        Assert.Empty(check.Tried);
        Assert.True(manager.IsFaulted("adapter0", out _));
    }

    [Fact]
    public void ATunerTheLedgerFaultsWhileItWaitsToBeTriedIsNoLongerTried()
    {
        TunerSessionManager manager = Manager();

        FailOn(manager, "s-1");
        manager.Fault("adapter0", "the delivery systems it reports are not the ones recorded");
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Empty(check.Tried);
        Assert.True(manager.IsFaulted("adapter0", out string? detail));
        Assert.Contains("not the ones recorded", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BringingATunerBackAnnouncesThatItsHealthChanged()
    {
        TunerSessionManager manager = Manager();

        FailOn(manager, "s-1");

        Assert.True(hub.TryListen(out DriverEventListener? listener));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        clock.Advance(TimeSpan.FromMinutes(1));

        var signalled = new List<string>();
        while (
            !signalled.Contains(DriverEvents.TunerHealthChanged, StringComparer.Ordinal)
            || !signalled.Contains(DriverEvents.Tuners, StringComparer.Ordinal)
        )
        {
            signalled.AddRange(await listener.Take(deadline.Token));
        }

        listener.Dispose();
    }

    [Fact]
    public void WhileATunerWaitsToBeTriedTheRefusalSaysWhenRatherThanARestart()
    {
        TunerSessionManager manager = Manager();

        FailOn(manager, "s-1");
        DateTimeOffset tried = clock.GetUtcNow() + TimeSpan.FromMinutes(1);

        SessionStart refused = manager.Begin(Watching("s-2"));

        Assert.Equal(SessionRefusal.FaultedDevice, refused.Refusal);
        Assert.Contains(tried.ToString("O"), refused.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("restart", refused.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADriverThatIsStoppingTriesNothingAgain()
    {
        TunerSessionManager manager = Manager();

        FailOn(manager, "s-1");
        await manager.StopAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Empty(check.Tried);
        Assert.Equal(0, clock.Waiting);
    }

    [Fact]
    public void WithNothingToCheckADeviceWithAFailedTunerStaysFaultedUntilTheDriverRestarts()
    {
        var manager = new TunerSessionManager(
            Configuration,
            new SelectiveTunerDeviceFactory("adapter0"),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

        FailOn(manager, "s-1");
        clock.Advance(TimeSpan.FromMinutes(1));

        SessionStart refused = manager.Begin(Watching("s-2"));

        Assert.Equal(SessionRefusal.FaultedDevice, refused.Refusal);
        Assert.Contains("until the driver restarts", refused.Detail, StringComparison.Ordinal);
    }

    private sealed class ScriptedTunerDeviceCheck : ITunerDeviceCheck
    {
        private readonly ConcurrentQueue<string> tried = new();

        private int refusals;

        public IReadOnlyList<string> Tried => [.. tried];

        public void RefuseTheNext(int count) => refusals = count;

        public void Check(DeviceSettings device)
        {
            tried.Enqueue(device.Id!);

            if (Interlocked.Decrement(ref refusals) >= 0)
            {
                throw new IOException("the transport stream reader would not open");
            }
        }
    }
}
