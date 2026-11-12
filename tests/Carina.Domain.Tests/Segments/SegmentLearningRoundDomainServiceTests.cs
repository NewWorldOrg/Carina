using Carina.Domain.Segments;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentLearningRoundDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Now = new(2026, 11, 1, 18, 30, 0, DateTimeKind.Utc);

    private static readonly SegmentEvaluation Evaluated = new(
        new SegmentMetrics(0.9, 0.8, [new KindMetrics(SegmentKind.Break, 0.9)], null, 0),
        [],
        new EvaluationEvidence(12, 148, Now.AddHours(-6)));

    private readonly HeldSegmentLearningRounds rounds = new();

    private readonly HeldLearningMaterialChanges changes = new();

    private SegmentLearningRoundDomainService Service => new(rounds, changes);

    [Fact(DisplayName = "BR-CD-004: the day's round is due until a round of that day is kept, failed included, and the next day's is due again")]
    public async Task TheDaysRoundIsDueOnce()
    {
        DateOnly today = SegmentLearningRound.DayOf(Now);

        Assert.True(await Service.IsDueAsync(today, Cancel));

        await rounds.TakeAsync(SegmentLearningRound.Ended(today, Now, SegmentTrainingOutcome.Failed), Cancel);

        Assert.False(await Service.IsDueAsync(today, Cancel));
        Assert.True(await Service.IsDueAsync(today.AddDays(1), Cancel));
    }

    [Fact(DisplayName = "BR-CD-004: a round has something new to learn from while no round kept a candidate, and later only when what learning reads changed after the last round that kept one")]
    public async Task ARoundHasNewsOnlyAfterAChange()
    {
        Assert.True(await Service.HasNewsAsync(Cancel));

        await rounds.TakeAsync(SegmentLearningRound.Ended(SegmentLearningRound.DayOf(Now), Now, SegmentTrainingOutcome.Kept, 2, AdoptionVerdict.NoBetterAtBoundaries), Cancel);
        changes.LastChanged = Now.AddMinutes(-1);

        Assert.False(await Service.HasNewsAsync(Cancel));

        changes.LastChanged = Now.AddMinutes(1);

        Assert.True(await Service.HasNewsAsync(Cancel));
    }

    [Theory(DisplayName = "BR-CD-004: a round that stopped partway is not kept, and every other round is")]
    [InlineData(SegmentTrainingOutcome.Stopped, false)]
    [InlineData(SegmentTrainingOutcome.NothingNew, true)]
    [InlineData(SegmentTrainingOutcome.NothingToLearnFrom, true)]
    [InlineData(SegmentTrainingOutcome.Kept, true)]
    [InlineData(SegmentTrainingOutcome.Adopted, true)]
    [InlineData(SegmentTrainingOutcome.Failed, true)]
    public void ARoundStoppedPartwayIsNotKept(SegmentTrainingOutcome outcome, bool kept)
    {
        Assert.Equal(kept, SegmentLearningRound.IsKept(outcome));

        if (!kept)
        {
            Assert.ThrowsAny<ArgumentException>(() => SegmentLearningRound.Ended(SegmentLearningRound.DayOf(Now), Now, outcome));
        }
    }

    [Theory(DisplayName = "BR-CD-008: a candidate judged on no recording had nothing to learn from and is not kept, and one judged on any is")]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(12, true)]
    public void ACandidateJudgedOnNoRecordingIsNotKept(int recordings, bool kept)
    {
        SegmentEvaluation judged = Evaluated with { Evidence = new EvaluationEvidence(recordings, recordings * 10, Now) };

        Assert.Equal(kept, new SegmentLearned(HandMadeModels.First, judged, Evaluated, AdoptionVerdict.Adopted).IsKept);
    }
}
