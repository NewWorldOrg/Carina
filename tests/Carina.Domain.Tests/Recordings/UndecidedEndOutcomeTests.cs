using Carina.Contracts;
using Carina.Domain.Recordings;

using static Carina.Domain.Tests.Recordings.RecordingFactory;

namespace Carina.Domain.Tests.Recordings;

public sealed class UndecidedEndOutcomeTests
{
    [Fact]
    public void ARecordingCarriedOnAHorizonSaysSoWhileItIsStillRunning()
    {
        Recording recording = Started();

        recording.Note(new OutcomeDetail(RecordingFault.EndStillUndecided, null, string.Empty, Now));

        Assert.Equal(
            RecordingFault.EndStillUndecided,
            Assert.Single(recording.OutcomeDetail).Fault);
    }

    [Fact]
    public void AWindowThatRanOutWhileTheEndWasUndecidedStillSaysSoAfterwards()
    {
        Recording recording = Started();

        recording.Note(new OutcomeDetail(RecordingFault.EndStillUndecided, null, string.Empty, Now));
        recording.Note(new OutcomeDetail(RecordingFault.StoppedUnasked, null, string.Empty, Now));
        recording.Settle(RecordingOutcome.Truncated, 4_000_000L, Now.AddMinutes(50));

        Assert.Equal(RecordingOutcome.Truncated, recording.Outcome);
        Assert.Contains(
            recording.OutcomeDetail,
            detail => detail.Fault is RecordingFault.EndStillUndecided);
    }

    [Fact]
    public void ARecordingTheDriverStoppedOnItsOwnIsNotCalledComplete()
    {
        RecordingVerdict verdict = CompletionEvaluator.Judge(
            new RecordingEvidence(
                4_000_000_000L,
                TimeSpan.FromMinutes(60),
                Now,
                Now.AddMinutes(60),
                null),
            ExpectedBitrate.Of(TunerKind.Terrestrial),
            CompletionTolerance.Default);

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.Contains(RecordingFault.StoppedUnasked, verdict.Faults);
    }

    [Fact]
    public void AnEndNobodyAnnouncedIsNotSomethingThatBreaksARunningRecording()
        => Assert.DoesNotContain(RecordingFault.EndStillUndecided, RecordingFaults.ThatCanInterrupt);

    [Fact]
    public void AnEndNobodyAnnouncedSaysNothingAboutWhichTunerWasUsed()
        => Assert.DoesNotContain(RecordingFault.EndStillUndecided, RecordingFaults.ThatReachedTheTuner);
}
