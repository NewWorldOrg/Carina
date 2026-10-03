using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;

using static Carina.Infrastructure.Tests.Recordings.RecordingStreamFixture;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingStreamEndingCountTests
{
    private const long Packets = 1_000_000;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Ended = Airs.AddMinutes(31);

    private static readonly SessionCounters WhatTheSessionEndedWith = new(
        Packets: Packets + 50_000,
        Drops: 7,
        ScrambledPackets: 9,
        DeviceOverflows: 2,
        CcMeasured: true,
        ScrambleMeasured: true);

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
}
