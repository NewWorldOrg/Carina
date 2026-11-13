using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;
using Carina.Infrastructure.Tests.Fixtures.Recordings;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;

using Puppis.Contracts;

using static Carina.Infrastructure.Tests.Fixtures.Recordings.RecordingStreamFixture;

namespace Carina.Infrastructure.Tests.Unit.Infra.Recordings;

[Trait("Category", "Unit")]
public sealed class RecordingStreamSupervisorTests
{
    private const long WhatLandedBeforeTheDiskFilled = 4_096_000;

    private const long Packets = 1_000_000;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime LastWrite = Airs.AddMinutes(10).AddSeconds(0.08);

    private static readonly DateTime CameBack = Airs.AddMinutes(11).AddSeconds(51.79);

    private static readonly DateTime FirstWriteAfter = Airs.AddMinutes(11).AddSeconds(52.6);

    private static readonly DateTime Ended = Airs.AddMinutes(31);

    private static readonly SessionCounters WhatTheSessionEndedWith = new(
        Packets: Packets + 50_000,
        Drops: 7,
        ScrambledPackets: 9,
        DeviceOverflows: 2,
        CcMeasured: true,
        ScrambleMeasured: true);

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

    [Theory]
    [InlineData(SessionRefusalTitles.NoLock, TuneFailureKind.NoLock)]
    [InlineData(SessionRefusalTitles.NoData, TuneFailureKind.NoData)]
    public async Task ARecordingCutShortByReceptionKeepsWhichOfTheFourItWas(
        string title,
        TuneFailureKind kind)
    {
        Recording read = await BrokenAfterTheDriverSaid(title);

        Assert.Equal(RecordingFault.TuneFailed, Assert.Single(read.Interruptions).Fault);

        OutcomeDetail noted = Assert.Single(read.OutcomeDetail);

        Assert.Equal(RecordingFault.TuneFailed, noted.Fault);
        Assert.Equal(kind, noted.TuneFailure);
    }

    [Fact]
    public async Task ARecordingThatHadNotYetNamedItsTunerTakesTheOneTheBreakCameFrom()
    {
        Recording read = await BrokenAfterTheDriverSaid(SessionRefusalTitles.NoData, deviceId: null);

        Assert.Equal("adapter1", read.TunerDeviceId!.Value);
        Assert.Equal(TuneFailureKind.NoData, Assert.Single(read.OutcomeDetail).TuneFailure);
    }

