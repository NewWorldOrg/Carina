using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;

using static Carina.Infrastructure.Tests.Recordings.RecordingStreamFixture;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingStreamFreshnessTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ARecordingStoppedBetweenTheReadingAndTheWritingIsJudgedRatherThanGivenANewStream()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        ledger.AfterListing = () =>
        {
            ledger.AfterListing = null;
            recording.Abort(Airs.AddMinutes(9));
        };
        var driver = new WatchedDriver();

        RecordingWatch watch = await Supervisor(
                ledger,
                driver,
                new WatchClock(Airs.AddMinutes(10)),
                new WeighedFiles { Weighs = 3_400_000_000 })
            .WatchAsync(Cancel);

        Assert.Empty(driver.Started);
        Assert.Equal(0, watch.Broken);
        Assert.Equal(1, watch.Settled);
        Assert.Empty(ledger.Read(recording.Id).Interruptions);
        Assert.NotNull(ledger.Read(recording.Id).Outcome);
    }

    [Fact]
    public async Task ARecordingWhoseWindowGrewBetweenTheReadingAndTheWritingIsNotJudgedAsIfItHadClosed()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        ledger.AfterListing = () =>
        {
            ledger.AfterListing = null;
            recording.Extend(Airs.AddHours(1));
        };
        var driver = new WatchedDriver();

        RecordingWatch watch = await Supervisor(
                ledger,
                driver,
                new WatchClock(Airs.AddMinutes(31)),
                new WeighedFiles { Weighs = 3_400_000_000 })
            .WatchAsync(Cancel);

        Assert.Equal(0, watch.Settled);
        Assert.Equal(1, watch.Broken);
        Assert.NotEmpty(driver.Started);
        Assert.Null(ledger.Read(recording.Id).Outcome);
    }

    [Fact]
    public async Task ARecordingStillBeingWrittenAfterItEndedIsAskedToStopAndIsJudgedOnTheNextPass()
    {
        Recording recording = InFlight();
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(Airs.AddMinutes(9));
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        SessionId named = RecordingSessions.Named(recording.Id);
        driver.Holding[named] = Live(recording, Airs, new SessionCounters(Packets: 1000, Drops: 3, CcMeasured: true));
        var clock = new WatchClock(Airs.AddMinutes(10));
        RecordingStreamSupervisor supervisor = Supervisor(
            ledger,
            driver,
            clock,
            new WeighedFiles { Weighs = 3_400_000_000 });

        RecordingWatch first = await supervisor.WatchAsync(Cancel);

        Assert.Equal(1, first.StoodDown);
        Assert.Equal(0, first.Kept);
        Assert.Equal(0, first.Settled);
        Assert.Equal(named, Assert.Single(driver.Stopped));
        Assert.Empty(driver.Started);
        Assert.Empty(ledger.Saved);
        Assert.Equal(TimeSpan.FromMinutes(30), ledger.Read(recording.Id).Written);

        driver.Holding[named] = Over(recording);
        clock.Now = Airs.AddMinutes(11);

        RecordingWatch second = await supervisor.WatchAsync(Cancel);

        Assert.Equal(1, second.Settled);
        Assert.Equal(RecordingOutcome.Complete, ledger.Read(recording.Id).Outcome);
    }

    [Fact]
    public async Task ARecordingThatIsAlreadyDrainingIsNotAskedToStopAgain()
    {
        Recording recording = InFlight();
        recording.Abort(Airs.AddMinutes(9));
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] =
            In(recording, SessionState.Stopping, Airs);

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)))
            .WatchAsync(Cancel);

        Assert.Equal(0, watch.StoodDown);
        Assert.Equal(0, watch.Settled);
        Assert.Empty(driver.Stopped);
        Assert.Empty(ledger.Saved);
    }

    [Fact]
    public async Task AClockThatWentBackwardsDoesNotLetTheSameMinutesBeCountedTwice()
    {
        Recording recording = InFlight(until: Airs.AddHours(1));
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(recording, Airs);
        var clock = new WatchClock(Airs.AddMinutes(10));
        RecordingStreamSupervisor supervisor = Supervisor(ledger, driver, clock);

        await supervisor.WatchAsync(Cancel);

        Assert.Equal(TimeSpan.FromMinutes(10), ledger.Read(recording.Id).Written);
        Assert.Equal(Airs.AddMinutes(10), ledger.Read(recording.Id).MeasuredUpdatedAt);

        clock.Now = Airs.AddMinutes(5);
        await supervisor.WatchAsync(Cancel);

        Assert.Equal(TimeSpan.FromMinutes(10), ledger.Read(recording.Id).Written);
        Assert.Equal(Airs.AddMinutes(10), ledger.Read(recording.Id).MeasuredUpdatedAt);

        clock.Now = Airs.AddMinutes(15);
        await supervisor.WatchAsync(Cancel);

        Assert.Equal(TimeSpan.FromMinutes(15), ledger.Read(recording.Id).Written);
        Assert.Equal(Airs.AddMinutes(15), ledger.Read(recording.Id).MeasuredUpdatedAt);
    }

    [Fact]
    public async Task ARefusalThatSaysNothingAboutTheSessionEndsNothingEvenAfterTheWindowHasClosed()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver
        {
            WhenAsked = DriverCall<SessionSnapshot>.Refused(new DriverProblem("http500", [])),
        };
        var said = new WhatTheWatchSaid();

        RecordingWatch watch = await Supervisor(
                ledger,
                driver,
                new WatchClock(Airs.AddMinutes(31)),
                new WeighedFiles { Weighs = 3_400_000_000 },
                logger: said.Logger())
            .WatchAsync(Cancel);

        Assert.Equal(0, watch.Settled);
        Assert.Equal(1, watch.OutOfTouch);
        Assert.Empty(ledger.Saved);
        Assert.Null(ledger.Read(recording.Id).Outcome);
        Assert.Equal(2, said.Lines.Count(line => line.StartsWith("Warning:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ARefusalThatSaysNothingAboutTheSessionDoesNotOpenASecondStreamBesideTheFirst()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver
        {
            WhenAsked = DriverCall<SessionSnapshot>.Refused(new DriverProblem("http503", [])),
        };

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)))
            .WatchAsync(Cancel);

        Assert.Equal(0, watch.Broken);
        Assert.Equal(1, watch.OutOfTouch);
        Assert.Empty(driver.Started);
        Assert.Empty(ledger.Read(recording.Id).Interruptions);
    }

    [Fact]
    public async Task OnlyTheRefusalThatNamesNoSuchSessionSaysNothingIsWritingTheRecording()
    {
        Assert.Equal(1, (await Asked("noSuchSession")).Broken);
        Assert.Equal(0, (await Asked("badSessionId")).Broken);
        Assert.Equal(0, (await Asked("http500")).Broken);
    }

    [Fact]
    public async Task ASessionTheDriverCallsFailedIsOverRatherThanLeftUnwatched()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = In(recording, SessionState.Failed, Airs);

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)))
            .WatchAsync(Cancel);

        Assert.Equal(1, watch.Broken);
        Assert.NotEmpty(driver.Started);
        Assert.Single(ledger.Read(recording.Id).Interruptions);
    }

    [Theory]
    [InlineData(SessionState.Requested)]
    [InlineData(SessionState.Active)]
    [InlineData(SessionState.Stopping)]
    public async Task AStreamInAStateTheDriverIsStillWritingIsCountedRatherThanOverlooked(SessionState state)
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = In(
            recording,
            state,
            Airs,
            new SessionCounters(Packets: 1000, Drops: 3, CcMeasured: true));

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)))
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(1, watch.Kept);
        Assert.Equal(3, read.CcDroppedPackets);
        Assert.Equal(TimeSpan.FromMinutes(10), read.Written);
        Assert.Empty(driver.Started);
    }

    [Fact]
    public async Task ARecordingStoppedBetweenTheReadingAndTheWritingIsNotCountedIntoAnyFurther()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        ledger.AfterListing = () =>
        {
            ledger.AfterListing = null;
            recording.Abort(Airs.AddMinutes(9));
        };
        var driver = new WatchedDriver();
        SessionId named = RecordingSessions.Named(recording.Id);
        driver.Holding[named] = Live(
            recording,
            Airs,
            new SessionCounters(Packets: 1000, Drops: 3, CcMeasured: true));

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)))
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(1, watch.StoodDown);
        Assert.Equal(0, watch.Kept);
        Assert.Equal(named, Assert.Single(driver.Stopped));
        Assert.Empty(ledger.Saved);
        Assert.Equal(TimeSpan.Zero, read.Written);
        Assert.False(read.CcMeasured);
    }

    [Fact]
    public async Task ARecordingStoppedBetweenTheReadingAndTheWritingHasNothingCountedOntoIt()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        ledger.AfterFinding = () => recording.Abort(Airs.AddMinutes(9));
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(
            recording,
            Airs,
            new SessionCounters(Packets: 1000, Drops: 3, CcMeasured: true));

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)))
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(0, watch.Kept);
        Assert.Equal(TimeSpan.Zero, read.Written);
        Assert.False(read.CcMeasured);
    }

    [Fact]
    public async Task AWindowThatGrewBetweenTheReadingAndTheWritingIsNotGivenAnOutcome()
    {
        Recording recording = InFlight();
        recording.Wrote(TimeSpan.FromMinutes(30));
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        ledger.AfterFinding = () => recording.Extend(Airs.AddHours(1));
        var driver = new WatchedDriver();

        RecordingWatch watch = await Supervisor(
                ledger,
                driver,
                new WatchClock(Airs.AddMinutes(31)),
                new WeighedFiles { Weighs = 3_400_000_000 })
            .WatchAsync(Cancel);

        Assert.Equal(0, watch.Settled);
        Assert.Null(ledger.Read(recording.Id).Outcome);
        Assert.Empty(ledger.Saved);
    }

    [Fact]
    public async Task ARecordingStoppedBetweenTheReadingAndTheWritingIsNotBrokenOffAndOpenedAgain()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        ledger.AfterFinding = () => recording.Abort(Airs.AddMinutes(9));
        var driver = new WatchedDriver();

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)))
            .WatchAsync(Cancel);

        Assert.Equal(0, watch.Broken);
        Assert.Empty(driver.Started);
        Assert.Empty(ledger.Read(recording.Id).Interruptions);
        Assert.Empty(ledger.Saved);
    }

    [Fact]
    public async Task AnInstantThatHasAlreadyBeenCountedIsNotCountedAgain()
    {
        Recording recording = InFlight(until: Airs.AddHours(1));
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        SessionId named = RecordingSessions.Named(recording.Id);
        driver.Holding[named] = Live(recording, Airs, new SessionCounters(Packets: 1000, Drops: 3, CcMeasured: true));
        var clock = new WatchClock(Airs.AddMinutes(10));
        RecordingStreamSupervisor supervisor = Supervisor(ledger, driver, clock);

        await supervisor.WatchAsync(Cancel);

        Assert.Equal(3, ledger.Read(recording.Id).CcDroppedPackets);

        driver.Holding[named] = Live(recording, Airs, new SessionCounters(Packets: 1000, Drops: 9, CcMeasured: true));
        await supervisor.WatchAsync(Cancel);

        Assert.Equal(3, ledger.Read(recording.Id).CcDroppedPackets);
        Assert.Single(ledger.Saved);

        clock.Now = Airs.AddMinutes(10).AddTicks(1);
        await supervisor.WatchAsync(Cancel);

        Assert.Equal(9, ledger.Read(recording.Id).CcDroppedPackets);
        Assert.Equal(2, ledger.Saved.Count);
    }

    private static async Task<RecordingWatch> Asked(string title)
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver
        {
            WhenAsked = DriverCall<SessionSnapshot>.Refused(new DriverProblem(title, [])),
        };

        return await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10))).WatchAsync(Cancel);
    }
}
