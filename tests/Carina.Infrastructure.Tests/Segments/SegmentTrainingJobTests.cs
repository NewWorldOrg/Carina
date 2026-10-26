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
        Assert.Equal([Kept(SegmentTrainingOutcome.NothingToLearnFrom, null, null, null)], harness.Rounds.Taken.Select(KeptAs));
    }

    [Fact(DisplayName = "BR-CD-008: a round that judged no recording keeps no candidate, says there was nothing to learn from, and leaves the version in use")]
    public async Task ARoundThatJudgedNoRecordingKeepsNoCandidate()
    {
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.93, 0.9, new EvaluationEvidence(0, 0, null));

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal((SegmentTrainingOutcome.NothingToLearnFrom, (SegmentModelVersion?)null), (look.Round?.Outcome, look.Round?.Candidate));
        Assert.Empty(harness.Versions.Added);
        Assert.Empty(harness.Versions.Written);
        Assert.Equal(SegmentModelVersion.FirstNumber, harness.Versions.Active.Number);
        Assert.Equal([Kept(SegmentTrainingOutcome.NothingToLearnFrom, null, null, null)], harness.Rounds.Taken.Select(KeptAs));
        Assert.Equal([(int?)null], harness.Rounds.Taken.Select(round => round.InUseVersion));
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
        Assert.Equal([Kept(SegmentTrainingOutcome.Adopted, 2, AdoptionVerdict.Adopted, harness.Trainer.InUse.Metrics)], harness.Rounds.Taken.Select(KeptAs));
        Assert.Equal([(int?)SegmentModelVersion.FirstNumber], harness.Rounds.Taken.Select(round => round.InUseVersion));
    }

    [Theory(DisplayName = "BR-CD-008: a candidate that finds no more boundaries, as many with fewer seconds of adverts, or stands on too little is kept as a candidate only, and the candidate before it is let go")]
    [InlineData(0.80, 0.9, 12, AdoptionVerdict.NoBetterAtBoundaries)]
    [InlineData(0.85, 0.8, 12, AdoptionVerdict.NoBetterAtBoundaries)]
    [InlineData(0.93, 0.9, 9, AdoptionVerdict.TooLittleEvidence)]
    public async Task AWorseCandidateIsKeptOnly(double recall, double seconds, int recordings, AdoptionVerdict verdict)
    {
        harness.Versions.Others.Add(SegmentModelVersion.Learned(2, HandMadeModels.First, SegmentTrainingHarness.Evaluated(0.5, 0.9), SegmentTrainingHarness.Start.AddDays(-1)));
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(recall, seconds, new EvaluationEvidence(recordings, 148, SegmentTrainingHarness.Start));

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal((SegmentTrainingOutcome.Kept, (AdoptionVerdict?)verdict), (look.Round?.Outcome, look.Round?.Verdict));
        Assert.Equal([3], harness.Versions.Added.Select(version => version.Number));
        Assert.Equal(
            [(1, SegmentModelVersionState.Active), (3, SegmentModelVersionState.Candidate)],
            (await harness.Versions.ListAsync(Cancel)).OrderBy(version => version.Number).Select(version => (version.Number, version.State)));
        Assert.Equal([2], look.Round?.LetGo);
        Assert.Empty(harness.Versions.Written);
        Assert.Equal([Kept(SegmentTrainingOutcome.Kept, 3, verdict, harness.Trainer.InUse.Metrics)], harness.Rounds.Taken.Select(KeptAs));
    }

    [Fact(DisplayName = "BR-CD-008: a candidate that finds more boundaries but loses the openings the version in use finds is kept as a candidate only, and the round says so")]
    public async Task ACandidateThatLosesTheOpeningsIsKeptOnly()
    {
        EvaluationEvidence themes = new(12, 148, SegmentTrainingHarness.Start, openings: 3, endings: 3);
        harness.Trainer.InUse = Themed(0.85, themes, 0.83);
        harness.Trainer.Candidate = Themed(0.90, themes, 0.0);

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal((SegmentTrainingOutcome.Kept, (AdoptionVerdict?)AdoptionVerdict.LosesOpeningsOrEndings), (look.Round?.Outcome, look.Round?.Verdict));
        Assert.Equal((SegmentModelVersion.FirstNumber, SegmentModelVersionState.Active), (harness.Versions.Active.Number, harness.Versions.Active.State));
        Assert.Empty(harness.Versions.Written);
        Assert.Equal([Kept(SegmentTrainingOutcome.Kept, 2, AdoptionVerdict.LosesOpeningsOrEndings, harness.Trainer.InUse.Metrics)], harness.Rounds.Taken.Select(KeptAs));
    }

    [Fact(DisplayName = "BR-CS-003: a round lets go of the candidates before its own that learning made and nobody used, and keeps one something still names, one built in and the version in use")]
    public async Task ARoundLetsGoOfTheCandidatesNobodyUsed()
    {
        harness.Versions.Others.Add(SegmentModelVersion.Learned(2, HandMadeModels.First, SegmentTrainingHarness.Evaluated(0.5, 0.9), SegmentTrainingHarness.Start.AddDays(-4)));
        harness.Versions.Others.Add(SegmentModelVersion.Rehydrate(3, SegmentModelVersionState.Candidate, false, HandMadeModels.First, SegmentTrainingHarness.Start.AddDays(-3), null, null, null, null, builtIn: true));
        harness.Versions.Others.Add(SegmentModelVersion.Learned(4, HandMadeModels.First, SegmentTrainingHarness.Evaluated(0.6, 0.9), SegmentTrainingHarness.Start.AddDays(-2)));
        harness.Versions.Others.Add(SegmentModelVersion.Learned(5, HandMadeModels.First, SegmentTrainingHarness.Evaluated(0.7, 0.9), SegmentTrainingHarness.Start.AddDays(-1)));
        harness.Versions.Named.Add(4);
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.80, 0.9);

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal((SegmentTrainingOutcome.Kept, (int?)6), (look.Round?.Outcome, look.Round?.Candidate?.Number));
        Assert.Equal([2, 5], look.Round?.LetGo);
        Assert.Equal([1, 3, 4, 6], (await harness.Versions.ListAsync(Cancel)).Select(version => version.Number).Order());
        Assert.Contains(harness.Log.Lines, line => line.Level is LogLevel.Information && line.Line.Contains("let go of candidates 2, 5", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "BR-CS-003: the round keeps its candidate's figures and what they stand on, so that what it said reads once the next round lets the candidate go")]
    public async Task TheRoundKeepsTheFiguresOfItsCandidate()
    {
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.80, 0.9, new EvaluationEvidence(12, 56, SegmentTrainingHarness.Start));
        harness.Trainer.WhileLearning = () =>
        {
            harness.Changes.LastChanged = harness.Now.AddSeconds(1);

            return Task.CompletedTask;
        };
        await harness.Job().LookAsync(Cancel);
        harness.Clock.Turn(TimeSpan.FromDays(1));
        SegmentEvaluation first = harness.Trainer.Candidate;
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.82, 0.9);

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal([2], look.Round?.LetGo);
        Assert.Null(await harness.Versions.FindAsync(2, Cancel));
        SegmentLearningRound kept = harness.Rounds.Taken[0];
        Assert.Equal((2, first.Metrics, first.Evidence), (kept.Candidate, kept.CandidateMetrics, kept.CandidateEvidence));
        Assert.Equal((3, harness.Trainer.Candidate.Metrics, harness.Trainer.Candidate.Evidence), (harness.Rounds.Taken[1].Candidate, harness.Rounds.Taken[1].CandidateMetrics, harness.Rounds.Taken[1].CandidateEvidence));
    }

    [Fact(DisplayName = "BR-CS-003: a number let go is never given again: each round's candidate goes on after the highest number given, those let go and one only remembered elsewhere included")]
    public async Task ANumberLetGoIsNeverGivenAgain()
    {
        harness.Versions.Others.Add(SegmentModelVersion.Learned(2, HandMadeModels.First, SegmentTrainingHarness.Evaluated(0.5, 0.9), SegmentTrainingHarness.Start.AddDays(-2)));
        harness.Versions.Others.Add(SegmentModelVersion.Learned(3, HandMadeModels.First, SegmentTrainingHarness.Evaluated(0.6, 0.9), SegmentTrainingHarness.Start.AddDays(-1)));
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.80, 0.9);
        harness.Trainer.WhileLearning = () =>
        {
            harness.Changes.LastChanged = harness.Now.AddSeconds(1);

            return Task.CompletedTask;
        };

        SegmentTrainingLook first = await harness.Job().LookAsync(Cancel);
        harness.Clock.Turn(TimeSpan.FromDays(1));
        SegmentTrainingLook second = await harness.Job().LookAsync(Cancel);
        harness.Versions.RememberedElsewhere = 9;
        harness.Clock.Turn(TimeSpan.FromDays(1));
        SegmentTrainingLook third = await harness.Job().LookAsync(Cancel);

        Assert.Equal([4, 5, 10], new[] { first, second, third }.Select(look => look.Round?.Candidate?.Number ?? 0));
        Assert.Equal([2, 3], first.Round?.LetGo);
        Assert.Equal([4], second.Round?.LetGo);
        Assert.Equal([5], third.Round?.LetGo);
        Assert.Equal([1, 10], (await harness.Versions.ListAsync(Cancel)).Select(version => version.Number).Order());
        Assert.Equal([4, 5, 10], harness.Rounds.Taken.Select(round => round.Candidate ?? 0));
    }

    [Fact(DisplayName = "BR-CD-008: while the version in use is chosen by hand, learning still keeps and evaluates a candidate but never puts it in use")]
    public async Task AVersionChosenByHandIsNeverReplaced()
    {
        harness.Versions.Others.Add(SegmentModelVersion.Learned(2, HandMadeModels.First, SegmentTrainingHarness.Evaluated(0.5, 0.9), SegmentTrainingHarness.Start.AddDays(-2)));
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
        Assert.Equal([Kept(SegmentTrainingOutcome.InUseChangedMeanwhile, 2, AdoptionVerdict.Adopted, harness.Trainer.InUse.Metrics)], harness.Rounds.Taken.Select(KeptAs));
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

    [Fact(DisplayName = "BR-CD-008: a round that rolls the version in use back and learns a candidate keeps the number of the version it went back to as the version in use it measured")]
    public async Task ARoundThatRollsBackKeepsTheNumberOfTheVersionItWentBackTo()
    {
        await harness.SecondInUseAsync(daysAgo: 3);
        DateTime adopted = harness.Versions.Active.AdoptedAt!.Value;
        await harness.SkippedAsync(2, adopted.AddHours(1), skips: 25, returned: 10);
        await harness.SkippedAsync(1, adopted.AddDays(-3), skips: 40, returned: 4);
        harness.Trainer.Candidate = SegmentTrainingHarness.Evaluated(0.93, 0.9);

        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.NotNull(look.Round?.Rollback);
        Assert.Equal([SegmentModelVersion.FirstNumber], harness.Trainer.Asked.Select(asked => asked.Active));
        SegmentLearningRound kept = Assert.Single(harness.Rounds.Taken);
        Assert.Equal((SegmentModelVersion.FirstNumber, harness.Trainer.InUse.Metrics), (kept.InUseVersion, kept.InUseMetrics));
    }

    [Fact(DisplayName = "BR-CD-008: a round that learns nothing keeps the number of no version")]
    public async Task ARoundThatLearnsNothingKeepsNoVersionNumber()
    {
        SegmentTrainingLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal(SegmentTrainingOutcome.NothingToLearnFrom, look.Round?.Outcome);
        Assert.Equal([(int?)null], harness.Rounds.Taken.Select(round => round.InUseVersion));
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
        Assert.Equal(
            [(SegmentTrainingOutcome.HeldAfterRollback, AdoptionVerdict.HeldAfterRollback), (SegmentTrainingOutcome.HeldAfterRollback, AdoptionVerdict.HeldAfterRollback), ((SegmentTrainingOutcome?)SegmentTrainingOutcome.Adopted, (AdoptionVerdict?)AdoptionVerdict.Adopted)],
            harness.Rounds.Taken.Select(round => (round.Outcome, round.Verdict)));
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
        Assert.Equal(Kept(SegmentTrainingOutcome.NothingNew, null, null, null), KeptAs(harness.Rounds.Taken[1]));
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
        Assert.Equal(
            [Kept(SegmentTrainingOutcome.Failed, null, null, null), Kept(SegmentTrainingOutcome.Kept, 2, AdoptionVerdict.NoBetterAtBoundaries, harness.Trainer.InUse.Metrics)],
            harness.Rounds.Taken.Select(KeptAs));
    }

    private static (SegmentTrainingOutcome? Outcome, int? Candidate, AdoptionVerdict? Verdict, SegmentMetrics? InUse) Kept(
        SegmentTrainingOutcome outcome,
        int? candidate,
        AdoptionVerdict? verdict,
        SegmentMetrics? inUse)
        => (outcome, candidate, verdict, inUse);

    private static (SegmentTrainingOutcome? Outcome, int? Candidate, AdoptionVerdict? Verdict, SegmentMetrics? InUse) KeptAs(SegmentLearningRound round)
        => (round.Outcome, round.Candidate, round.Verdict, round.InUseMetrics);

    private static SegmentEvaluation Themed(double recall, EvaluationEvidence evidence, double openings)
        => new(
            new SegmentMetrics(recall, Math.Round(recall - 0.1, 2), [new KindMetrics(SegmentKind.Break, 0.9), new KindMetrics(SegmentKind.Opening, openings)], null, 0),
            [],
            evidence);
}