    [Fact]
    public async Task ABreakThatSaysNothingAboutReceptionWritesNoClassOfItsOwn()
    {
        Recording read = await BrokenAfterTheDriverSaid(null);

        Assert.Equal(RecordingFault.DriverLost, Assert.Single(read.Interruptions).Fault);
        Assert.Empty(read.OutcomeDetail);
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

    [Fact]
    public async Task AWatchThatOnlyKeptWhatWasAlreadyRunningMovedItsCountsAndNothingElse()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(recording, Airs);

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)))
            .WatchAsync(Cancel);

        Assert.Equal(1, watch.Kept);
        Assert.False(watch.AnythingMoved);
        Assert.True(watch.CountsMoved);
    }

    [Fact]
    public async Task ASessionThatCarriedOnIntoTheFileLeavesTheStretchItMissedOnTheRecording()
    {
        Recording recording = InFlight();
        StreamLedger ledger = new();
        ledger.Hold(recording);
        WatchedDriver driver = new();
        driver.Holding[RecordingSessions.Named(recording.Id)] = CarriedOn(
            recording,
            lastWritten: Airs.AddMinutes(5).AddSeconds(12),
            firstWritten: Airs.AddMinutes(5).AddSeconds(14.5));
        RecordingStreamSupervisor supervisor = Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)));

        RecordingWatch first = await supervisor.WatchAsync(Cancel);
        await supervisor.WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);
        RecordingGap gap = Assert.Single(read.Gaps);

        Assert.True(first.CountsMoved);
        Assert.Equal(Airs.AddMinutes(5).AddSeconds(12), gap.From);
        Assert.Equal(Airs.AddMinutes(5).AddSeconds(14.5), gap.Until);
        Assert.Equal(2_500, read.MissedMs);
    }

    [Fact]
    public async Task ASessionThatBeganTheFileLeavesNoGap()
    {
        Recording recording = InFlight();
        StreamLedger ledger = new();
        ledger.Hold(recording);
        WatchedDriver driver = new();
        driver.Holding[RecordingSessions.Named(recording.Id)] = CarriedOn(
            recording,
            lastWritten: null,
            firstWritten: Airs.AddSeconds(1));

        await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10))).WatchAsync(Cancel);

        Assert.Empty(ledger.Read(recording.Id).Gaps);
        Assert.Equal(0, ledger.Read(recording.Id).MissedMs);
    }

    [Fact]
    public async Task AGapTheDriverPlacesBeforeTheRecordingBeganIsNotKept()
    {
        Recording recording = InFlight();
        StreamLedger ledger = new();
        ledger.Hold(recording);
        WatchedDriver driver = new();
        driver.Holding[RecordingSessions.Named(recording.Id)] = CarriedOn(
            recording,
            lastWritten: Airs.AddSeconds(-5),
            firstWritten: Airs.AddSeconds(2));

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10))).WatchAsync(Cancel);

        Assert.Equal(1, watch.Kept);
        Assert.Empty(ledger.Read(recording.Id).Gaps);
    }

    private static DriverCall<SessionSnapshot> CarriedOn(Recording recording, DateTime? lastWritten, DateTime firstWritten)
        => DriverCall<SessionSnapshot>.Reached(
            Live(recording, firstWritten.AddSeconds(-1)).Value! with
            {
                AppendedAfter = lastWritten is { } after ? new DateTimeOffset(after) : null,
                FirstWrittenAt = new DateTimeOffset(firstWritten),
            });

    [Fact]
    public async Task APassAtTheInstantARecordingWasLastCountedMovedNoCounts()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(recording, Airs);
        RecordingStreamSupervisor supervisor = Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)));

        RecordingWatch first = await supervisor.WatchAsync(Cancel);
        RecordingWatch again = await supervisor.WatchAsync(Cancel);

        Assert.True(first.CountsMoved);
        Assert.False(again.CountsMoved);
        Assert.False(again.SaysAnything);
    }

    [Fact]
    public async Task LossesCountedAfreshOnAStreamOpenedThatInstantMoveTheCountsThoughNothingMoreWasWritten()
    {
        (RecordingWatch watch, Recording read) = await ReadAgainOnAStreamOpenedThatInstant(
            new SessionCounters(Packets: 1000, Drops: 3, CcMeasured: true));

        Assert.True(watch.CountsMoved);
        Assert.Equal(3, read.CcDroppedPackets);
        Assert.Equal(TimeSpan.FromMinutes(10), read.Written);
    }

    [Fact]
    public async Task AStreamOpenedThatInstantThatHasCountedNothingYetMovesNoCounts()
    {
        (RecordingWatch watch, Recording read) = await ReadAgainOnAStreamOpenedThatInstant(
            new SessionCounters(Packets: 0, Drops: 0, CcMeasured: true));

        Assert.False(watch.CountsMoved);
        Assert.Equal(1, watch.Kept);
        Assert.Equal(TimeSpan.FromMinutes(10), read.Written);
    }

    [Fact(DisplayName = "a stream opened again adds what it counts to what the one before it counted, and places its losses after them")]
    public async Task AStreamOpenedAgainAddsWhatItCountsToWhatTheOneBeforeItCounted()
    {
        Recording recording = InFlight();
        StreamLedger ledger = new();
        ledger.Hold(recording);
        WatchedDriver driver = new();
        SessionId named = RecordingSessions.Named(recording.Id);
        WatchClock clock = new(Airs.AddMinutes(10));
        RecordingStreamSupervisor supervisor = Supervisor(ledger, driver, clock);
        driver.Holding[named] = Live(recording, Airs, Counted(1_000, 3, 20, 1, 900_000, new DropBucketDto(12, 3, 0)));

        await supervisor.WatchAsync(Cancel);

        DateTime reopened = Airs.AddMinutes(11);
        long clockWhenReopened = 900_000 + (662 * DropTimeline.TicksPerSecond);
        driver.Holding[named] = Live(recording, reopened, Counted(500, 2, 5, 2, clockWhenReopened, new DropBucketDto(5, 2, 0)));
        clock.Now = Airs.AddMinutes(12);
        RecordingWatch added = await supervisor.WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);
        Assert.True(added.CountsMoved);
        Assert.Equal(DropCounters.Counted(5, 1_505), read.Counters);
        Assert.Equal(25, read.ScrambledPackets);
        Assert.Equal(3, read.EovfCount);
        Assert.Equal(900_000, read.Positions.AnchorPcr);
        Assert.Equal([new DropBucket(12, 3, 0), new DropBucket(667, 2, 0)], read.Positions.Buckets);

        driver.Holding[named] = Live(recording, reopened, Counted(800, 4, 6, 2, clockWhenReopened, new DropBucketDto(5, 2, 0), new DropBucketDto(90, 2, 0)));
        clock.Now = Airs.AddMinutes(13);
        await supervisor.WatchAsync(Cancel);

        Recording again = ledger.Read(recording.Id);
        Assert.Equal(DropCounters.Counted(7, 1_807), again.Counters);
        Assert.Equal(26, again.ScrambledPackets);
        Assert.Equal(3, again.EovfCount);
        Assert.Equal(
            [new DropBucket(12, 3, 0), new DropBucket(667, 2, 0), new DropBucket(752, 2, 0)],
            again.Positions.Buckets);
    }

    private static SessionCounters Counted(
        long packets,
        long drops,
        long scrambled,
        long overflows,
        long anchor,
        params DropBucketDto[] placed)
        => new(
            Packets: packets,
            Drops: drops,
            ScrambledPackets: scrambled,
            DeviceOverflows: overflows,
            CcMeasured: true,
            ScrambleMeasured: true,
            Positions: new DropPositionsDto(anchor, placed, []));

    private static async Task<(RecordingWatch Watch, Recording Read)> ReadAgainOnAStreamOpenedThatInstant(
        SessionCounters counters)
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        SessionId named = RecordingSessions.Named(recording.Id);
        driver.Holding[named] = Live(recording, Airs, new SessionCounters(Packets: 1000, Drops: 0, CcMeasured: true));
        var clock = new WatchClock(Airs.AddMinutes(10));
        RecordingStreamSupervisor supervisor = Supervisor(ledger, driver, clock);

        await supervisor.WatchAsync(Cancel);

        driver.Holding[named] = Live(recording, Airs.AddMinutes(20), counters);
        clock.Now = Airs.AddMinutes(20);
        RecordingWatch watch = await supervisor.WatchAsync(Cancel);

        return (watch, ledger.Read(recording.Id));
    }

    [Fact]
    public async Task AStreamThatStaysWithoutATunerMovedOnlyOnThePassItBroke()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        RecordingStreamSupervisor supervisor = Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)));

        RecordingWatch broke = await supervisor.WatchAsync(Cancel);
        RecordingWatch still = await supervisor.WatchAsync(Cancel);
        RecordingWatch again = await supervisor.WatchAsync(Cancel);

        Assert.True(broke.AnythingMoved);
        Assert.False(still.AnythingMoved);
        Assert.False(again.AnythingMoved);
        Assert.False(still.CountsMoved);
        Assert.False(again.CountsMoved);
        Assert.True(still.SaysAnything);
        Assert.True(again.SaysAnything);
    }

    [Fact]
    public async Task AWatchThatGaveARecordingItsOutcomeMovedSomething()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);

        RecordingWatch watch = await Supervisor(ledger, new WatchedDriver(), new WatchClock(Airs.AddMinutes(30)))
            .WatchAsync(Cancel);

        Assert.Equal(1, watch.Settled);
        Assert.True(watch.AnythingMoved);
    }

    private static async Task<Recording> BrokenAfterTheDriverSaid(string? title, string? deviceId = "adapter1")
    {
        Recording recording = InFlight(deviceId: deviceId);
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();

        driver.Holding[RecordingSessions.Named(recording.Id)] =
            Over(recording, SessionStopReason.Unspecified, title);

        await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10))).WatchAsync(Cancel);

        return ledger.Read(recording.Id);
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

    [Fact]
    public async Task ARecordingWhoseDiskFilledFailsThereRatherThanBeingOpenedAgain()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] =
            Over(recording, SessionStopReason.RecordingFailed, SessionRefusalTitles.DiskFull);

        RecordingWatch watch = await Supervisor(
                ledger,
                driver,
                new WatchClock(Airs.AddMinutes(10)),
                new WeighedFiles { Weighs = WhatLandedBeforeTheDiskFilled })
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Empty(driver.Started);
        Assert.Equal(1, watch.Settled);
        Assert.Equal(0, watch.Broken);
        Assert.False(read.IsInFlight);
        Assert.Equal(RecordingOutcome.Failed, read.Outcome);
        Assert.Equal(RecordingFault.DiskExhausted, Assert.Single(read.OutcomeDetail).Fault);
        Assert.Equal(WhatLandedBeforeTheDiskFilled, read.FileSizeObserved);
        Assert.Empty(read.Interruptions);
    }

    [Theory]
    [InlineData(null, RecordingFault.SizeUnobserved)]
    [InlineData(0L, RecordingFault.NothingLanded)]
    public async Task AFullDiskUnderAFileThatWeighsNothingKnownSaysWhichOfTheTwoItWas(
        long? weighs,
        RecordingFault said)
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] =
            Over(recording, SessionStopReason.RecordingFailed, SessionRefusalTitles.DiskFull);

        await Supervisor(
                ledger,
                driver,
                new WatchClock(Airs.AddMinutes(10)),
                new WeighedFiles { Weighs = weighs })
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(RecordingOutcome.Failed, read.Outcome);
        Assert.Equal(
            [RecordingFault.DiskExhausted, said],
            read.OutcomeDetail.Select(detail => detail.Fault).ToArray());
        Assert.Equal(0, read.FileSizeObserved);
    }

    [Fact]
    public async Task ARecordingThatFailedOnAFullDiskIsNotOpenedAgainOnTheNextPass()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] =
            Over(recording, SessionStopReason.RecordingFailed, SessionRefusalTitles.DiskFull);
        RecordingStreamSupervisor supervisor = Supervisor(
            ledger,
            driver,
            new WatchClock(Airs.AddMinutes(10)),
            new WeighedFiles { Weighs = WhatLandedBeforeTheDiskFilled });

        await supervisor.WatchAsync(Cancel);
        RecordingWatch again = await supervisor.WatchAsync(Cancel);

        Assert.Empty(driver.Started);
        Assert.Equal(0, again.Watched);
        Assert.Single(ledger.Read(recording.Id).OutcomeDetail);
    }

    [Fact]
    public async Task ADiskThatFilledAfterTheWindowClosedStillFailsTheRecordingForTheFullDisk()
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] =
            Over(recording, SessionStopReason.RecordingFailed, SessionRefusalTitles.DiskFull);

        await Supervisor(
                ledger,
                driver,
                new WatchClock(Airs.AddMinutes(31)),
                new WeighedFiles { Weighs = WhatLandedBeforeTheDiskFilled })
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Empty(driver.Started);
        Assert.Equal(RecordingOutcome.Failed, read.Outcome);
        Assert.Equal(RecordingFault.DiskExhausted, Assert.Single(read.OutcomeDetail).Fault);
    }

    [Theory]
    [InlineData(SessionStopReason.RecordingFailed, null)]
    [InlineData(SessionStopReason.DeviceFailed, SessionRefusalTitles.DiskFull)]
    public async Task AStreamThatDidNotEndOnAFullDiskIsStillABreakToMend(SessionStopReason reason, string? title)
    {
        Recording recording = InFlight();
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Over(recording, reason, title);

        RecordingWatch watch = await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)))
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.NotEmpty(driver.Started);
        Assert.Equal(1, watch.Broken);
        Assert.Null(read.Outcome);
        Assert.Equal(RecordingFault.DriverLost, Assert.Single(read.Interruptions).Fault);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ADriverThatStaysDownForMinutesLeavesAnInterruptionAsLongAsTheStretchTheFileMissed(
        bool theFileSaysWhenItWasLastWritten)
    {
        Recording recording = InFlight();
        StreamLedger ledger = new();
        ledger.Hold(recording);
        WatchedDriver driver = new()
        {
            WhenAsked = DriverCall<SessionSnapshot>.Unreachable("the socket is not there"),
        };
        WeighedFiles files = new()
        {
            Weighs = 1_200_000_000,
            LastWritten = theFileSaysWhenItWasLastWritten ? LastWrite : null,
        };
        WatchClock clock = new(Airs.AddMinutes(10).AddSeconds(10));
        RecordingStreamSupervisor supervisor = Supervisor(ledger, driver, clock, files);

        for (int pass = 0; clock.Now < CameBack; pass++)
        {
            await supervisor.WatchAsync(Cancel);
            clock.Now = Airs.AddMinutes(10).AddSeconds(10 * (pass + 2));
        }

        Assert.Empty(ledger.Read(recording.Id).Interruptions);

        clock.Now = CameBack;
        driver.WhenAsked = DriverCall<SessionSnapshot>.Refused(new DriverProblem("noSuchSession", []));
        driver.WhenStarted = Live(recording, CameBack.AddSeconds(0.5));
        await supervisor.WatchAsync(Cancel);

        Interruption noticed = Assert.Single(ledger.Read(recording.Id).Interruptions);
        Assert.Equal(RecordingFault.DriverLost, noticed.Fault);
        Assert.Equal(theFileSaysWhenItWasLastWritten ? LastWrite : CameBack, noticed.OccurredAt);

        clock.Now = CameBack.AddSeconds(10);
        driver.Holding[RecordingSessions.Named(recording.Id)] = DriverCall<SessionSnapshot>.Reached(
            Live(recording, CameBack.AddSeconds(0.5)).Value! with
            {
                AppendedAfter = new DateTimeOffset(LastWrite),
                FirstWrittenAt = new DateTimeOffset(FirstWriteAfter),
            });
        await supervisor.WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);
        Interruption placed = Assert.Single(read.Interruptions);
        RecordingGap gap = Assert.Single(read.Gaps);

        Assert.Equal(LastWrite, placed.OccurredAt);
        Assert.Equal(FirstWriteAfter, placed.ResumedAt);
        Assert.Equal(gap.From, placed.OccurredAt);
        Assert.Equal(gap.Until, placed.ResumedAt);
        Assert.Equal(112_520, read.MissedMs);
        Assert.Equal(1, read.ResumeCount);
    }

    [Fact]
    public async Task AFileLastWrittenBeforeTheStretchThatResumedLastOpensTheBreakWhereThatStretchResumed()
    {
        Recording recording = InFlight();
        StreamLedger ledger = new();
        recording.Interrupt(RecordingFault.DriverLost, Airs.AddMinutes(3));
        recording.Resume(Airs.AddMinutes(4));
        ledger.Hold(recording);
        WatchedDriver driver = new();
        WeighedFiles files = new() { Weighs = 1_200_000_000, LastWritten = Airs.AddMinutes(2) };

        await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)), files).WatchAsync(Cancel);

        Interruption opened = ledger.Read(recording.Id).Interruptions[^1];

        Assert.Equal(Airs.AddMinutes(4), opened.OccurredAt);
    }

    [Fact]
    public async Task AFileTheClockSaysWasWrittenAfterTheBreakWasNoticedOpensTheBreakWhenItWasNoticed()
    {
        Recording recording = InFlight();
        StreamLedger ledger = new();
        ledger.Hold(recording);
        WatchedDriver driver = new();
        WeighedFiles files = new() { Weighs = 1_200_000_000, LastWritten = Airs.AddMinutes(11) };

        await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)), files).WatchAsync(Cancel);

        Assert.Equal(Airs.AddMinutes(10), Assert.Single(ledger.Read(recording.Id).Interruptions).OccurredAt);
    }

    [Fact(DisplayName = "the losses counted after the last pass are in the ledger once the recording is judged")]
    public async Task TheLossesCountedAfterTheLastPassAreInTheLedgerOnceTheRecordingIsJudged()
    {
        Recording recording = CountedUntilTheLastPass();
        StreamLedger ledger = new();
        ledger.Hold(recording);

        await Supervisor(ledger, Ending(recording, WhatTheSessionEndedWith), new WatchClock(Ended), Weighed())
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.NotNull(read.Outcome);
        Assert.Equal(7, read.CcDroppedPackets);
        Assert.Equal(Packets + 50_007, read.CcTotalPackets);
        Assert.Equal(9, read.ScrambledPackets);
        Assert.Equal(2, read.EovfCount);
        Assert.Equal(Ended, read.MeasuredUpdatedAt);
    }

    [Fact(DisplayName = "scrambling the session counted after the last pass decides whether the recording ends left scrambled")]
    public async Task ScramblingCountedAfterTheLastPassDecidesWhetherTheRecordingEndsLeftScrambled()
    {
        Recording recording = CountedUntilTheLastPass();
        StreamLedger ledger = new();
        ledger.Hold(recording);
        SessionCounters scrambledAtTheEnd = WhatTheSessionEndedWith with { ScrambledPackets = 600_000 };

        await Supervisor(ledger, Ending(recording, scrambledAtTheEnd), new WatchClock(Ended), Weighed())
            .WatchAsync(Cancel);

        Assert.Contains(
            ledger.Read(recording.Id).OutcomeDetail,
            detail => detail.Fault is RecordingFault.ScramblingUnresolved);
    }

    [Fact(DisplayName = "a recording is still judged on what the ledger holds when the driver's greeting cannot be read")]
    public async Task ARecordingIsStillJudgedOnWhatTheLedgerHoldsWhenTheGreetingCannotBeRead()
    {
        Recording recording = CountedUntilTheLastPass();
        StreamLedger ledger = new();
        ledger.Hold(recording);

        await Supervisor(
                ledger,
                Ending(recording, WhatTheSessionEndedWith),
                new WatchClock(Ended),
                Weighed(),
                new HeldStatus(DriverObservation.NotConnected))
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.NotNull(read.Outcome);
        Assert.Equal(3, read.CcDroppedPackets);
        Assert.Equal(5, read.ScrambledPackets);
        Assert.Equal(Airs.AddMinutes(29), read.MeasuredUpdatedAt);
    }

    [Fact(DisplayName = "a session that counted nothing leaves the counts the ledger already holds")]
    public async Task ASessionThatCountedNothingLeavesTheCountsTheLedgerAlreadyHolds()
    {
        Recording recording = CountedUntilTheLastPass();
        StreamLedger ledger = new();
        ledger.Hold(recording);

        await Supervisor(ledger, Ending(recording, SessionCounters.Nothing), new WatchClock(Ended), Weighed())
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.True(read.CcMeasured);
        Assert.Equal(3, read.CcDroppedPackets);
        Assert.Equal(5, read.ScrambledPackets);
    }

    [Fact(DisplayName = "the losses counted after the last pass are in the ledger when the recording fails on a full disk")]
    public async Task TheLossesCountedAfterTheLastPassAreInTheLedgerWhenTheRecordingFailsOnAFullDisk()
    {
        Recording recording = CountedUntilTheLastPass();
        StreamLedger ledger = new();
        ledger.Hold(recording);
        WatchedDriver driver = new();
        driver.Holding[RecordingSessions.Named(recording.Id)] = OnAFullDisk(recording);

        await Supervisor(ledger, driver, new WatchClock(Ended), Weighed()).WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(RecordingOutcome.Failed, read.Outcome);
        Assert.Equal(7, read.CcDroppedPackets);
        Assert.Equal(9, read.ScrambledPackets);
    }

    [Fact(DisplayName = "the losses counted after the last pass are in the ledger when recovery marks what was left behind")]
    public async Task TheLossesCountedAfterTheLastPassAreInTheLedgerWhenRecoveryMarksWhatWasLeftBehind()
    {
        Recording recording = CountedUntilTheLastPass();
        StreamLedger ledger = new();
        ledger.Hold(recording);
        SessionSnapshot stopped = Stopped(recording, SessionStopReason.DeviceFailed, WhatTheSessionEndedWith);

        await Recovery(ledger, new WatchedDriver(), new WatchClock(Ended), Weighed())
            .RecoverAsync(Greeting(), [stopped], Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.NotNull(read.Outcome);
        Assert.Equal(7, read.CcDroppedPackets);
        Assert.Equal(9, read.ScrambledPackets);
        Assert.Equal(Ended, read.MeasuredUpdatedAt);
    }

    [Fact(DisplayName = "the losses counted after the last pass are in the ledger when recovery fails a recording on a full disk")]
    public async Task TheLossesCountedAfterTheLastPassAreInTheLedgerWhenRecoveryFailsARecordingOnAFullDisk()
    {
        Recording recording = CountedUntilTheLastPass();
        StreamLedger ledger = new();
        ledger.Hold(recording);
        SessionSnapshot full = OnAFullDisk(recording).Value!;

        await Recovery(ledger, new WatchedDriver(), new WatchClock(Ended), Weighed())
            .RecoverAsync(Greeting(), [full], Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(RecordingOutcome.Failed, read.Outcome);
        Assert.Equal(7, read.CcDroppedPackets);
    }

    [Fact(DisplayName = "a recording already counted at the moment it is judged keeps that count rather than an older one")]
    public async Task ARecordingAlreadyCountedAtTheMomentItIsJudgedKeepsThatCount()
    {
        Recording recording = CountedUntilTheLastPass(Ended);
        StreamLedger ledger = new();
        ledger.Hold(recording);

        await Supervisor(ledger, Ending(recording, WhatTheSessionEndedWith), new WatchClock(Ended), Weighed())
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.NotNull(read.Outcome);
        Assert.Equal(3, read.CcDroppedPackets);
        Assert.Equal(5, read.ScrambledPackets);
        Assert.Equal(Ended, read.MeasuredUpdatedAt);
    }

    [Fact(DisplayName = "a recording recovery finds already counted at that moment keeps that count rather than an older one")]
    public async Task ARecordingRecoveryFindsAlreadyCountedKeepsThatCount()
    {
        Recording recording = CountedUntilTheLastPass(Ended);
        StreamLedger ledger = new();
        ledger.Hold(recording);
        SessionSnapshot stopped = Stopped(recording, SessionStopReason.DeviceFailed, WhatTheSessionEndedWith);

        await Recovery(ledger, new WatchedDriver(), new WatchClock(Ended), Weighed())
            .RecoverAsync(Greeting(), [stopped], Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.NotNull(read.Outcome);
        Assert.Equal(3, read.CcDroppedPackets);
        Assert.Equal(Ended, read.MeasuredUpdatedAt);
    }

    [Fact(DisplayName = "a recording that had not yet named its tuner takes the one the session it ended on was written from")]
    public async Task ARecordingThatHadNotYetNamedItsTunerTakesTheOneItsLastSessionWasWrittenFrom()
    {
        Recording recording = InFlight(deviceId: null);
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(Airs.AddMinutes(30));
        StreamLedger ledger = new();
        ledger.Hold(recording);

        await Supervisor(ledger, Ending(recording, WhatTheSessionEndedWith), new WatchClock(Ended), Weighed())
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(new TunerDeviceId("adapter1"), read.TunerDeviceId);
        Assert.Equal(7, read.CcDroppedPackets);
    }

    private static Recording CountedUntilTheLastPass(DateTime? lastCounted = null)
    {
        Recording recording = InFlight();
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Measure(
            DropCounters.Counted(3, Packets),
            DropTimeline.Unlocated,
            5,
            1,
            lastCounted ?? Airs.AddMinutes(29),
            Airs);
        recording.Abort(Airs.AddMinutes(30));

        return recording;
    }

    private static WeighedFiles Weighed() => new() { Weighs = 3_400_000_000 };

    private static WatchedDriver Ending(Recording recording, SessionCounters counters)
    {
        WatchedDriver driver = new();
        driver.Holding[RecordingSessions.Named(recording.Id)] = DriverCall<SessionSnapshot>.Reached(
            Stopped(recording, SessionStopReason.EndTimeReached, counters));

        return driver;
    }

    private static DriverCall<SessionSnapshot> OnAFullDisk(Recording recording)
        => DriverCall<SessionSnapshot>.Reached(
            Stopped(recording, SessionStopReason.RecordingFailed, WhatTheSessionEndedWith) with
            {
                FailureTitle = SessionRefusalTitles.DiskFull,
            });

    private static SessionSnapshot Stopped(Recording recording, SessionStopReason reason, SessionCounters counters)
        => Over(recording, reason).Value! with { Counters = counters };

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

    [Fact]
    public async Task ARecordingLeftScrambledPastTheLevelEndsWithThatClassInItsDetail()
    {
        Recording read = await Settled(scrambled: 600_000);

        OutcomeDetail scrambling = Assert.Single(read.OutcomeDetail);

        Assert.Equal(RecordingFault.ScramblingUnresolved, scrambling.Fault);
        Assert.Equal(Ended, scrambling.NoticedAt);
        Assert.Equal(RecordingOutcome.Complete, read.Outcome);
    }

    [Fact]
    public async Task ARecordingScrambledOnlyUnderTheLevelEndsWithNoClassForIt()
    {
        Recording read = await Settled(scrambled: 10);

        Assert.Empty(read.OutcomeDetail);
        Assert.Equal(RecordingOutcome.Complete, read.Outcome);
    }

    [Fact]
    public async Task ARecordingWhoseScramblingWasNeverCountedEndsWithNoClassForIt()
    {
        Recording read = await Settled(scrambled: null);

        Assert.Empty(read.OutcomeDetail);
    }

    [Fact]
    public async Task TheLevelIsTheOneSetForTheQualityLedgerRatherThanOneOfItsOwn()
    {
        HeldQualityThresholds thresholds = new();
        thresholds.Thresholds.Add(Set(QualityThresholdKey.PacketsLeftScrambled, 0.5));
        thresholds.Thresholds.Add(Set(QualityThresholdKey.PacketsLeftScrambledUnwatchable, 0.9));

        Recording asShipped = await Settled(scrambled: 100_000);
        Recording raised = await Settled(scrambled: 100_000, thresholds);

        Assert.Contains(asShipped.OutcomeDetail, detail => detail.Fault is RecordingFault.ScramblingUnresolved);
        Assert.Empty(raised.OutcomeDetail);
    }

    [Fact]
    public async Task ARecordingOverAndUnknownToTheDriverCarriesTheClassWhenItWasLeftScrambled()
    {
        Recording recording = Scrambled(600_000);
        StreamLedger ledger = new();
        ledger.Hold(recording);

        await Supervisor(ledger, new WatchedDriver(), new WatchClock(Ended), new WeighedFiles { Weighs = 3_400_000_000 })
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Contains(read.OutcomeDetail, detail => detail.Fault is RecordingFault.ScramblingUnresolved);
    }

    [Fact]
    public async Task ARecordingRecoveryMarksCarriesTheClassWhenItWasLeftScrambled()
    {
        Recording recording = Scrambled(600_000);
        StreamLedger ledger = new();
        ledger.Hold(recording);

        await Recovery(ledger, new WatchedDriver(), new WatchClock(Ended), new WeighedFiles { Weighs = 3_400_000_000 })
            .RecoverAsync(Greeting(), [], Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Contains(read.OutcomeDetail, detail => detail.Fault is RecordingFault.ScramblingUnresolved);
    }

    [Fact]
    public async Task ARecordingRecoveryMarksCarriesNoClassWhenItsScramblingStayedUnderTheLevel()
    {
        Recording recording = Scrambled(10);
        StreamLedger ledger = new();
        ledger.Hold(recording);

        await Recovery(ledger, new WatchedDriver(), new WatchClock(Ended), new WeighedFiles { Weighs = 3_400_000_000 })
            .RecoverAsync(Greeting(), [], Cancel);

        Assert.DoesNotContain(
            ledger.Read(recording.Id).OutcomeDetail,
            detail => detail.Fault is RecordingFault.ScramblingUnresolved);
    }

    private static Recording Scrambled(long scrambled)
    {
        Recording recording = InFlight();
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Measure(DropCounters.Counted(0, Packets), DropTimeline.Unlocated, scrambled, 0, Airs.AddMinutes(29));

        return recording;
    }

    private static QualityThreshold Set(QualityThresholdKey key, double level)
        => QualityThreshold.Declare(key, Threshold.Of(level, level, provisional: true, 0, Airs));

    private static async Task<Recording> Settled(long? scrambled, HeldQualityThresholds? thresholds = null)
    {
        Recording recording = InFlight();
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Measure(DropCounters.Counted(0, Packets), DropTimeline.Unlocated, scrambled, 0, Airs.AddMinutes(29));
        recording.Abort(Airs.AddMinutes(30));

        StreamLedger ledger = new();
        ledger.Hold(recording);
        WatchedDriver driver = new();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Over(recording);

        await Supervisor(
                ledger,
                driver,
                new WatchClock(Ended),
                new WeighedFiles { Weighs = 3_400_000_000 },
                thresholds: thresholds)
            .WatchAsync(Cancel);

        return ledger.Read(recording.Id);
    }

    [Fact]
    public async Task ARecordingThisSideAskedToStopIsWeighedAgainstTheFileAndCalledComplete()
    {
        Recording read = await Judged(TimeSpan.FromMinutes(30), 3_400_000_000, asked: true);

        Assert.Equal(RecordingOutcome.Complete, read.Outcome);
        Assert.Empty(read.OutcomeDetail);
        Assert.Equal(3_400_000_000, read.FileSizeObserved);
        Assert.Equal(Ended, read.StoppedAtActual);
        Assert.Equal(Ended, read.ObservedAt);
    }

    [Fact]
    public async Task AnEndNobodyAskedForCannotBeCompleteHoweverWellItWentOtherwise()
    {
        Recording read = await Judged(
            TimeSpan.FromMinutes(30),
            3_400_000_000,
            asked: false,
            reason: SessionStopReason.DeviceFailed);

        OutcomeDetail unasked = Assert.Single(read.OutcomeDetail);

        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Equal(RecordingFault.StoppedUnasked, unasked.Fault);
        Assert.Equal(Ended, unasked.NoticedAt);
    }

    [Fact]
    public async Task AnEndTheDriverWasToldAboutIsAnEndThisSideAskedForAndIsWeighedLikeOne()
    {
        Recording read = await Judged(TimeSpan.FromMinutes(30), 3_400_000_000, asked: false);

        Assert.Equal(RecordingOutcome.Complete, read.Outcome);
        Assert.Empty(read.OutcomeDetail);
        Assert.NotNull(read.AbortedAt);
    }

    [Fact]
    public async Task AnEndTheDriverWasToldAboutIsStillCutShortWhenTooLittleOfTheWindowWasCovered()
    {
        Recording read = await Judged(TimeSpan.FromSeconds(1750), 3_300_000_000, asked: false);

        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Contains(read.OutcomeDetail, detail => detail.Fault is RecordingFault.ShortOfTheWindow);
        Assert.DoesNotContain(read.OutcomeDetail, detail => detail.Fault is RecordingFault.StoppedUnasked);
    }

    [Fact]
    public async Task AnEndTheDriverWasToldAboutIsStillCutShortWhenTheFileIsTooLightForTheClock()
    {
        Recording read = await Judged(TimeSpan.FromMinutes(30), 2_000_000_000, asked: false);

        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Contains(read.OutcomeDetail, detail => detail.Fault is RecordingFault.LighterThanTheStream);
        Assert.DoesNotContain(read.OutcomeDetail, detail => detail.Fault is RecordingFault.StoppedUnasked);
    }

    [Fact]
    public async Task AnEndTheDriverWasToldAboutIsStillAFailureWhenNothingLanded()
    {
        Recording read = await Judged(TimeSpan.FromMinutes(30), 0, asked: false);

        Assert.Equal(RecordingOutcome.Failed, read.Outcome);
        Assert.Contains(read.OutcomeDetail, detail => detail.Fault is RecordingFault.NothingLanded);
    }

    [Theory]
    [InlineData(SessionStopReason.DeviceFailed)]
    [InlineData(SessionStopReason.RecordingFailed)]
    [InlineData(SessionStopReason.Preempted)]
    [InlineData(SessionStopReason.DrainCapReached)]
    [InlineData(SessionStopReason.Unspecified)]
    [InlineData(SessionStopReason.Requested)]
    [InlineData(SessionStopReason.Running)]
    public async Task AnEndTheDriverGivesAnyOtherReasonForIsStillAnEndNobodyAskedFor(SessionStopReason reason)
    {
        Recording read = await Judged(TimeSpan.FromMinutes(30), 3_400_000_000, asked: false, reason: reason);

        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Contains(read.OutcomeDetail, detail => detail.Fault is RecordingFault.StoppedUnasked);
        Assert.Null(read.AbortedAt);
    }

    [Fact]
    public async Task TheEndWrittenDownIsTheOneTheDriverWasHoldingRatherThanTheMomentItWasNoticed()
    {
        Recording read = await Judged(
            TimeSpan.FromMinutes(30),
            3_400_000_000,
            asked: false,
            endsAt: Airs.AddMinutes(30));

        Assert.Equal(Airs.AddMinutes(30), read.AbortedAt);
        Assert.Equal(Ended, read.StoppedAtActual);
        Assert.True(read.AbortedAt >= read.StartedAtActual, "The end was written down before the recording began.");
        Assert.True(read.AbortedAt <= read.StoppedAtActual, "The end was written down after the recording stopped.");
    }

    [Fact]
    public async Task AnEndAnExtensionMovedIsWrittenDownWhereTheExtensionLeftIt()
    {
        Recording read = await Judged(
            TimeSpan.FromMinutes(30),
            3_400_000_000,
            asked: false,
            endsAt: Airs.AddMinutes(30.5));

        Assert.Equal(Airs.AddMinutes(30.5), read.AbortedAt);
    }

    [Fact]
    public async Task ASessionThatNamesNoEndOfItsOwnIsWrittenDownAtTheMomentOfTheJudgement()
    {
        Recording read = await Judged(TimeSpan.FromMinutes(30), 3_400_000_000, asked: false);

        Assert.Equal(Ended, read.AbortedAt);
    }

    [Fact]
    public async Task AnEndLaterThanTheMomentOfTheJudgementIsNotWrittenDownInTheFuture()
    {
        Recording read = await Judged(
            TimeSpan.FromMinutes(30),
            3_400_000_000,
            asked: false,
            endsAt: Ended.AddMinutes(5));

        Assert.Equal(Ended, read.AbortedAt);
    }

    [Fact]
    public async Task AnEndTheDriverNamesAtTheVeryMomentTheRecordingBeganIsStillAnEndItReached()
    {
        Recording read = await Judged(TimeSpan.FromMinutes(30), 3_400_000_000, asked: false, endsAt: Airs);

        Assert.Equal(Airs, read.AbortedAt);
        Assert.Equal(RecordingOutcome.Complete, read.Outcome);
    }

    [Fact]
    public async Task AnEndTheDriverNamesBeforeTheRecordingBeganIsNoEndThisSideCanHaveAskedFor()
    {
        Recording read = await Judged(
            TimeSpan.FromMinutes(30),
            3_400_000_000,
            asked: false,
            endsAt: Airs.AddMinutes(-5));

        Assert.Null(read.AbortedAt);
        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Contains(read.OutcomeDetail, detail => detail.Fault is RecordingFault.StoppedUnasked);
    }

    [Fact]
    public async Task TheEndThisSideAlreadyWroteDownIsNotMovedByTheOneTheDriverWasHolding()
    {
        Recording read = await Judged(
            TimeSpan.FromMinutes(30),
            3_400_000_000,
            asked: true,
            endsAt: Airs.AddMinutes(30.5));

        Assert.Equal(Airs.AddMinutes(30), read.AbortedAt);
        Assert.Equal(RecordingOutcome.Complete, read.Outcome);
    }

    [Fact]
    public async Task ARecordingOverAndUnknownToTheDriverIsMarkedTheWayRecoveryMarksItRatherThanJudged()
    {
        Recording recording = Ready(TimeSpan.FromMinutes(30), asked: true);
        var ledger = new StreamLedger();
        ledger.Hold(recording);

        RecordingWatch watch = await Supervisor(
                ledger,
                new WatchedDriver(),
                new WatchClock(Ended),
                new WeighedFiles { Weighs = 3_400_000_000 })
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(1, watch.Settled);
        Assert.NotEqual(RecordingOutcome.Complete, read.Outcome);
        Assert.Contains(read.OutcomeDetail, detail => detail.Fault is RecordingFault.LeftRunningUnwatched);
        Assert.Equal(3_400_000_000, read.FileSizeObserved);
    }

    [Fact]
    public async Task ARecordingOverAndUnknownToTheDriverEndsWhereRecoveryWouldHaveEndedItToo()
    {
        Recording recording = Ready(TimeSpan.FromMinutes(30), asked: true);
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var files = new WeighedFiles { Weighs = 3_400_000_000 };

        await Supervisor(ledger, new WatchedDriver(), new WatchClock(Ended), files).WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(OrphanRecovery.WhatIsLeftOf(3_400_000_000), read.Outcome);
        Assert.Equal(
            OrphanRecovery.WhyItEndedWhereItDid(driverIsAnotherInstance: false, 3_400_000_000, QualityLevel.Unmeasured),
            read.OutcomeDetail.Select(detail => detail.Fault).ToArray());
    }

    [Fact]
    public async Task AnEmptyFileIsAFailureWhateverElseWasObserved()
    {
        Recording read = await Judged(TimeSpan.FromMinutes(30), 0, asked: true);

        Assert.Equal(RecordingOutcome.Failed, read.Outcome);
        Assert.Contains(read.OutcomeDetail, detail => detail.Fault is RecordingFault.NothingLanded);
    }

    [Fact]
    public async Task AFileThatCouldNotBeReadOffTheDiskSaysSoRatherThanReadingAsEmpty()
    {
        Recording read = await Judged(TimeSpan.FromMinutes(30), null, asked: true);

        Assert.Equal(RecordingOutcome.Failed, read.Outcome);
        Assert.Contains(read.OutcomeDetail, detail => detail.Fault is RecordingFault.SizeUnobserved);
        Assert.DoesNotContain(read.OutcomeDetail, detail => detail.Fault is RecordingFault.NothingLanded);
        Assert.Equal(0, read.FileSizeObserved);
    }

    [Fact]
    public async Task ARecordingThatCoveredLessOfTheWindowThanItPromisedIsCutShort()
    {
        Recording read = await Judged(TimeSpan.FromSeconds(1750), 3_300_000_000, asked: true);

        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Equal(RecordingFault.ShortOfTheWindow, Assert.Single(read.OutcomeDetail).Fault);
    }

    [Fact]
    public async Task ARecordingThatCoveredMostButNotAllOfTheWindowRanShortRatherThanFailed()
    {
        Recording read = await Judged(TimeSpan.FromSeconds(1700), 3_200_000_000, asked: true);

        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Equal(RecordingFault.ShortOfTheWindow, Assert.Single(read.OutcomeDetail).Fault);
    }

    [Fact]
    public async Task ARecordingThatCoveredLessThanHalfOfTheWindowFailedRatherThanRanShort()
    {
        Recording read = await Judged(TimeSpan.FromSeconds(850), 1_600_000_000, asked: true);

        Assert.Equal(RecordingOutcome.Failed, read.Outcome);
        Assert.Equal(RecordingFault.ShortOfTheWindow, Assert.Single(read.OutcomeDetail).Fault);
    }

    [Fact]
    public async Task AFileTooLightForTheClockLetsTheBytesArgueWithTheClock()
    {
        Recording read = await Judged(TimeSpan.FromMinutes(30), 2_000_000_000, asked: true);

        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Equal(RecordingFault.LighterThanTheStream, Assert.Single(read.OutcomeDetail).Fault);
    }

    [Fact]
    public async Task AFileHeavierThanTheStreamIsNotedAndTheVerdictStaysWhereItWas()
    {
        Recording read = await Judged(TimeSpan.FromMinutes(30), 5_000_000_000, asked: true);

        Assert.Equal(RecordingOutcome.Complete, read.Outcome);
        Assert.Equal(RecordingFault.HeavierThanTheStream, Assert.Single(read.OutcomeDetail).Fault);
    }

    [Fact]
    public async Task TheWeightIsJudgedAgainstTheRatesMeasuredOffTheKindTheServiceIsCarriedOn()
    {
        Recording satellite = await Judged(TimeSpan.FromMinutes(30), 2_400_000_000, asked: true, tuning: Satellite);
        Recording terrestrial = await Judged(TimeSpan.FromMinutes(30), 2_400_000_000, asked: true, tuning: Terrestrial);

        Assert.Equal(RecordingOutcome.Complete, satellite.Outcome);
        Assert.Equal(RecordingOutcome.Truncated, terrestrial.Outcome);
        Assert.Equal(RecordingFault.LighterThanTheStream, Assert.Single(terrestrial.OutcomeDetail).Fault);
    }

    [Fact]
    public async Task TheFileTheVerdictIsWeighedAgainstIsTheOneTheLedgerNames()
    {
        Recording recording = Ready(TimeSpan.FromMinutes(30), asked: true);
        var ledger = new StreamLedger();
        ledger.Hold(recording);
        var files = new WeighedFiles { Weighs = 3_400_000_000 };

        await Supervisor(ledger, Concluded(recording), new WatchClock(Ended), files).WatchAsync(Cancel);

        Assert.Equal($"{recording.OutputRoot.Value}/{recording.FileName.Value}", Assert.Single(files.Read));
    }

    [Theory]
    [InlineData(TuningRefusal.NoSuchService)]
    [InlineData(TuningRefusal.NoSelectedChannel)]
    public async Task ARecordingOnAServiceNothingCanTuneAnyMoreIsWeighedAgainstTheRangeEveryKindFallsIn(
        TuningRefusal refusal)
    {
        Recording read = await Judged(
            TimeSpan.FromMinutes(30),
            2_400_000_000,
            asked: true,
            tuning: TuningResolution.Refused(refusal));

        Assert.Equal(RecordingOutcome.Complete, read.Outcome);
        Assert.Empty(read.OutcomeDetail);
        Assert.Equal(2_400_000_000, read.FileSizeObserved);
    }

    [Fact]
    public async Task AFileTooLightForEveryKindIsStillCutShortWhenTheServiceCannotBeTunedAnyMore()
    {
        Recording read = await Judged(
            TimeSpan.FromMinutes(30),
            2_000_000_000,
            asked: true,
            tuning: TuningResolution.Refused(TuningRefusal.NoSelectedChannel));

        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Equal(RecordingFault.LighterThanTheStream, Assert.Single(read.OutcomeDetail).Fault);
    }

    [Theory]
    [InlineData(TuningRefusal.NoTunerForSystem)]
    [InlineData(TuningRefusal.CapacityUnknown)]
    [InlineData(TuningRefusal.LedgerUnreadable)]
    public async Task ARecordingWhoseTunerIsUnavailableIsStillWeighedAgainstTheKindItsChannelIsCarriedOn(
        TuningRefusal refusal)
    {
        TuningResolution unavailable = TuningResolution.Refused(refusal, TuningParameters.Terrestrial(27));

        Recording light = await Judged(TimeSpan.FromMinutes(30), 2_480_000_000, asked: true, tuning: unavailable);
        Recording right = await Judged(TimeSpan.FromMinutes(30), 3_400_000_000, asked: true, tuning: unavailable);

        Assert.Equal(RecordingOutcome.Truncated, light.Outcome);
        Assert.Equal(RecordingFault.LighterThanTheStream, Assert.Single(light.OutcomeDetail).Fault);
        Assert.Equal(RecordingOutcome.Complete, right.Outcome);
    }

    [Theory]
    [InlineData(TuningRefusal.NoTunerForSystem)]
    [InlineData(TuningRefusal.CapacityUnknown)]
    [InlineData(TuningRefusal.LedgerUnreadable)]
    public async Task ARecordingWhoseServiceCannotBeAnsweredForYetIsLeftInFlightForTheNextPass(TuningRefusal refusal)
    {
        Recording recording = Ready(TimeSpan.FromMinutes(30), asked: true);
        var ledger = new StreamLedger();
        ledger.Hold(recording);

        RecordingWatch watch = await Supervisor(
                ledger,
                Concluded(recording),
                new WatchClock(Ended),
                new WeighedFiles { Weighs = 3_400_000_000 },
                tuning: TuningResolution.Refused(refusal))
            .WatchAsync(Cancel);

        Assert.Equal(0, watch.Settled);
        Assert.Empty(ledger.Saved);
        Assert.True(ledger.Read(recording.Id).IsInFlight);
    }

    [Fact]
    public async Task ARecordingStoppedByHandKeepsTheReasonItWasGivenAndGainsTheOnesTheJudgementFound()
    {
        Recording recording = InFlight();
        recording.Wrote(TimeSpan.FromSeconds(1750));
        recording.Note(new OutcomeDetail(RecordingFault.StoppedByHand, null, "the wrong programme", Airs.AddMinutes(29)));
        recording.Abort(Airs.AddMinutes(29));
        var ledger = new StreamLedger();
        ledger.Hold(recording);

        await Supervisor(
                ledger,
                Concluded(recording),
                new WatchClock(Ended),
                new WeighedFiles { Weighs = 3_300_000_000 })
            .WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);

        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Equal(
            [RecordingFault.StoppedByHand, RecordingFault.ShortOfTheWindow],
            read.OutcomeDetail.Select(detail => detail.Fault).ToArray());
        Assert.Equal("the wrong programme", read.OutcomeDetail[0].Note);
    }

    private static WatchedDriver Concluded(
        Recording recording,
        SessionStopReason reason = SessionStopReason.EndTimeReached,
        DateTime? endsAt = null)
    {
        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Over(recording, reason, endsAt: endsAt);

        return driver;
    }

    private static Recording Ready(TimeSpan written, bool asked)
    {
        Recording recording = InFlight();

        if (written > TimeSpan.Zero)
        {
            recording.Wrote(written);
        }

        if (asked)
        {
            recording.Abort(Airs.AddMinutes(30));
        }

        return recording;
    }

    private static async Task<Recording> Judged(
        TimeSpan written,
        long? weighs,
        bool asked,
        TuningResolution? tuning = null,
        SessionStopReason reason = SessionStopReason.EndTimeReached,
        DateTime? endsAt = null)
    {
        Recording recording = Ready(written, asked);
        var ledger = new StreamLedger();
        ledger.Hold(recording);

        await Supervisor(
                ledger,
                Concluded(recording, reason, endsAt),
                new WatchClock(Ended),
                new WeighedFiles { Weighs = weighs },
                tuning: tuning)
            .WatchAsync(Cancel);

        return ledger.Read(recording.Id);
    }
}
