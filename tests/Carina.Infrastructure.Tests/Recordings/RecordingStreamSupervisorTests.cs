using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;

using Microsoft.EntityFrameworkCore;

using static Carina.Infrastructure.Tests.Recordings.RecordingStreamFixture;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingStreamSupervisorTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task AStreamThatEndedOnItsOwnIsABreakToBeMendedRatherThanAnEnding()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Over(recording);

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)))
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(1, watch.Broken);
        Assert.Equal(0, watch.Settled);
        Assert.Null(read.Outcome);
        Assert.True(read.IsInFlight);
        Assert.True(Assert.Single(read.Interruptions).IsOpen);
    }

    [Fact]
    public async Task AnEndTheDriverReportedAsCleanIsStillTreatedAsUnfinished()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] =
            Over(recording, SessionStopReason.EndTimeReached);

        await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10))).WatchAsync(Cancel);

        Assert.Null(ledger.Read(recording.Id).Outcome);
        Assert.NotEmpty(driver.Started);
    }

    [Fact]
    public async Task AStreamThatWasNeverThereIsOpenedAgainAgainstTheSameFile()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver
        {
            WhenStarted = Live(recording, Airs.AddMinutes(10)),
        };

        await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10))).WatchAsync(Cancel);

        StartSessionRequest opened = Assert.Single(driver.Started);
        Recording read = ledger.Read(recording.Id);

        Assert.Equal(recording.Id.Wire, opened.RecordingId);
        Assert.Equal(RecordingSessions.Named(recording.Id), opened.SessionId);
        Assert.Equal(recording.OutputRoot.Value, opened.OutputRoot);
        Assert.Equal(SessionPurpose.Recording, opened.Purpose);
        Assert.Equal(recording.ExpectedWindowEnd, opened.EndsAt!.Value.UtcDateTime);
        Assert.Equal(1, read.ResumeCount);
        Assert.False(Assert.Single(read.Interruptions).IsOpen);
    }

    [Fact]
    public async Task TheInnerLoopStopsAfterFiveOpensAndWaitsTwoSecondsBetweenThem()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        var clock = new WatchClock(Airs.AddMinutes(10));

        RecordingWatch watch = await Supervisor(ledger, driver, clock).WatchAsync(Cancel);

        Assert.Equal(5, driver.Started.Count);
        Assert.Equal(4, clock.Waits.Count);
        Assert.All(clock.Waits, wait => Assert.Equal(TimeSpan.FromSeconds(2), wait));
        Assert.Equal(1, watch.LeftOpen);
        Assert.Equal(0, watch.Resumed);
        Assert.True(Assert.Single(ledger.Read(recording.Id).Interruptions).IsOpen);
    }

    [Fact]
    public async Task TheOuterLoopKeepsOpeningTheStreamAfterTheInnerOneGaveUp()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        RecordingStreamSupervisor supervisor = Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)));

        await supervisor.WatchAsync(Cancel);
        await supervisor.WatchAsync(Cancel);
        await supervisor.WatchAsync(Cancel);

        Assert.Equal(15, driver.Started.Count);
        Assert.Single(ledger.Read(recording.Id).Interruptions);
    }

    [Fact]
    public async Task ARecordingWhoseWindowHasClosedIsNotOpenedAgain()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();

        await Supervisor(
                ledger,
                driver,
                new WatchClock(Airs.AddMinutes(31)),
                new WeighedFiles { Weighs = 3_400_000_000 })
            .WatchAsync(Cancel);

        Assert.Empty(driver.Started);
        Assert.NotNull(ledger.Read(recording.Id).Outcome);
    }

    [Fact]
    public async Task TheClassOfTheBreakFollowsWhatTheDriverSaidStoppedTheStream()
    {
        Assert.Equal(RecordingFault.TunerContended, await BrokeBy(SessionStopReason.Preempted));
        Assert.Equal(RecordingFault.DrainGraceExpired, await BrokeBy(SessionStopReason.DrainCapReached));
        Assert.Equal(RecordingFault.DriverLost, await BrokeBy(SessionStopReason.DeviceFailed));
        Assert.Equal(RecordingFault.DriverLost, await BrokeBy(null));
    }

    [Fact]
    public async Task ADriverThatCannotBeReachedLeavesTheRecordingExactlyAsItWas()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver
        {
            WhenAsked = DriverCall<SessionSnapshot>.Unreachable("the socket is not there"),
        };

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)))
            .WatchAsync(Cancel);

        Assert.Empty(ledger.Saved);
        Assert.Empty(driver.Started);
        Assert.Equal(0, watch.Broken);
        Assert.Equal(0, watch.Settled);
    }

    [Fact]
    public async Task ASessionInAStateThisBuildCannotNameIsLeftAlone()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = DriverCall<SessionSnapshot>.Reached(
            new SessionSnapshot(
                RecordingSessions.Named(recording.Id),
                SessionPurpose.Recording,
                "adapter1",
                SessionState.Unspecified,
                Airs));

        await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10))).WatchAsync(Cancel);

        Assert.Empty(ledger.Saved);
        Assert.Empty(driver.Started);
    }

    [Fact]
    public async Task AStreamStillDrainingIsNotTakenAsOneThatEnded()
    {
        Recording recording = InFlight();
        recording.Abort(Airs.AddMinutes(30));
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = DriverCall<SessionSnapshot>.Reached(
            new SessionSnapshot(
                RecordingSessions.Named(recording.Id),
                SessionPurpose.Recording,
                "adapter1",
                SessionState.Stopping,
                Airs));

        RecordingWatch watch = await Supervisor(
                ledger,
                driver,
                new WatchClock(Airs.AddMinutes(31)),
                new WeighedFiles { Weighs = 3_400_000_000 })
            .WatchAsync(Cancel);

        Assert.Equal(0, watch.Settled);
        Assert.Null(ledger.Read(recording.Id).Outcome);
    }

    [Fact]
    public async Task WhatWasWrittenIsAddedToRatherThanReplacedEachTimeItIsRead()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(recording, Airs);
        var clock = new WatchClock(Airs.AddMinutes(10));
        RecordingStreamSupervisor supervisor = Supervisor(ledger, driver, clock);

        await supervisor.WatchAsync(Cancel);

        Assert.Equal(TimeSpan.FromMinutes(10), ledger.Read(recording.Id).Written);

        clock.Now = Airs.AddMinutes(20);
        await supervisor.WatchAsync(Cancel);

        Assert.Equal(TimeSpan.FromMinutes(20), ledger.Read(recording.Id).Written);
    }

    [Fact]
    public async Task TheTimeSpentWithoutAStreamIsNotCountedAsWritten()
    {
        Recording recording = InFlight(until: Airs.AddHours(1));
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        SessionId named = RecordingSessions.Named(recording.Id);
        driver.Holding[named] = Live(recording, Airs);
        var clock = new WatchClock(Airs.AddMinutes(10));
        RecordingStreamSupervisor supervisor = Supervisor(ledger, driver, clock);

        await supervisor.WatchAsync(Cancel);

        driver.Holding[named] = Over(recording);
        clock.Now = Airs.AddMinutes(15);
        await supervisor.WatchAsync(Cancel);

        driver.Holding[named] = Live(recording, Airs.AddMinutes(20));
        clock.Now = Airs.AddMinutes(25);
        await supervisor.WatchAsync(Cancel);

        Assert.Equal(TimeSpan.FromMinutes(15), ledger.Read(recording.Id).Written);

        clock.Now = Airs.AddMinutes(30);
        await supervisor.WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(TimeSpan.FromMinutes(20), read.Written);
        Assert.Equal(1, read.ResumeCount);
        Assert.False(Assert.Single(read.Interruptions).IsOpen);
    }

    [Fact]
    public async Task CountsAreKeptWhileTheRecordingRunsRatherThanOnlyWhenItEnds()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(
            recording,
            Airs,
            new SessionCounters(
                Packets: 1000,
                Drops: 3,
                ScrambledPackets: 5,
                DeviceOverflows: 2,
                CcMeasured: true,
                ScrambleMeasured: true,
                Positions: new DropPositionsDto(900_000, [new DropBucketDto(12, 3, 1)], [])));

        await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10))).WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.True(read.CcMeasured);
        Assert.Equal(3, read.CcDroppedPackets);
        Assert.Equal(5, read.ScrambledPackets);
        Assert.Equal(2, read.EovfCount);
        Assert.Equal(Airs.AddMinutes(10), read.MeasuredUpdatedAt);
        Assert.Equal(900_000, read.Positions.AnchorPcr);
        Assert.Equal(12, Assert.Single(read.Positions.Buckets).Second);
        Assert.True(read.IsInFlight);
    }

    [Fact]
    public async Task ATotalIsWhatTheStreamShouldHaveCarriedRatherThanWhatArrived()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(
            recording,
            Airs,
            new SessionCounters(Packets: 40, Drops: 117, CcMeasured: true));

        await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10))).WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(117, read.CcDroppedPackets);
        Assert.Equal(157, read.CcTotalPackets);
    }

    [Fact]
    public async Task NothingCountedIsNotWrittenDownAsCountedZero()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(
            recording,
            Airs,
            new SessionCounters(Packets: 1000, Drops: 3, ScrambledPackets: 5, CcMeasured: true, ScrambleMeasured: true));

        await Supervisor(
                ledger,
                driver,
                new WatchClock(Airs.AddMinutes(10)),
                status: new HeldStatus(Connected(DriverCapabilities.Recording)))
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.False(read.CcMeasured);
        Assert.Null(read.CcDroppedPackets);
        Assert.Null(read.CcTotalPackets);
        Assert.Null(read.ScrambledPackets);
    }

    [Fact]
    public async Task ACountIsNotWrittenBeforeTheDriverHasSaidWhatItCanCount()
    {
        Recording recording = InFlight();
        recording.Measure(DropCounters.Counted(3, 1000), DropTimeline.Unlocated, 5, 0, Airs.AddMinutes(5));
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(recording, Airs);

        await Supervisor(
                ledger,
                driver,
                new WatchClock(Airs.AddMinutes(10)),
                status: new HeldStatus(DriverObservation.NotConnected))
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Empty(ledger.Saved);
        Assert.True(read.CcMeasured);
        Assert.Equal(3, read.CcDroppedPackets);
    }

    [Fact]
    public async Task TheTunerTheStreamCameOffIsWrittenDownWhenTheLedgerDoesNotYetNameOne()
    {
        Recording recording = InFlight(deviceId: null);
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(
            recording,
            Airs,
            new SessionCounters(Packets: 1000, Drops: 3, CcMeasured: true),
            "adapter7");

        await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10))).WatchAsync(Cancel);

        Assert.Equal(new TunerDeviceId("adapter7"), ledger.Read(recording.Id).TunerDeviceId);
    }

    [Fact]
    public async Task TheTunerTheLedgerAlreadyNamesIsNotOverwrittenByTheOneTheSessionReports()
    {
        Recording recording = InFlight(deviceId: "adapter1");
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(
            recording,
            Airs,
            new SessionCounters(Packets: 1000, Drops: 3, CcMeasured: true),
            "adapter7");

        await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10))).WatchAsync(Cancel);

        Assert.Equal(new TunerDeviceId("adapter1"), ledger.Read(recording.Id).TunerDeviceId);
    }

    [Fact]
    public async Task AWriteThatLandedOnARowSomethingElseMovedIsTakenAgainRatherThanLeftForTheNextTurn()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger { Collisions = 1 };
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(recording, Airs);

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)))
            .WatchAsync(Cancel);

        Assert.Equal(2, ledger.Saved.Count);
        Assert.Equal(1, watch.Collisions);
        Assert.Equal(TimeSpan.FromMinutes(10), ledger.Read(recording.Id).Written);
    }

    [Fact]
    public async Task AWriteThatKeepsCollidingIsReportedRatherThanSwallowed()
    {
        Recording mine = InFlight(eventId: 11);
        Recording theirs = InFlight(eventId: 12, until: Airs.AddMinutes(45));
        var ledger = new StreamLedger { Collisions = 3 };
        ledger.Hold(mine, theirs);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(mine.Id)] = Live(mine, Airs);
        driver.Holding[RecordingSessions.Named(theirs.Id)] = Live(theirs, Airs);
        var said = new WhatTheWatchSaid();

        RecordingWatch watch = await Supervisor(
                ledger,
                driver,
                new WatchClock(Airs.AddMinutes(10)),
                logger: said.Logger())
            .WatchAsync(Cancel);

        Assert.Equal(3, watch.Collisions);
        Assert.Equal(4, ledger.Saved.Count);
        Assert.Contains(said.Lines, line => line.StartsWith("Error:", StringComparison.Ordinal));
        Assert.Equal(TimeSpan.FromMinutes(10), ledger.Read(theirs.Id).Written);
        Assert.Equal(TimeSpan.Zero, ledger.Read(mine.Id).Written);
    }

    [Fact]
    public async Task AWatchThatThrewOnOneRecordingStillReadsTheNext()
    {
        Recording mine = InFlight(eventId: 11, deviceId: null);
        Recording theirs = InFlight(eventId: 12, until: Airs.AddMinutes(45));
        var ledger = new StreamLedger();
        ledger.Hold(mine, theirs);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(mine.Id)] = DriverCall<SessionSnapshot>.Reached(
            new SessionSnapshot(
                RecordingSessions.Named(mine.Id),
                SessionPurpose.Recording,
                new string('x', 200),
                SessionState.Active,
                Airs));
        driver.Holding[RecordingSessions.Named(theirs.Id)] = Live(theirs, Airs);
        var said = new WhatTheWatchSaid();

        await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)), logger: said.Logger())
            .WatchAsync(Cancel);

        Assert.Contains(said.Lines, line => line.StartsWith("Error:", StringComparison.Ordinal));
        Assert.Equal(TimeSpan.FromMinutes(10), ledger.Read(theirs.Id).Written);
    }

    [Fact]
    public async Task ARecordingThatEndedWhileTheWatchWasReadingItIsLeftAlone()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        ledger.AfterListing = () =>
        {
            ledger.AfterListing = null;
            recording.Abort(Airs.AddMinutes(5));
            recording.Settle(RecordingOutcome.Complete, 3_400_000_000, Airs.AddMinutes(5));
        };
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(recording, Airs);
        var said = new WhatTheWatchSaid();

        RecordingWatch watch = await Supervisor(
                ledger,
                driver,
                new WatchClock(Airs.AddMinutes(10)),
                logger: said.Logger())
            .WatchAsync(Cancel);

        Assert.Equal(1, watch.Watched);
        Assert.Equal(0, watch.Kept);
        Assert.Empty(ledger.Saved);
        Assert.Empty(said.Lines);
        Assert.Equal(TimeSpan.Zero, ledger.Read(recording.Id).Written);
    }

    [Fact]
    public async Task ASessionTheDriverHasConcludedIsOverEvenIfItStillCallsItselfActive()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = DriverCall<SessionSnapshot>.Reached(
            new SessionSnapshot(
                RecordingSessions.Named(recording.Id),
                SessionPurpose.Recording,
                "adapter1",
                SessionState.Active,
                Airs)
            {
                Concluded = true,
            });

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)))
            .WatchAsync(Cancel);

        Assert.Equal(1, watch.Broken);
        Assert.NotEmpty(driver.Started);
        Assert.Single(ledger.Read(recording.Id).Interruptions);
    }

    [Fact]
    public async Task TheWindowIsOverAtItsEndRatherThanAfterIt()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(30)))
            .WatchAsync(Cancel);

        Assert.Equal(1, watch.Settled);
        Assert.Empty(driver.Started);
        Assert.Equal(RecordingOutcome.Failed, ledger.Read(recording.Id).Outcome);
    }

    [Fact]
    public async Task ARecordingThisSideStoppedEarlyIsJudgedRatherThanOpenedAgain()
    {
        Recording recording = InFlight();
        recording.Abort(Airs.AddMinutes(5));
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)))
            .WatchAsync(Cancel);

        Assert.Equal(1, watch.Settled);
        Assert.Empty(driver.Started);
        Assert.Equal(RecordingOutcome.Failed, ledger.Read(recording.Id).Outcome);
    }

    private static async Task<RecordingFault> BrokeBy(SessionStopReason? reason)
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();

        if (reason is { } named)
        {
            driver.Holding[RecordingSessions.Named(recording.Id)] = Over(recording, named);
        }

        await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10))).WatchAsync(Cancel);

        return Assert.Single(ledger.Read(recording.Id).Interruptions).Fault;
    }
}
