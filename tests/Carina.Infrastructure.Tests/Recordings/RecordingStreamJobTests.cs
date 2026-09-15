using Carina.Contracts;
using Carina.Domain.Driver;
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

    [Fact]
    public async Task AWatchThatGaveARecordingItsOutcomeTellsTheScreensAndTheQualityLedgerMoved()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var events = new SilentEvents();
        using RecordingStreamJob job = Job(
            ledger,
            new WatchedDriver(),
            new WatchClock(Airs.AddMinutes(30)),
            new DriverSignalRelay(NullLogger<DriverSignalRelay>.Instance),
            events: events);
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => events.Signalled.Count >= 2, "the watch that ended one told the screens nothing");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Contains(AppEventName.Recordings, events.Signalled);
        Assert.Contains(AppEventName.Quality, events.Signalled);
    }

    [Fact]
    public async Task CountsThatMovedOnAPassTellTheScreensOnce()
    {
        Recording recording = InFlight(until: Airs.AddHours(2));
        (StreamLedger ledger, WatchedDriver driver) = Streaming(recording);
        var clock = new HandTurnedClock(Airs);
        var events = new SilentEvents();
        using RecordingStreamJob job = Job(ledger, driver, clock, Relay(), Paced, events, Pace(TimeSpan.FromSeconds(30)));
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Passes(clock, ledger, 1);
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal([AppEventName.Recordings], events.Signalled);
        Assert.Equal(TimeSpan.FromSeconds(10), ledger.Read(recording.Id).Written);
    }

    [Fact]
    public async Task CountsThatMoveAgainBeforeThePaceIsUpTellTheScreensNothingMore()
    {
        Recording recording = InFlight(until: Airs.AddHours(2));
        (StreamLedger ledger, WatchedDriver driver) = Streaming(recording);
        var clock = new HandTurnedClock(Airs);
        var events = new SilentEvents();
        using RecordingStreamJob job = Job(ledger, driver, clock, Relay(), Paced, events, Pace(TimeSpan.FromSeconds(30)));
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Passes(clock, ledger, 3);
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal([AppEventName.Recordings], events.Signalled);
        Assert.Equal(TimeSpan.FromSeconds(30), ledger.Read(recording.Id).Written);
    }

    [Fact]
    public async Task CountsThatMoveOnceThePaceIsUpTellTheScreensAgain()
    {
        Recording recording = InFlight(until: Airs.AddHours(2));
        (StreamLedger ledger, WatchedDriver driver) = Streaming(recording);
        var clock = new HandTurnedClock(Airs);
        var events = new SilentEvents();
        using RecordingStreamJob job = Job(ledger, driver, clock, Relay(), Paced, events, Pace(TimeSpan.FromSeconds(30)));
        using var stopping = new CancellationTokenSource();
        List<int> toldAfterEachPass = [];

        await job.StartAsync(stopping.Token);

        for (int pass = 0; pass < 4; pass++)
        {
            await Passes(clock, ledger, 1);
            toldAfterEachPass.Add(events.Signalled.Count);
        }

        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal([1, 1, 1, 2], toldAfterEachPass);
        Assert.All(events.Signalled, name => Assert.Equal(AppEventName.Recordings, name));
    }

    [Fact]
    public async Task APassOnWhichNothingMovedTellsTheScreensNothingEvenWhenThePaceWouldAllowIt()
    {
        Recording recording = InFlight(until: Airs.AddHours(2));
        (StreamLedger ledger, WatchedDriver driver) = Streaming(recording);
        var clock = new HandTurnedClock(Airs);
        var events = new SilentEvents();
        DriverSignalRelay signals = Relay();
        using RecordingStreamJob job = Job(ledger, driver, clock, signals, Paced, events, Pace(TimeSpan.FromTicks(1)));
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Passes(clock, ledger, 1);

        for (int wake = 0; wake < 2; wake++)
        {
            int listed = ledger.Listings;
            signals.Publish(DriverEvents.RecordingProgress);
            await Eventually.Happens(
                () => ledger.Listings > listed && clock.Pending is 1,
                "the progress signal never woke the watch for a pass at the same instant");
        }

        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal([AppEventName.Recordings], events.Signalled);
        Assert.Single(ledger.Saved);
    }

    [Fact]
    public async Task ARecordingLeftWithoutAStreamTellsTheScreensOnlyOnThePassItBroke()
    {
        Recording recording = InFlight(until: Airs.AddHours(2));
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var clock = new HandTurnedClock(Airs);
        var events = new SilentEvents();
        using RecordingStreamJob job = Job(
            ledger,
            new WatchedDriver(),
            clock,
            Relay(),
            Paced,
            events,
            Pace(TimeSpan.FromTicks(1)));
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Passes(clock, ledger, 3);
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal([AppEventName.Recordings], events.Signalled);
        Assert.True(Assert.Single(ledger.Read(recording.Id).Interruptions).IsOpen);
    }

    [Fact]
    public async Task ARecordingTheDriverWillNotSpeakAboutTellsTheScreensNothing()
    {
        Recording recording = InFlight(until: Airs.AddHours(2));
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver
        {
            WhenAsked = DriverCall<SessionSnapshot>.Refused(new DriverProblem("driverBusy", [])),
        };
        var clock = new HandTurnedClock(Airs);
        var events = new SilentEvents();
        using RecordingStreamJob job = Job(ledger, driver, clock, Relay(), Paced, events, Pace(TimeSpan.FromTicks(1)));
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Passes(clock, ledger, 3);
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Empty(events.Signalled);
        Assert.Empty(ledger.Saved);
    }

    [Fact]
    public async Task AnOutcomeIsToldAtOnceWhileThePaceIsStillHoldingTheCounts()
    {
        Recording recording = InFlight(until: Airs.AddSeconds(15));
        (StreamLedger ledger, WatchedDriver driver) = Streaming(recording);
        var clock = new HandTurnedClock(Airs);
        var events = new SilentEvents();
        using RecordingStreamJob job = Job(ledger, driver, clock, Relay(), Paced, events, Pace(TimeSpan.FromHours(1)));
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Passes(clock, ledger, 1);

        driver.Holding[RecordingSessions.Named(recording.Id)] = Over(recording);

        await Passes(clock, ledger, 1);
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal([AppEventName.Recordings, AppEventName.Recordings, AppEventName.Quality], events.Signalled);
        Assert.NotNull(ledger.Read(recording.Id).Outcome);
    }

    private static readonly RecordingWatchSettings Paced = new(
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(10),
        1,
        TimeSpan.FromSeconds(2),
        3);

    private static RecordingProgressSettings Pace(TimeSpan atMostEvery) => new(atMostEvery);

    private static DriverSignalRelay Relay() => new(NullLogger<DriverSignalRelay>.Instance);

    private static (StreamLedger Ledger, WatchedDriver Driver) Streaming(Recording recording)
    {
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(recording, Airs);

        return (ledger, driver);
    }

    private static async Task Passes(HandTurnedClock clock, StreamLedger ledger, int count)
    {
        for (int pass = 0; pass < count; pass++)
        {
            await Eventually.Happens(() => clock.Pending is 1, "the watch never began waiting for its next pass");

            int listed = ledger.Listings;
            clock.Turn(Paced.BetweenWatches);

            await Eventually.Happens(
                () => ledger.Listings > listed && clock.Pending is 1,
                "the watch never finished the pass the clock was turned for");
        }
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
        RecordingWatchSettings? settings = null,
        SilentEvents? events = null,
        RecordingProgressSettings? progress = null)
        => new(
            Supervisor(ledger, driver, clock, settings: settings ?? Settings),
            signals,
            settings ?? Settings,
            progress ?? RecordingProgressSettings.Default,
            events ?? new SilentEvents(),
            clock,
            NullLogger<RecordingStreamJob>.Instance);
}
