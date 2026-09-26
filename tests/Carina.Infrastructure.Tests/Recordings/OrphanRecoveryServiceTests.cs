using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Recordings.RecordingStreamFixture;

using EventId = Carina.Domain.Programmes.EventId;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class OrphanRecoveryServiceTests
{
    private const long WhatLandedBeforeTheDiskFilled = 4_096_000;

    private static readonly DateTime Now = Airs.AddMinutes(10);

    private static readonly ProgrammeId Broadcast =
        new(new NetworkId(32736), new ServiceId(1025), new EventId(9));

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ARecordingTheDriverIsStillWritingIsTakenBackUpAndNothingIsAskedOfIt()
    {
        var ledger = new StreamLedger();
        Recording running = InFlight(Airs, Airs.AddMinutes(30), deviceId: null);
        ledger.Hold(running);

        var driver = new WatchedDriver();
        OrphanRecovered recovered = await Recovery(ledger, driver, new WatchClock(Now))
            .RecoverAsync(Hello(), [Writing(running)], Cancel);

        Assert.Equal(new OrphanRecovered(1, 1, 0, 0, 0), recovered);
        Assert.Empty(driver.Started);
        Assert.Empty(driver.Stopped);
        Assert.True(ledger.Read(running.Id).IsInFlight);
        Assert.Equal("adapter1", ledger.Read(running.Id).TunerDeviceId?.Value);
    }

    [Fact]
    public async Task ARecordingLeftRunningWhileItsBroadcastCarriesOnGoesBackOntoAStream()
    {
        var ledger = new StreamLedger();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        ledger.Hold(running);

        var driver = new WatchedDriver { WhenStarted = Live(running, Now) };
        OrphanRecovered recovered = await Recovery(ledger, driver, new WatchClock(Now))
            .RecoverAsync(Hello(), [], Cancel);

        Assert.Equal(new OrphanRecovered(1, 0, 1, 0, 0), recovered);

        StartSessionRequest asked = Assert.Single(driver.Started);

        Assert.Equal(RecordingSessions.Named(running.Id), asked.SessionId);
        Assert.Equal(running.Id.Wire, asked.RecordingId);
        Assert.Equal(running.OutputRoot.Value, asked.OutputRoot);
        Assert.Equal(SessionPurpose.Recording, asked.Purpose);

        Recording carried = ledger.Read(running.Id);

        Assert.True(carried.IsInFlight);
        Assert.Equal(1, carried.ResumeCount);
        Assert.Equal(
            RecordingFault.LeftRunningUnwatched,
            Assert.Single(carried.Interruptions).Fault);
        Assert.False(Assert.Single(carried.Interruptions).IsOpen);
    }

    [Fact]
    public async Task ARecordingWhoseBroadcastIsOverIsMarkedForWhatWasLeftOfIt()
    {
        var ledger = new StreamLedger();
        Recording running = InFlight(Airs, Now.AddMinutes(-1));
        ledger.Hold(running);

        var driver = new WatchedDriver();
        OrphanRecovered recovered = await Recovery(
                ledger,
                driver,
                new WatchClock(Now),
                new WeighedFiles { Weighs = 900_000_000 })
            .RecoverAsync(Hello(), [], Cancel);

        Assert.Equal(new OrphanRecovered(1, 0, 0, 1, 0), recovered);

        Recording ended = ledger.Read(running.Id);

        Assert.Equal(RecordingOutcome.Truncated, ended.Outcome);
        Assert.Equal(900_000_000, ended.FileSizeObserved);
        Assert.Equal(
            [RecordingFault.LeftRunningUnwatched],
            ended.OutcomeDetail.Select(detail => detail.Fault).ToArray());
        Assert.Empty(driver.Started);
        Assert.Empty(driver.Stopped);
    }

    [Fact]
    public async Task ARecordingThisSideHadAlreadyAskedToStopIsStillNeverCalledComplete()
    {
        var ledger = new StreamLedger();
        Recording running = InFlight(Airs, Now.AddMinutes(-1));

        running.Abort(Now.AddMinutes(-1));
        ledger.Hold(running);

        OrphanRecovered recovered = await Recovery(
                ledger,
                new WatchedDriver(),
                new WatchClock(Now),
                new WeighedFiles { Weighs = 900_000_000 })
            .RecoverAsync(Hello(), [], Cancel);

        Assert.Equal(1, recovered.Marked);

        RecordingOutcome? outcome = ledger.Read(running.Id).Outcome;

        Assert.NotEqual(RecordingOutcome.Complete, outcome);
        Assert.Contains(outcome!.Value, OrphanRecovery.OutcomesItCanWrite);
    }

    [Fact]
    public async Task ARecordingWhoseFileHoldsNothingIsAFailureThatSaysNothingLanded()
    {
        var ledger = new StreamLedger();
        Recording running = InFlight(Airs, Now.AddMinutes(-1));
        ledger.Hold(running);

        await Recovery(ledger, new WatchedDriver(), new WatchClock(Now), new WeighedFiles { Weighs = 0 })
            .RecoverAsync(Hello(), [], Cancel);

        Recording ended = ledger.Read(running.Id);

        Assert.Equal(RecordingOutcome.Failed, ended.Outcome);
        Assert.Equal(
            [RecordingFault.LeftRunningUnwatched, RecordingFault.NothingLanded],
            ended.OutcomeDetail.Select(detail => detail.Fault).ToArray());
    }

    [Fact]
    public async Task ARecordingWhoseFileCouldNotBeWeighedSaysThatTooAndIsAFailure()
    {
        var ledger = new StreamLedger();
        Recording running = InFlight(Airs, Now.AddMinutes(-1));
        ledger.Hold(running);

        await Recovery(ledger, new WatchedDriver(), new WatchClock(Now), new WeighedFiles { Weighs = null })
            .RecoverAsync(Hello(), [], Cancel);

        Recording ended = ledger.Read(running.Id);

        Assert.Equal(RecordingOutcome.Failed, ended.Outcome);
        Assert.Equal(
            [RecordingFault.LeftRunningUnwatched, RecordingFault.SizeUnobserved],
            ended.OutcomeDetail.Select(detail => detail.Fault).ToArray());
    }

    [Fact]
    public async Task ADriverThatCameBackAsAnotherInstanceIsToldApartFromOneWhoseSessionWentAway()
    {
        var ledger = new StreamLedger();
        Recording first = InFlight(Airs, Airs.AddMinutes(30));
        ledger.Hold(first);

        var driver = new WatchedDriver();
        OrphanRecoveryService recovery = Recovery(
            ledger,
            driver,
            new WatchClock(Now),
            new WeighedFiles { Weighs = 900_000_000 });

        await recovery.RecoverAsync(Hello(), [Writing(first)], Cancel);

        Recording second = InFlight(Airs, Now.AddMinutes(-1));
        ledger.Hold(second);

        await recovery.RecoverAsync(Hello("driver-2"), [], Cancel);

        Assert.Equal(
            RecordingFault.DriverReplaced,
            ledger.Read(second.Id).OutcomeDetail.Select(detail => detail.Fault).First());
    }

    [Fact]
    public async Task ABroadcastTheGuideNoLongerAnnouncesIsNotPutBackOntoAStream()
    {
        var ledger = new StreamLedger();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        ledger.Hold(running);

        var driver = new WatchedDriver();
        OrphanRecovered recovered = await Recovery(
                ledger,
                driver,
                new WatchClock(Now),
                new WeighedFiles { Weighs = 900_000_000 },
                Withdrawn())
            .RecoverAsync(Hello(), [], Cancel);

        Assert.Equal(new OrphanRecovered(1, 0, 0, 1, 0), recovered);
        Assert.Empty(driver.Started);
        Assert.Equal(RecordingOutcome.Truncated, ledger.Read(running.Id).Outcome);
    }

    [Fact]
    public async Task ARecordingTheDriverWillNotTakeBackStaysInterruptedAndInFlight()
    {
        var ledger = new StreamLedger();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        ledger.Hold(running);

        var driver = new WatchedDriver();
        OrphanRecovered recovered = await Recovery(ledger, driver, new WatchClock(Now))
            .RecoverAsync(Hello(), [], Cancel);

        Assert.Equal(new OrphanRecovered(1, 0, 0, 0, 1), recovered);

        Recording waiting = ledger.Read(running.Id);

        Assert.True(waiting.IsInFlight);
        Assert.True(Assert.Single(waiting.Interruptions).IsOpen);
        Assert.Empty(driver.Stopped);
    }

    [Fact]
    public async Task ARecordingOnAServiceNothingCanTuneAnyMoreIsLeftWhereItIs()
    {
        var ledger = new StreamLedger();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        ledger.Hold(running);

        var driver = new WatchedDriver();
        OrphanRecovered recovered = await Recovery(
                ledger,
                driver,
                new WatchClock(Now),
                tuning: TuningResolution.Refused(TuningRefusal.NoSelectedChannel))
            .RecoverAsync(Hello(), [], Cancel);

        Assert.Equal(new OrphanRecovered(1, 0, 0, 0, 1), recovered);
        Assert.Empty(driver.Started);
        Assert.True(ledger.Read(running.Id).IsInFlight);
    }

    [Fact]
    public async Task ARecordingThatAlreadyEndedIsNotOneRecoveryHasAnythingToSayAbout()
    {
        var ledger = new StreamLedger();
        OrphanRecovered recovered = await Recovery(ledger, new WatchedDriver(), new WatchClock(Now))
            .RecoverAsync(Hello(), [], Cancel);

        Assert.Equal(OrphanRecovered.Nothing, recovered);
    }

    [Fact]
    public async Task ASessionTheDriverHasAlreadyConcludedIsNotOneToTakeBackUp()
    {
        var ledger = new StreamLedger();
        Recording running = InFlight(Airs, Now.AddMinutes(-1));
        ledger.Hold(running);

        var driver = new WatchedDriver();
        OrphanRecovered recovered = await Recovery(
                ledger,
                driver,
                new WatchClock(Now),
                new WeighedFiles { Weighs = 900_000_000 })
            .RecoverAsync(Hello(), [Concluded(running)], Cancel);

        Assert.Equal(new OrphanRecovered(1, 0, 0, 1, 0), recovered);
    }

    [Fact]
    public async Task ARecordingTheDriverStoppedAtTheEndItWasOpenedWithIsLeftForThePassThatJudgesIt()
    {
        var ledger = new StreamLedger();
        DateTime told = Now.AddMinutes(-1);
        Recording running = InFlight(Airs, told);

        running.Wrote(told - Airs);
        ledger.Hold(running);

        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(running.Id)] = Over(running, endsAt: told);
        var files = new WeighedFiles { Weighs = 1_030_000_000 };

        OrphanRecovered recovered = await Recovery(ledger, driver, new WatchClock(Now), files)
            .RecoverAsync(
                Hello(),
                [Concluded(running) with { StopReason = SessionStopReason.EndTimeReached, EndsAt = told }],
                Cancel);

        Assert.Equal(new OrphanRecovered(1, 0, 0, 0, 0), recovered);
        Assert.True(ledger.Read(running.Id).IsInFlight);

        await Supervisor(ledger, driver, new WatchClock(Now), files).WatchAsync(Cancel);

        Recording read = ledger.Read(running.Id);

        Assert.Equal(RecordingOutcome.Complete, read.Outcome);
        Assert.Empty(read.OutcomeDetail);
        Assert.Equal(told, read.AbortedAt);
        Assert.Empty(driver.Started);
    }

    [Theory]
    [InlineData(SessionStopReason.DeviceFailed)]
    [InlineData(SessionStopReason.Preempted)]
    public async Task ASessionThatEndedForAnyOtherReasonIsStillMarkedForWhatWasLeftOfIt(SessionStopReason reason)
    {
        var ledger = new StreamLedger();
        Recording running = InFlight(Airs, Now.AddMinutes(-1));
        ledger.Hold(running);

        OrphanRecovered recovered = await Recovery(
                ledger,
                new WatchedDriver(),
                new WatchClock(Now),
                new WeighedFiles { Weighs = 900_000_000 })
            .RecoverAsync(Hello(), [Concluded(running) with { StopReason = reason }], Cancel);

        Assert.Equal(new OrphanRecovered(1, 0, 0, 1, 0), recovered);
        Assert.Equal(RecordingOutcome.Truncated, ledger.Read(running.Id).Outcome);
    }

    [Fact]
    public async Task OneRecordingThatCannotBeTakenBackDoesNotStopTheNextFromBeing()
    {
        var ledger = new StreamLedger();
        Recording awkward = InFlight(Airs, Now.AddMinutes(-2), eventId: 9);
        Recording ordinary = InFlight(Airs, Now.AddMinutes(-1), eventId: 11);

        ledger.Hold(awkward, ordinary);
        ledger.AfterFinding = () => throw new InvalidOperationException("The ledger would not answer.");

        OrphanRecovered recovered = await Recovery(
                ledger,
                new WatchedDriver(),
                new WatchClock(Now),
                new WeighedFiles { Weighs = 900_000_000 })
            .RecoverAsync(Hello(), [], Cancel);

        Assert.Equal(2, recovered.Found);
        Assert.Equal(1, recovered.Marked);
    }

    [Fact]
    public async Task BR_KD_004_ARecordingWhoseDiskFilledIsNotPutBackOnAStreamByRecovery()
    {
        var ledger = new StreamLedger();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        ledger.Hold(running);

        var driver = new WatchedDriver { WhenStarted = Live(running, Now) };
        OrphanRecovered recovered = await Recovery(
                ledger,
                driver,
                new WatchClock(Now),
                new WeighedFiles { Weighs = WhatLandedBeforeTheDiskFilled })
            .RecoverAsync(Hello(), [FilledTheDisk(running)], Cancel);

        Recording read = ledger.Read(running.Id);

        Assert.Empty(driver.Started);
        Assert.Equal(1, recovered.Marked);
        Assert.False(read.IsInFlight);
        Assert.Equal(RecordingOutcome.Failed, read.Outcome);
        Assert.Equal(RecordingFault.DiskExhausted, Assert.Single(read.OutcomeDetail).Fault);
        Assert.Equal(WhatLandedBeforeTheDiskFilled, read.FileSizeObserved);
        Assert.Empty(read.Interruptions);
    }

    [Fact]
    public async Task ADiskThatFilledIsNamedEvenWhenTheBroadcastWasOverByTheTimeRecoveryLooked()
    {
        var ledger = new StreamLedger();
        Recording running = InFlight(Airs, Now.AddMinutes(-1));
        ledger.Hold(running);

        var driver = new WatchedDriver();

        await Recovery(
                ledger,
                driver,
                new WatchClock(Now),
                new WeighedFiles { Weighs = WhatLandedBeforeTheDiskFilled })
            .RecoverAsync(Hello(), [FilledTheDisk(running)], Cancel);

        Recording read = ledger.Read(running.Id);

        Assert.Empty(driver.Started);
        Assert.Equal(RecordingOutcome.Failed, read.Outcome);
        Assert.Equal(RecordingFault.DiskExhausted, Assert.Single(read.OutcomeDetail).Fault);
    }

    [Theory]
    [InlineData(SessionStopReason.RecordingFailed, null)]
    [InlineData(SessionStopReason.DeviceFailed, SessionRefusalTitles.DiskFull)]
    public async Task ASessionThatDidNotEndOnAFullDiskIsStillOneRecoveryPutsBackOnAStream(
        SessionStopReason reason,
        string? title)
    {
        var ledger = new StreamLedger();
        Recording running = InFlight(Airs, Airs.AddMinutes(30));
        ledger.Hold(running);

        var driver = new WatchedDriver { WhenStarted = Live(running, Now) };

        await Recovery(ledger, driver, new WatchClock(Now))
            .RecoverAsync(
                Hello(),
                [
                    Concluded(running) with
                    {
                        State = SessionState.Failed,
                        StopReason = reason,
                        FailureTitle = title,
                    },
                ],
                Cancel);

        Recording read = ledger.Read(running.Id);

        Assert.NotEmpty(driver.Started);
        Assert.True(read.IsInFlight);
        Assert.Equal(RecordingFault.LeftRunningUnwatched, Assert.Single(read.Interruptions).Fault);
    }

    private static SessionSnapshot Writing(Recording recording)
        => new(
            RecordingSessions.Named(recording.Id),
            SessionPurpose.Recording,
            "adapter1",
            SessionState.Active,
            Airs)
        {
            RecordingId = recording.Id.Wire,
            OutputRoot = recording.OutputRoot.Value,
        };

    private static SessionSnapshot FilledTheDisk(Recording recording)
        => Concluded(recording) with
        {
            State = SessionState.Failed,
            StopReason = SessionStopReason.RecordingFailed,
            FailureTitle = SessionRefusalTitles.DiskFull,
        };

    private static SessionSnapshot Concluded(Recording recording)
        => Writing(recording) with
        {
            State = SessionState.Stopped,
            Concluded = true,
        };

    private static DriverHello Hello(string instanceId = "driver-1")
        => new(DriverProtocol.Version, instanceId, [DriverCapabilities.Recording]);

    private static HeldProgrammes Withdrawn()
    {
        var guide = new HeldProgrammes();
        Programme elsewhere = Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(Broadcast.NetworkId, Broadcast.ServiceId, new EventId(10)),
                new TransportStreamId(32736),
                Airs.AddHours(2),
                Airs.AddHours(3),
                "The next programme",
                string.Empty,
                false),
            Airs);

        elsewhere.Heard(Airs);
        guide.Programmes.Add(elsewhere);

        return guide;
    }
}
