using Carina.Contracts;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;

using static Carina.Infrastructure.Tests.Recordings.RecordingStreamFixture;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingStreamDiskFullTests
{
    private const long WhatLandedBeforeTheDiskFilled = 4_096_000;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task BR_KD_004_ARecordingWhoseDiskFilledFailsThereRatherThanBeingOpenedAgain()
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
}
