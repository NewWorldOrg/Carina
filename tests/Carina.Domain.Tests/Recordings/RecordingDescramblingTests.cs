using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingDescramblingTests
{
    private static readonly DateTime Ended = RecordingFactory.Now.AddHours(1);

    private static readonly DateTime Later = Ended.AddDays(2);

    [Theory]
    [InlineData(RecordingOutcome.Complete)]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public void ARecordingThatEndedWithItsScramblingUnresolvedIsLeftScrambled(RecordingOutcome outcome)
    {
        Recording recording = Scrambled(outcome);

        Assert.True(recording.LeftScrambled);
        Assert.Null(recording.DescrambledAt);
    }

    [Fact]
    public void ARecordingThatNamesNoScramblingIsNotLeftScrambled()
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(Ended);
        recording.Settle(RecordingOutcome.Complete, 1_200_000, Ended);

        Assert.False(recording.LeftScrambled);
    }

    [Fact]
    public void ARecordingStillBeingWrittenIsNotYetLeftScrambled()
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault(RecordingFault.ScramblingUnresolved));

        Assert.False(recording.LeftScrambled);
    }

    [Theory]
    [InlineData(RecordingOutcome.Complete)]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public void DescramblingLiftsWhatWasLeftScrambledAndSaysWhen(RecordingOutcome outcome)
    {
        Recording recording = Scrambled(outcome);

        recording.Descrambled(Later);

        Assert.False(recording.LeftScrambled);
        Assert.Equal(Later, recording.DescrambledAt);
    }

    [Fact]
    public void DescramblingKeepsTheOutcomeAndTheClassificationItEndedWith()
    {
        Recording recording = Scrambled(RecordingOutcome.Complete);

        recording.Descrambled(Later);

        Assert.Equal(RecordingOutcome.Complete, recording.Outcome);
        Assert.Contains(
            recording.OutcomeDetail,
            detail => detail.Fault is RecordingFault.ScramblingUnresolved);
    }

    [Fact]
    public void ARecordingThatWasNeverLeftScrambledIsNotDescrambled()
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault());
        recording.Settle(RecordingOutcome.Truncated, 1_200_000, Ended);

        Assert.Throws<InvalidOperationException>(() => recording.Descrambled(Later));
        Assert.Null(recording.DescrambledAt);
    }

    [Fact]
    public void ARecordingStillBeingWrittenIsNotDescrambled()
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault(RecordingFault.ScramblingUnresolved));

        Assert.Throws<InvalidOperationException>(() => recording.Descrambled(Later));
    }

    [Fact]
    public void ARecordingIsDescrambledOnce()
    {
        Recording recording = Scrambled(RecordingOutcome.Complete);
        recording.Descrambled(Later);

        Assert.Throws<InvalidOperationException>(() => recording.Descrambled(Later.AddDays(1)));
        Assert.Equal(Later, recording.DescrambledAt);
    }

    [Fact]
    public void ARecordingIsNotDescrambledBeforeItEnded()
    {
        Recording recording = Scrambled(RecordingOutcome.Complete);

        Assert.Throws<ArgumentException>(() => recording.Descrambled(Ended.AddSeconds(-1)));
        Assert.Throws<ArgumentException>(
            () => recording.Descrambled(DateTime.SpecifyKind(Later, DateTimeKind.Local)));
        Assert.True(recording.LeftScrambled);
    }

    [Fact]
    public void ARowReadBackKeepsWhenItWasDescrambled()
    {
        Recording held = Scrambled(RecordingOutcome.Complete);

        Recording again = Again(held, Later);

        Assert.Equal(Later, again.DescrambledAt);
        Assert.False(again.LeftScrambled);
    }

    [Fact]
    public void ARowCannotSayItWasDescrambledUnlessItEndedLeftScrambledBeforeThat()
    {
        Recording unscrambled = RecordingFactory.Started();
        unscrambled.Note(RecordingFactory.Fault());
        unscrambled.Settle(RecordingOutcome.Truncated, 1_200_000, Ended);

        Assert.Throws<ArgumentException>(() => Again(unscrambled, Later));
        Assert.Throws<ArgumentException>(() => Again(RecordingFactory.Started(), Later));
        Assert.Throws<ArgumentException>(() => Again(Scrambled(RecordingOutcome.Complete), Ended.AddSeconds(-1)));
    }

    private static Recording Scrambled(RecordingOutcome outcome)
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault(RecordingFault.ScramblingUnresolved));

        if (outcome is RecordingOutcome.Complete)
        {
            recording.Abort(Ended);
        }

        recording.Settle(outcome, 1_200_000, Ended);

        return recording;
    }

    private static Recording Again(Recording held, DateTime? descrambledAt)
        => Recording.Rehydrate(
            held.Id,
            held.ReservationId,
            held.Programme,
            held.OutputRoot,
            held.FileName,
            held.FileSizeObserved,
            held.ObservedAt,
            held.StartedAtActual,
            held.StoppedAtActual,
            held.AbortedAt,
            held.WrittenDurationMs,
            held.ResumeCount,
            held.Interruptions,
            held.ExpectedWindowStart,
            held.ExpectedWindowEnd,
            held.PromisedWindowEnd,
            held.Outcome,
            held.OutcomeDetail,
            held.Counters,
            held.Positions,
            held.ScrambledPackets,
            held.EovfCount,
            held.MeasuredUpdatedAt,
            held.TunerDeviceId,
            held.ThumbnailState,
            RecordingFactory.Snapshot(),
            held.BroadcastGroupKey,
            held.BroadcastGroupRole,
            held.ThumbnailFault,
            held.LeftBehindAt,
            held.FilesLeftBehind,
            held.EncodeWhenRecorded,
            descrambledAt);
}
