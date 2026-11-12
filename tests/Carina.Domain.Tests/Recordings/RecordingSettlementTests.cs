using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingSettlementTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    private static readonly DateTime Later = Now.AddMinutes(30);

    private static readonly QualityBands AsShipped = QualityThresholdStanding.Bands(QualityThresholdStanding.Over([], Now));

    public static TheoryData<long?> EverySize => new(null, 0L, 1L, 4_096_000L);

    [Theory(DisplayName = "BR-KD-010: a recording whose disk filled fails, naming the full disk and what its file weighed, and keeps the size it was weighed at")]
    [MemberData(nameof(EverySize))]
    public void ARecordingWhoseDiskFilledFails(long? weighed)
    {
        Recording recording = RecordingFactory.Started();

        recording.FailOnAFullDisk(weighed, Later);

        Assert.Equal(RecordingOutcome.Failed, recording.Outcome);
        Assert.Equal(RecordingFaults.OfAFullDisk(weighed), recording.OutcomeDetail.Select(detail => detail.Fault));
        Assert.All(recording.OutcomeDetail, detail => Assert.Equal(Later, detail.NoticedAt));
        Assert.Equal(weighed ?? 0, recording.FileSizeObserved);
        Assert.Equal(Later, recording.StoppedAtActual);
    }

    [Theory(DisplayName = "BR-KS-002: a recording nothing was writing ends on what its file holds, naming why it ended where it did")]
    [InlineData(false, 4_096_000L)]
    [InlineData(true, 4_096_000L)]
    [InlineData(false, 0L)]
    [InlineData(true, null)]
    public void ARecordingNothingWasWritingEndsOnWhatItsFileHolds(bool another, long? weighed)
    {
        Recording recording = RecordingFactory.Started();

        recording.SettleOnWhatWasLeft(another, weighed, AsShipped, Later);

        Assert.Equal(OrphanRecovery.WhatIsLeftOf(weighed), recording.Outcome);
        Assert.Equal(
            OrphanRecovery.WhyItEndedWhereItDid(another, weighed, QualityLevel.Unmeasured),
            recording.OutcomeDetail.Select(detail => detail.Fault));
        Assert.Equal(weighed ?? 0, recording.FileSizeObserved);
    }

    [Fact(DisplayName = "BR-KS-002: a recording nothing was writing that was left scrambled says so as it ends")]
    public void ARecordingLeftScrambledSaysSoAsItEnds()
    {
        Recording recording = RecordingFactory.Started();
        recording.Measure(DropCounters.Counted(0, 1_000_000), DropTimeline.Unlocated, 900_000, 0, Now.AddMinutes(1));

        recording.SettleOnWhatWasLeft(false, 4_096_000, AsShipped, Later);

        Assert.Contains(RecordingFault.ScramblingUnresolved, recording.OutcomeDetail.Select(detail => detail.Fault));
        Assert.Equal(RecordingOutcome.Truncated, recording.Outcome);
    }

    [Theory(DisplayName = "BR-KD-002: a recording ends with what the completion judgement says of its own window, length, stop and file")]
    [InlineData(true, 1_200_000_000L, 55)]
    [InlineData(true, 1_000L, 55)]
    [InlineData(false, 1_200_000_000L, 55)]
    [InlineData(true, null, 55)]
    [InlineData(true, 0L, 5)]
    [InlineData(true, 600_000_000L, 20)]
    public void ARecordingEndsWithWhatTheJudgementSays(bool askedToStop, long? weighed, int minutesWritten)
    {
        Recording recording = RecordingFactory.Started();
        recording.Wrote(TimeSpan.FromMinutes(minutesWritten));

        if (askedToStop)
        {
            recording.Abort(recording.ExpectedWindowEnd);
        }

        RecordingVerdict expected = CompletionEvaluator.Judge(
            new RecordingEvidence(
                weighed,
                recording.Written,
                recording.ExpectedWindowStart,
                recording.ExpectedWindowEnd,
                recording.AbortedAt,
                QualityLevel.Unmeasured),
            ExpectedBitrate.Terrestrial,
            CompletionTolerance.Default);

        recording.Conclude(weighed, ExpectedBitrate.Terrestrial, AsShipped, Later);

        Assert.Equal(expected.Outcome, recording.Outcome);
        Assert.Equal(expected.Detail(Later), recording.OutcomeDetail);
        Assert.Equal(weighed ?? 0, recording.FileSizeObserved);
    }

    [Fact(DisplayName = "BR-KD-002: a recording judged complete on its own evidence is complete only because this side asked it to stop")]
    public void OnlyARecordingAskedToStopIsJudgedComplete()
    {
        Recording asked = RecordingFactory.Started();
        Recording unasked = RecordingFactory.Started();

        foreach (Recording recording in new[] { asked, unasked })
        {
            recording.Wrote(recording.ExpectedWindowEnd - recording.ExpectedWindowStart);
        }

        asked.Abort(asked.ExpectedWindowEnd);

        asked.Conclude(6_800_000_000, ExpectedBitrate.Terrestrial, AsShipped, Later);
        unasked.Conclude(6_800_000_000, ExpectedBitrate.Terrestrial, AsShipped, Later);

        Assert.Equal(RecordingOutcome.Complete, asked.Outcome);
        Assert.NotEqual(RecordingOutcome.Complete, unasked.Outcome);
    }

    [Fact(DisplayName = "BR-KS-001: a recording the driver ended at the end it was holding is read as stopped there by this side")]
    public void ARecordingEndedAtItsHeldEndIsReadAsStoppedThere()
    {
        Recording recording = RecordingFactory.Started();
        DateTime held = Now.AddMinutes(20);

        recording.ReachedItsEnd(held, Later);

        Assert.Equal(held, recording.AbortedAt);
    }

    [Fact(DisplayName = "BR-KS-001: an end held later than now, or none at all, is read as reached now")]
    public void AnEndHeldLaterThanNowOrNoneIsReadAsReachedNow()
    {
        Recording later = RecordingFactory.Started();
        Recording unnamed = RecordingFactory.Started();

        later.ReachedItsEnd(Later.AddMinutes(5), Later);
        unnamed.ReachedItsEnd(null, Later);

        Assert.Equal(Later, later.AbortedAt);
        Assert.Equal(Later, unnamed.AbortedAt);
    }

    [Fact(DisplayName = "BR-KS-001: an end held before the recording began is no end this recording reached, so nobody asked for it to stop")]
    public void AnEndBeforeTheRecordingBeganIsNotOneItReached()
    {
        Recording recording = RecordingFactory.Started();

        recording.ReachedItsEnd(recording.StartedAtActual.AddTicks(-1), Later);

        Assert.Null(recording.AbortedAt);

        recording.ReachedItsEnd(recording.StartedAtActual, Later);

        Assert.Equal(recording.StartedAtActual, recording.AbortedAt);
    }

    [Fact(DisplayName = "BR-KD-006: a stop this side already wrote down stands when the driver later says it reached its end")]
    public void AStopAlreadyWrittenDownStands()
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(Now.AddMinutes(10));

        recording.ReachedItsEnd(Now.AddMinutes(20), Later);

        Assert.Equal(Now.AddMinutes(10), recording.AbortedAt);
    }
}
