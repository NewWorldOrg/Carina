using Carina.Contracts;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Driver;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

using static Carina.Infrastructure.Tests.Recordings.RecordingStreamFixture;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingStreamJobTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task TheLoopWatchesOnceItsFirstWaitIsOverAndKeepsWatchingAfterThat()
    {
        var clock = new WatchClock(Airs);
        var ledger = new StreamLedger();
        var driver = new WatchedDriver();
        using RecordingStreamJob job = Job(
            ledger,
            driver,
            clock,
            new DriverSignalRelay(NullLogger<DriverSignalRelay>.Instance),
            new RecordingWatchSettings(
                TimeSpan.FromMinutes(3),
                TimeSpan.FromHours(2),
                5,
                TimeSpan.FromSeconds(2),
                3));
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => clock.Waits.Count >= 2, "the loop never waited twice");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal([TimeSpan.FromMinutes(3), TimeSpan.FromHours(2)], clock.Waits.Take(2).ToArray());
    }

    [Fact]
    public async Task AWatchThatThrewStillLetsTheNextOneStart()
    {
        var clock = new WatchClock(Airs);
        var ledger = new StreamLedger { RefusingToList = new InvalidOperationException("the ledger is gone") };
        using RecordingStreamJob job = Job(
            ledger,
            new WatchedDriver(),
            clock,
            new DriverSignalRelay(NullLogger<DriverSignalRelay>.Instance));
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => clock.Waits.Count >= 3, "the loop stopped at the first watch that threw");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.True(clock.Waits.Count >= 3);
    }

    [Fact]
    public async Task ProgressFromTheDriverWakesTheWatchBeforeItsWaitIsOver()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(recording, Airs);
        var signals = new DriverSignalRelay(NullLogger<DriverSignalRelay>.Instance);
        var clock = new StillClock(Airs.AddMinutes(10));
        using RecordingStreamJob job = Job(
            ledger,
            driver,
            clock,
            signals,
            new RecordingWatchSettings(
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(1),
                5,
                TimeSpan.FromSeconds(2),
                3));
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => clock.Armed >= 1, "the loop never began waiting");

        Assert.Empty(driver.Asked);

        signals.Publish(DriverEvents.RecordingProgress);

        await Eventually.Happens(() => ledger.Saved.Count >= 1, "the progress signal never woke the watch");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal(TimeSpan.FromMinutes(10), ledger.Read(recording.Id).Written);
    }

    [Fact]
    public async Task ASignalThatIsNotProgressLeavesTheWatchWaiting()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(recording, Airs);
        var signals = new DriverSignalRelay(NullLogger<DriverSignalRelay>.Instance);
        var clock = new StillClock(Airs.AddMinutes(10));
        using RecordingStreamJob job = Job(
            ledger,
            driver,
            clock,
            signals,
            new RecordingWatchSettings(
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(1),
                5,
                TimeSpan.FromSeconds(2),
                3));
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => clock.Armed >= 1, "the loop never began waiting");

        foreach (string name in DriverEvents.All.Where(name => !RecordingStreamJob.WakesOn(name)))
        {
            signals.Publish(name);
        }

        await Stayed(() => driver.Asked.Count is 0, "a signal that is not recording progress woke the watch");

        signals.Publish(DriverEvents.RecordingProgress);

        await Eventually.Happens(() => driver.Asked.Count >= 1, "the progress signal never woke the watch");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);
    }

    [Fact]
    public void TheOnlySignalTheWatchWakesOnIsTheOneTheDriverSendsWhileRecording()
    {
        Assert.True(RecordingStreamJob.WakesOn(DriverEvents.RecordingProgress));
        Assert.All(
            DriverEvents.All.Where(name => name != DriverEvents.RecordingProgress),
            name => Assert.False(RecordingStreamJob.WakesOn(name)));
        Assert.False(RecordingStreamJob.WakesOn("recordingprogress"));
    }

    [Fact]
    public async Task ALoopAskedToStopWhileItIsWaitingStopsRatherThanTakingOneMoreTurn()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(recording, Airs);
        var clock = new StillClock(Airs.AddMinutes(10));
        using RecordingStreamJob job = Job(
            ledger,
            driver,
            clock,
            new DriverSignalRelay(NullLogger<DriverSignalRelay>.Instance));
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => clock.Armed >= 1, "the loop never began waiting");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal(0, ledger.Listings);
        Assert.Empty(driver.Asked);
        Assert.Empty(ledger.Saved);
    }

    private static async Task Stayed(Func<bool> condition, string what)
    {
        long start = Environment.TickCount64;

        while (Environment.TickCount64 - start < 1000)
        {
            if (!condition())
            {
                throw new InvalidOperationException(what);
            }

            await Task.Delay(10, Cancel);
        }
    }

    private static RecordingStreamJob Job(
        StreamLedger ledger,
        WatchedDriver driver,
        TimeProvider clock,
        DriverSignalRelay signals,
        RecordingWatchSettings? settings = null)
        => new(
            Supervisor(ledger, driver, clock, settings: settings ?? Settings),
            signals,
            settings ?? Settings,
            clock,
            NullLogger<RecordingStreamJob>.Instance);
}
