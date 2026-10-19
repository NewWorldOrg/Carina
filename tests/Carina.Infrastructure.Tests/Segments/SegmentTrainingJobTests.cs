using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class SegmentTrainingJobTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly SegmentTrainingHarness harness = new();

    public void Dispose() => harness.Dispose();

    [Fact(DisplayName = "BR-CD-008: with nothing to learn from, the day's round keeps no version and leaves the version in use")]
    public async Task NothingToLearnFromKeepsNoVersion()
    {
        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal(SegmentTrainingOutcome.NothingToLearnFrom, look.Round?.Outcome);
        Assert.Empty(harness.Versions.Added);
        Assert.Empty(harness.Versions.Written);
        Assert.Equal(SegmentModelVersion.FirstNumber, harness.Versions.Active.Number);
        Assert.Equal([SegmentLearningRound.DayOf(SegmentTrainingHarness.Start)], harness.Rounds.Taken.Select(round => round.Day));
    }

    [Fact(DisplayName = "BR-CD-008: a candidate on enough evidence that finds more boundaries is kept and put in use in place of the version in use")]
    public async Task ABetterCandidateOnEnoughEvidenceIsPutInUse()
    {
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.93, 0.9);

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal((SegmentTrainingOutcome.Adopted, (AdoptionVerdict?)AdoptionVerdict.Adopted), (look.Round?.Outcome, look.Round?.Verdict));
        SegmentModelVersion kept = Assert.Single(harness.Versions.Added);
        Assert.Equal((2, SegmentModelVersionState.Candidate, harness.Now), (kept.Number, kept.State, kept.TrainedAt));
        Assert.Equal(harness.Trainer.Candidate.Metrics, kept.Metrics);
        Assert.Equal((2, SegmentModelVersionState.Active, false), (harness.Versions.Active.Number, harness.Versions.Active.State, harness.Versions.Active.ByHand));
        Assert.Equal([(1, SegmentModelVersionState.Retired), (2, SegmentModelVersionState.Active)], harness.Versions.Written.Select(version => (version.Number, version.State)));
        Assert.Equal([(SegmentModelVersion.FirstNumber, SegmentTrainingHarness.Start)], harness.Trainer.Asked);
    }

    [Theory(DisplayName = "BR-CD-008: a candidate that finds no more boundaries, as many with fewer seconds of adverts, or stands on too little is kept as a candidate only, beside the candidates before it")]
    [InlineData(0.80, 0.9, 12, AdoptionVerdict.NoBetterAtBoundaries)]
    [InlineData(0.85, 0.8, 12, AdoptionVerdict.NoBetterAtBoundaries)]
    [InlineData(0.93, 0.9, 9, AdoptionVerdict.TooLittleEvidence)]
    public async Task AWorseCandidateIsKeptOnly(double recall, double seconds, int recordings, AdoptionVerdict verdict)
    {
        harness.Versions.Others.Add(SegmentModelVersion.Learned(2, SegmentModel.First, SegmentTrainingHarness.Evaluated(0.5, 0.9), SegmentTrainingHarness.Start.AddDays(-1)));
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(recall, seconds, new EvaluationEvidence(recordings, 148, SegmentTrainingHarness.Start));

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal((SegmentTrainingOutcome.Kept, (AdoptionVerdict?)verdict), (look.Round?.Outcome, look.Round?.Verdict));
        Assert.Equal([3], harness.Versions.Added.Select(version => version.Number));
        Assert.Equal(
            [(1, SegmentModelVersionState.Active), (2, SegmentModelVersionState.Candidate), (3, SegmentModelVersionState.Candidate)],
            (await harness.Versions.ListAsync(Cancel)).OrderBy(version => version.Number).Select(version => (version.Number, version.State)));
        Assert.Empty(harness.Versions.Written);
    }

    [Fact(DisplayName = "BR-CD-008: while the version in use is chosen by hand, learning still keeps and evaluates a candidate but never puts it in use")]
    public async Task AVersionChosenByHandIsNeverReplaced()
    {
        harness.Versions.Others.Add(SegmentModelVersion.Learned(2, SegmentModel.First, SegmentTrainingHarness.Evaluated(0.5, 0.9), SegmentTrainingHarness.Start.AddDays(-2)));
        await harness.Versions.ChangeAsync(SegmentVersionChange.ChosenByHand(2), SegmentTrainingHarness.Start.AddDays(-1), Cancel);
        harness.Versions.Written.Clear();
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.99, 0.9);

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal((SegmentTrainingOutcome.Kept, (AdoptionVerdict?)AdoptionVerdict.ChosenByHand), (look.Round?.Outcome, look.Round?.Verdict));
        Assert.Equal((2, true), (harness.Versions.Active.Number, harness.Versions.Active.ByHand));
        Assert.Equal([3], harness.Versions.Added.Select(version => version.Number));
        Assert.Empty(harness.Versions.Written);
    }

    [Fact(DisplayName = "BR-CD-008: a better candidate is not put in use when the version in use is chosen by hand while it is learned")]
    public async Task AChoiceByHandWhileLearningKeepsTheCandidateOut()
    {
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.93, 0.9);
        harness.Trainer.WhileLearning = () => harness.Versions.ChangeAsync(SegmentVersionChange.ChosenByHand(SegmentModelVersion.FirstNumber), harness.Now, Cancel);

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal(SegmentTrainingOutcome.InUseChangedMeanwhile, look.Round?.Outcome);
        Assert.Equal((SegmentModelVersion.FirstNumber, true), (harness.Versions.Active.Number, harness.Versions.Active.ByHand));
        Assert.Equal(SegmentModelVersionState.Candidate, Assert.Single(harness.Versions.Added).State);
    }

    [Fact(DisplayName = "BR-CD-008: viewers coming back from more than one and a half times the share of skips within seven days roll the version in use back, kept on the version left, before the day's candidate is judged")]
    public async Task TooManyReturnsRollTheVersionInUseBack()
    {
        await harness.SecondInUseAsync(daysAgo: 3);
        DateTime adopted = harness.Versions.Active.AdoptedAt!.Value;
        await harness.SkippedAsync(2, adopted.AddHours(1), skips: 25, returned: 10);
        await harness.SkippedAsync(1, adopted.AddDays(-3), skips: 40, returned: 4);

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal(new SegmentRollback(0.4, 0.1, 25, 1), look.Round?.Rollback);
        Assert.Equal(SegmentModelVersion.FirstNumber, harness.Versions.Active.Number);
        SegmentModelVersion left = harness.Versions.Others.Single(version => version.Number == 2);
        Assert.Equal((SegmentModelVersionState.Retired, (DateTime?)harness.Now, new SegmentRollback(0.4, 0.1, 25, 1)), (left.State, left.RolledBackAt, left.Rollback));
        Assert.Equal([SegmentModelVersion.FirstNumber], harness.Trainer.Asked.Select(asked => asked.Active));
    }

    [Theory(DisplayName = "BR-CD-008: the version in use stays with no more than one and a half times the returns, fewer than twenty skips, or seven days on")]
    [InlineData(3, 20, 3)]
    [InlineData(3, 19, 19)]
    [InlineData(8, 25, 25)]
    public async Task TheVersionInUseStaysOtherwise(int daysAgo, int skips, int returned)
    {
        await harness.SecondInUseAsync(daysAgo);
        DateTime adopted = harness.Versions.Active.AdoptedAt!.Value;
        await harness.SkippedAsync(2, adopted.AddHours(1), skips, returned);
        await harness.SkippedAsync(1, adopted.AddDays(-3), skips: 40, returned: 4);

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Null(look.Round?.Rollback);
        Assert.Equal(2, harness.Versions.Active.Number);
        Assert.Empty(harness.Versions.Written);
    }

    [Fact(DisplayName = "BR-CD-008: a version chosen by hand is never rolled back, however many viewers came back")]
    public async Task AVersionChosenByHandIsNeverRolledBack()
    {
        await harness.SecondInUseAsync(daysAgo: 3);
        DateTime adopted = harness.Versions.Active.AdoptedAt!.Value;
        await harness.Versions.ChangeAsync(SegmentVersionChange.ChosenByHand(2), adopted, Cancel);
        harness.Versions.Written.Clear();
        await harness.SkippedAsync(2, adopted.AddHours(1), skips: 30, returned: 30);
        await harness.SkippedAsync(1, adopted.AddDays(-3), skips: 40, returned: 0);

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Null(look.Round?.Rollback);
        Assert.Equal((2, true), (harness.Versions.Active.Number, harness.Versions.Active.ByHand));
        Assert.Empty(harness.Versions.Written);
    }

    [Fact(DisplayName = "BR-CD-013: while learning is off nothing is learned, nothing moves and the day's round is not taken")]
    public async Task NothingHappensWhileLearningIsOff()
    {
        await harness.LearningAsync(false);
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.93, 0.9);

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal(new SegmentTrainingLook(SpareTimeVerdict.LearningOff, null), look);
        Assert.Empty(harness.Trainer.Asked);
        Assert.Empty(harness.Rounds.Taken);
        Assert.Empty(harness.Versions.Added);
    }

    [Fact(DisplayName = "BR-CD-004: while anything is recorded nothing is learned and the day's round waits for spare time")]
    public async Task NothingHappensWhileTheMachineIsBusy()
    {
        harness.Recording = true;
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.93, 0.9);
        SegmentTrainingJob job = harness.Job();

        SegmentTrainingLook busy = await job.LookAsync(Cancel);
        harness.Recording = false;
        SegmentTrainingLook spare = await job.LookAsync(Cancel);

        Assert.Equal(new SegmentTrainingLook(SpareTimeVerdict.Recording, null), busy);
        Assert.Equal(SegmentTrainingOutcome.Adopted, spare.Round?.Outcome);
        Assert.Single(harness.Trainer.Asked);
    }

    [Fact(DisplayName = "BR-CD-013: learning switched off while a round learns stops it at the next boundary, keeps nothing and moves nothing, and the round is taken again once learning is on")]
    public async Task LearningSwitchedOffStopsTheRound()
    {
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.93, 0.9);
        harness.Trainer.WhileLearning = () => harness.LearningAsync(false);

        SegmentTrainingLook stopped = await harness.Job().LookAsync(Cancel);
        harness.Trainer.WhileLearning = null;
        await harness.LearningAsync(true);
        SegmentTrainingLook resumed = await harness.Job().LookAsync(Cancel);

        Assert.Equal((SegmentTrainingOutcome.Stopped, (SpareTimeVerdict?)SpareTimeVerdict.LearningOff), (stopped.Round?.Outcome, stopped.Round?.StoppedBy));
        Assert.Equal(SegmentTrainingOutcome.Adopted, resumed.Round?.Outcome);
        Assert.Equal([2], harness.Versions.Added.Select(version => version.Number));
        Assert.Single(harness.Rounds.Taken);
    }

    [Fact(DisplayName = "BR-CD-004: a recording starting while a round learns stops it at the next boundary, nothing kept and no day taken, and the round is taken again in spare time the same day")]
    public async Task ARoundStoppedByARecordingIsTakenAgainTheSameDay()
    {
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.80, 0.9);
        harness.Trainer.WhileLearning = () =>
        {
            harness.Recording = true;

            return Task.CompletedTask;
        };

        SegmentTrainingLook stopped = await harness.Job().LookAsync(Cancel);
        SegmentTrainingLook busy = await harness.Job().LookAsync(Cancel);
        harness.Trainer.WhileLearning = null;
        harness.Recording = false;
        harness.Clock.Turn(TimeSpan.FromHours(2));
        SegmentTrainingLook resumed = await harness.Job().LookAsync(Cancel);

        Assert.Equal((SegmentTrainingOutcome.Stopped, (SpareTimeVerdict?)SpareTimeVerdict.Recording), (stopped.Round?.Outcome, stopped.Round?.StoppedBy));
        Assert.Equal(new SegmentTrainingLook(SpareTimeVerdict.Recording, null), busy);
        Assert.Equal((SegmentTrainingOutcome.Kept, stopped.Round!.Day), (resumed.Round?.Outcome, resumed.Round?.Day));
        Assert.Equal([2], harness.Versions.Added.Select(version => version.Number));
        Assert.Equal([(stopped.Round.Day, (int?)2)], harness.Rounds.Taken.Select(round => (round.Day, round.Candidate)));
    }

    [Fact(DisplayName = "BR-CD-008: a round that rolls the version in use back keeps a better candidate but holds it out of use, and so do rounds for seven days after")]
    public async Task ACandidateIsHeldOutAfterARollback()
    {
        await harness.SecondInUseAsync(daysAgo: 3);
        DateTime adopted = harness.Versions.Active.AdoptedAt!.Value;
        await harness.SkippedAsync(2, adopted.AddHours(1), skips: 25, returned: 10);
        await harness.SkippedAsync(1, adopted.AddDays(-3), skips: 40, returned: 4);
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.99, 0.9);

        SegmentTrainingLook rolledBack = await harness.Job().LookAsync(Cancel);
        harness.Clock.Turn(TimeSpan.FromDays(6));
        harness.Changes.LastChanged = harness.Now;
        SegmentTrainingLook sixDaysOn = await harness.Job().LookAsync(Cancel);
        harness.Clock.Turn(TimeSpan.FromDays(2));
        harness.Changes.LastChanged = harness.Now;
        SegmentTrainingLook eightDaysOn = await harness.Job().LookAsync(Cancel);

        Assert.NotNull(rolledBack.Round?.Rollback);
        Assert.Equal(
            [(SegmentTrainingOutcome.HeldAfterRollback, (AdoptionVerdict?)AdoptionVerdict.HeldAfterRollback), (SegmentTrainingOutcome.HeldAfterRollback, AdoptionVerdict.HeldAfterRollback), (SegmentTrainingOutcome.Adopted, AdoptionVerdict.Adopted)],
            new[] { rolledBack, sixDaysOn, eightDaysOn }.Select(look => (look.Round!.Outcome, look.Round.Verdict)));
        Assert.Equal([3, 4, 5], harness.Versions.Added.Select(version => version.Number));
        Assert.Equal(5, harness.Versions.Active.Number);
    }

    [Fact(DisplayName = "BR-CD-004: while another process takes a round, nothing is learned and no day is taken")]
    public async Task NothingIsLearnedWhileAnotherProcessTakesARound()
    {
        harness.Rounds.HeldElsewhere = true;
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.93, 0.9);

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal(new SegmentTrainingLook(null, null), look);
        Assert.Empty(harness.Trainer.Asked);
        Assert.Empty(harness.Rounds.Taken);
    }

    [Fact(DisplayName = "BR-CD-004: the day's round is taken once, not again after the process starts again, and again once the next day begins at three in the morning in Japan")]
    public async Task TheRoundIsTakenOnceADay()
    {
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.80, 0.9);

        SegmentTrainingLook first = await harness.Job().LookAsync(Cancel);
        harness.Clock.Turn(TimeSpan.FromHours(23));
        harness.Changes.LastChanged = harness.Now;
        SegmentTrainingLook again = await harness.Job().LookAsync(Cancel);
        harness.Clock.Turn(TimeSpan.FromHours(1));
        SegmentTrainingLook nextDay = await harness.Job().LookAsync(Cancel);

        Assert.NotNull(first.Round);
        Assert.Equal(new SegmentTrainingLook(null, null), again);
        Assert.Equal(first.Round!.Day.AddDays(1), nextDay.Round?.Day);
        Assert.Equal([2, 3], harness.Versions.Added.Select(version => version.Number));
        Assert.Equal(2, harness.Trainer.Asked.Count);
        Assert.Equal([2, 3], harness.Rounds.Taken.Select(round => round.Candidate));
    }

    [Fact(DisplayName = "BR-CD-004: with nothing changed since the last round that kept a candidate was taken, the next day's round still rolls back and is taken, says so, and learns nothing")]
    public async Task NothingChangedLearnsNothing()
    {
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.80, 0.9);
        harness.Changes.LastChanged = SegmentTrainingHarness.Start.AddDays(-1);
        await harness.Job().LookAsync(Cancel);
        harness.Clock.Turn(TimeSpan.FromDays(1));
        harness.Changes.LastChanged = SegmentTrainingHarness.Start;
        DateTime adopted = harness.Now;
        await harness.Versions.ChangeAsync(SegmentVersionChange.Adopting(2), adopted, Cancel);
        await harness.SkippedAsync(2, adopted.AddMinutes(1), skips: 25, returned: 10);
        await harness.SkippedAsync(1, adopted.AddDays(-3), skips: 40, returned: 4);
        harness.Clock.Turn(TimeSpan.FromHours(1));

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal((SegmentTrainingOutcome.NothingNew, (SegmentModelVersion?)null), (look.Round?.Outcome, look.Round?.Candidate));
        Assert.Equal(new SegmentRollback(0.4, 0.1, 25, 1), look.Round?.Rollback);
        Assert.Equal([2], harness.Versions.Added.Select(version => version.Number));
        Assert.Single(harness.Trainer.Asked);
        Assert.Equal([(int?)2, null], harness.Rounds.Taken.Select(round => round.Candidate));
        Assert.Contains(harness.Log.Lines, line => line.Level is LogLevel.Information && line.Line.Contains("learned nothing", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "BR-CD-004: something changed after the last round that kept a candidate was taken, even while it was learning, is learned from the next day")]
    public async Task SomethingChangedSinceIsLearnedFrom()
    {
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.80, 0.9);
        harness.Trainer.WhileLearning = () =>
        {
            harness.Changes.LastChanged = harness.Now.AddSeconds(1);

            return Task.CompletedTask;
        };
        await harness.Job().LookAsync(Cancel);
        harness.Clock.Turn(TimeSpan.FromDays(1));

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal(SegmentTrainingOutcome.Kept, look.Round?.Outcome);
        Assert.Equal([2, 3], harness.Versions.Added.Select(version => version.Number));
    }

    [Fact(DisplayName = "BR-CD-004: a round that kept no candidate does not hold the next day back, changed or not")]
    public async Task ARoundThatKeptNoCandidateDoesNotHoldTheNextBack()
    {
        await harness.Job().LookAsync(Cancel);
        harness.Clock.Turn(TimeSpan.FromDays(1));
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.80, 0.9);

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal(SegmentTrainingOutcome.Kept, look.Round?.Outcome);
        Assert.Equal(2, harness.Trainer.Asked.Count);
    }

    [Fact(DisplayName = "a round that fails is said so and not taken again that day, and the job goes on to the next day's round")]
    public async Task AFailedRoundIsTakenAgainTheNextDay()
    {
        harness.Trainer.Failure = new InvalidDataException("The learning data could not be read.");
        SegmentTrainingJob job = harness.Job(new SegmentTrainingSettings { BeforeFirstLook = TimeSpan.FromMinutes(1), BetweenLooks = TimeSpan.FromHours(12) });
        await job.StartAsync(Cancel);

        await Eventually.Happens(() => harness.Clock.Pending > 0, "the job never waited for its first look");
        harness.Clock.Turn(TimeSpan.FromMinutes(1));
        await Eventually.Happens(() => harness.Log.Lines.Any(line => line.Level is LogLevel.Error), "the failed round was never said");
        harness.Trainer.Failure = null;
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.80, 0.9);
        await Eventually.Happens(() => harness.Clock.Pending > 0, "the job never waited for its next look");
        harness.Clock.Turn(TimeSpan.FromHours(12));
        await Eventually.Happens(() => harness.Clock.Pending > 0, "the job never waited after its second look");
        int askedOnTheDay = harness.Trainer.Asked.Count;
        harness.Clock.Turn(TimeSpan.FromHours(12));
        await Eventually.Happens(() => harness.Versions.Added.Count is 1, "the next day's round never kept its candidate");
        await job.StopAsync(Cancel);

        Assert.Equal(1, askedOnTheDay);
        Assert.IsType<InvalidDataException>(harness.Log.Lines.Single(line => line.Level is LogLevel.Error).Failure);
        Assert.Equal(2, harness.Rounds.Taken.Count);
    }
}
