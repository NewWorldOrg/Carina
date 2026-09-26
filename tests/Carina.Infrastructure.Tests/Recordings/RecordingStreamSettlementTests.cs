using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;

using static Carina.Infrastructure.Tests.Recordings.RecordingStreamFixture;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingStreamSettlementTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Ended = Airs.AddMinutes(31);

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
    public async Task ARecordingThatCoveredFarTooLittleOfTheWindowFailedRatherThanRanShort()
    {
        Recording read = await Judged(TimeSpan.FromSeconds(1700), 3_200_000_000, asked: true);

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
    [InlineData(TuningRefusal.NoTunerForSystem)]
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
    [InlineData(TuningRefusal.LedgerUnreadable)]
    [InlineData(TuningRefusal.CapacityUnknown)]
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
