using Carina.Domain.Channels;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentModelVersionTests
{
    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-CS-003: a version in use carries its parameters, the two kinds they tell apart, and when it was made and put in use")]
    public void AVersionInUseCarriesItsParameters()
    {
        SegmentModelVersion version = Version(1, SegmentModelVersionState.Active, adoptedAt: Noon);

        Assert.Equal(1, version.Number);
        Assert.Equal(SegmentModelVersionState.Active, version.State);
        Assert.False(version.ByHand);
        Assert.Equal(2, version.Kinds);
        Assert.Equal(SegmentModel.First, version.Model);
        Assert.Equal(Noon.AddHours(-1), version.TrainedAt);
        Assert.Equal(Noon, version.AdoptedAt);
        Assert.Null(version.RetiredAt);
    }

    [Fact(DisplayName = "BR-CS-003: a version learning makes is a candidate, not chosen by hand, carrying how it was judged")]
    public void AVersionLearningMakesIsACandidate()
    {
        SegmentEvaluation judged = new(null, [], new EvaluationEvidence(3, 20, Noon.AddHours(-2)), null);

        SegmentModelVersion learned = SegmentModelVersion.Learned(5, SegmentModel.First, judged, Noon);

        Assert.Equal((5, SegmentModelVersionState.Candidate, false, Noon, (DateTime?)null), (learned.Number, learned.State, learned.ByHand, learned.TrainedAt, learned.AdoptedAt));
        Assert.Equal(judged.Evidence, learned.Evaluation?.Evidence);
        Assert.Throws<ArgumentNullException>(() => SegmentModelVersion.Learned(5, SegmentModel.First, null!, Noon));
    }

    [Fact(DisplayName = "BR-CS-003: only a candidate has never been put in use, and only a version used before has been taken out of use")]
    public void TheStateDecidesWhichTimesAreKnown()
    {
        Assert.Null(Version(2, SegmentModelVersionState.Candidate).AdoptedAt);
        Assert.Throws<ArgumentException>(() => Version(2, SegmentModelVersionState.Candidate, adoptedAt: Noon));
        Assert.Throws<ArgumentException>(() => Version(2, SegmentModelVersionState.Active));
        Assert.Throws<ArgumentException>(() => Version(2, SegmentModelVersionState.Retired, adoptedAt: Noon));
        Assert.Throws<ArgumentException>(() => Version(2, SegmentModelVersionState.Active, adoptedAt: Noon, retiredAt: Noon));
        Assert.Equal(Noon.AddHours(1), Version(2, SegmentModelVersionState.Retired, adoptedAt: Noon, retiredAt: Noon.AddHours(1)).RetiredAt);
    }

    [Fact(DisplayName = "BR-CS-003: only the version in use can be chosen by hand, and one chosen by hand says when")]
    public void OnlyTheVersionInUseCanBeChosenByHand()
    {
        SegmentModelVersion chosen = Version(3, SegmentModelVersionState.Active, adoptedAt: Noon, byHand: true, chosenAt: Noon);

        Assert.True(chosen.ByHand);
        Assert.Equal(Noon, chosen.ChosenByHandAt);
        Assert.Throws<ArgumentException>(() => Version(3, SegmentModelVersionState.Active, adoptedAt: Noon, byHand: true));
        Assert.Throws<ArgumentException>(
            () => Version(3, SegmentModelVersionState.Retired, adoptedAt: Noon, retiredAt: Noon, byHand: true, chosenAt: Noon));
        Assert.Throws<ArgumentException>(() => Version(3, SegmentModelVersionState.Candidate, byHand: true, chosenAt: Noon));
    }

    [Fact(DisplayName = "BR-CS-003: only a version taken out of use can have been rolled back from, and it keeps what the rollback measured")]
    public void OnlyARetiredVersionCanHaveBeenRolledBackFrom()
    {
        SegmentModelVersion rolledBack = Version(4, SegmentModelVersionState.Retired, adoptedAt: Noon, retiredAt: Noon.AddDays(7), rolledBackAt: Noon.AddDays(7), rollback: Measured);

        Assert.Equal(Noon.AddDays(7), rolledBack.RolledBackAt);
        Assert.Equal(Measured, rolledBack.Rollback);
        Assert.Throws<ArgumentException>(() => Version(4, SegmentModelVersionState.Active, adoptedAt: Noon, rolledBackAt: Noon.AddDays(7), rollback: Measured));
        Assert.Throws<ArgumentException>(() => Version(4, SegmentModelVersionState.Retired, adoptedAt: Noon, retiredAt: Noon.AddDays(7), rolledBackAt: Noon.AddDays(7)));
        Assert.Throws<ArgumentException>(() => Version(4, SegmentModelVersionState.Retired, adoptedAt: Noon, retiredAt: Noon.AddDays(7), rollback: Measured));
    }

    [Fact(DisplayName = "BR-CD-008: a version evaluated keeps its figures overall and by station, what they stand on and the threshold they recommend; one never evaluated keeps none")]
    public void AVersionEvaluatedKeepsItsFigures()
    {
        SegmentModelVersion evaluated = Version(5, SegmentModelVersionState.Candidate, evaluation: Evaluated);
        SegmentModelVersion never = Version(1, SegmentModelVersionState.Active, adoptedAt: Noon);

        Assert.Equal(Evaluated.Metrics, evaluated.Metrics);
        Assert.Equal(Evaluated.Stations, evaluated.Stations);
        Assert.Equal(Evaluated.Evidence, evaluated.Evidence);
        Assert.Equal(0.65, evaluated.RecommendedThreshold);
        Assert.Equal((Evaluated.Metrics, Evaluated.Evidence, Evaluated.RecommendedThreshold), (evaluated.Evaluation!.Metrics, evaluated.Evaluation.Evidence, evaluated.Evaluation.RecommendedThreshold));
        Assert.Equal(Evaluated.Stations, evaluated.Evaluation.Stations);
        Assert.Null(never.Evaluation);
        Assert.Null(never.Metrics);
        Assert.Null(never.Stations);
        Assert.Null(never.Evidence);
        Assert.Null(never.RecommendedThreshold);
        Assert.Null(never.Rollback);
    }

    [Fact(DisplayName = "BR-CS-003: a version chosen by hand is put in use, the one in use before it is taken out of use, and nothing else changes")]
    public void AVersionChosenByHandIsPutInUse()
    {
        SegmentModelVersion[] kept = [Version(1, SegmentModelVersionState.Retired, adoptedAt: Noon, retiredAt: Noon.AddDays(1)), Version(2, SegmentModelVersionState.Active, adoptedAt: Noon.AddDays(1)), Version(3, SegmentModelVersionState.Candidate, evaluation: Evaluated)];
        DateTime at = Noon.AddDays(2);

        SegmentVersionChanged changed = SegmentModelVersion.Change(kept, SegmentVersionChange.ChosenByHand(3), at)!;

        Assert.Equal([2, 3], changed.Written.Select(version => version.Number));
        Assert.Equal(
            (SegmentModelVersionState.Retired, false, (DateTime?)Noon.AddDays(1), (DateTime?)at, (DateTime?)null, (DateTime?)null),
            Stand(changed.Written[0]));
        Assert.Equal(
            (SegmentModelVersionState.Active, true, (DateTime?)at, (DateTime?)null, (DateTime?)at, (DateTime?)null),
            Stand(changed.Written[1]));
        Assert.Same(changed.Written[1], changed.Active);
        Assert.Equal((Evaluated.Metrics, Evaluated.Evidence, Evaluated.RecommendedThreshold), (changed.Active.Metrics, changed.Active.Evidence, changed.Active.RecommendedThreshold));
        Assert.Equal(Evaluated.Stations, changed.Active.Stations);
        Assert.Equal(kept[2].Model, changed.Active.Model);
    }

    [Fact(DisplayName = "BR-CS-003: a version used before can be chosen by hand again, and what a rollback from it measured goes with its being taken out of use")]
    public void AVersionUsedBeforeCanBeChosenAgain()
    {
        SegmentModelVersion[] kept = [Version(2, SegmentModelVersionState.Active, adoptedAt: Noon.AddDays(8)), Version(3, SegmentModelVersionState.Retired, adoptedAt: Noon, retiredAt: Noon.AddDays(8), rolledBackAt: Noon.AddDays(8), rollback: Measured)];

        SegmentVersionChanged changed = SegmentModelVersion.Change(kept, SegmentVersionChange.ChosenByHand(3), Noon.AddDays(9))!;

        Assert.Equal(3, changed.Active.Number);
        Assert.Equal((SegmentModelVersionState.Active, true, (DateTime?)Noon.AddDays(9), (DateTime?)null, (DateTime?)Noon.AddDays(9), (DateTime?)null), Stand(changed.Active));
        Assert.Null(changed.Active.Rollback);
    }

    [Fact(DisplayName = "BR-CS-003: choosing the version in use by hand only marks it chosen, and choosing it again changes nothing")]
    public void ChoosingTheVersionInUseOnlyMarksItChosen()
    {
        SegmentModelVersion[] kept = [Version(2, SegmentModelVersionState.Active, adoptedAt: Noon)];

        SegmentVersionChanged first = SegmentModelVersion.Change(kept, SegmentVersionChange.ChosenByHand(2), Noon.AddHours(1))!;
        SegmentVersionChanged again = SegmentModelVersion.Change([first.Active], SegmentVersionChange.ChosenByHand(2), Noon.AddHours(2))!;

        Assert.Equal((SegmentModelVersionState.Active, true, (DateTime?)Noon, (DateTime?)null, (DateTime?)Noon.AddHours(1), (DateTime?)null), Stand(Assert.Single(first.Written)));
        Assert.Empty(again.Written);
        Assert.Equal(Noon.AddHours(1), again.Active.ChosenByHandAt);
    }

    [Fact(DisplayName = "BR-CS-003: leaving the choice to learning again keeps the version in use and no longer marks it chosen; with nothing chosen it changes nothing")]
    public void LeavingTheChoiceToLearningKeepsTheVersionInUse()
    {
        SegmentModelVersion chosen = Version(3, SegmentModelVersionState.Active, adoptedAt: Noon, byHand: true, chosenAt: Noon);

        SegmentVersionChanged left = SegmentModelVersion.Change([chosen], SegmentVersionChange.LeftToLearning, Noon.AddHours(1))!;
        SegmentVersionChanged again = SegmentModelVersion.Change([left.Active], SegmentVersionChange.LeftToLearning, Noon.AddHours(2))!;

        Assert.Equal((SegmentModelVersionState.Active, false, (DateTime?)Noon, (DateTime?)null, (DateTime?)null, (DateTime?)null), Stand(Assert.Single(left.Written)));
        Assert.Empty(again.Written);
        Assert.Equal(3, again.Active.Number);
    }

    [Fact(DisplayName = "BR-CD-008: learning puts a candidate in use, and a rollback goes back to the version before, keeping what it measured on the version left")]
    public void LearningAdoptsAndRollsBackThroughTheSameChange()
    {
        SegmentModelVersion[] kept = [Version(1, SegmentModelVersionState.Active, adoptedAt: Noon), Version(2, SegmentModelVersionState.Candidate)];

        SegmentVersionChanged adopted = SegmentModelVersion.Change(kept, SegmentVersionChange.Adopting(2), Noon.AddDays(1))!;
        SegmentVersionChanged rolledBack = SegmentModelVersion.Change(adopted.Written, SegmentVersionChange.GoingBack(Measured with { BackTo = 1 }), Noon.AddDays(3))!;

        Assert.Equal((SegmentModelVersionState.Active, false, (DateTime?)Noon.AddDays(1), (DateTime?)null, (DateTime?)null, (DateTime?)null), Stand(adopted.Active));
        Assert.Equal(1, rolledBack.Active.Number);
        Assert.Equal(
            (SegmentModelVersionState.Retired, false, (DateTime?)Noon.AddDays(1), (DateTime?)Noon.AddDays(3), (DateTime?)null, (DateTime?)Noon.AddDays(3)),
            Stand(rolledBack.Written[0]));
        Assert.Equal(Measured with { BackTo = 1 }, rolledBack.Written[0].Rollback);
    }

    [Fact(DisplayName = "BR-CS-003: learning never moves a version chosen by hand, adopts only a candidate and goes back only to a version used before")]
    public void LearningNeverMovesAVersionChosenByHand()
    {
        SegmentModelVersion chosen = Version(3, SegmentModelVersionState.Active, adoptedAt: Noon, byHand: true, chosenAt: Noon);
        SegmentModelVersion candidate = Version(4, SegmentModelVersionState.Candidate);
        SegmentModelVersion used = Version(2, SegmentModelVersionState.Retired, adoptedAt: Noon.AddHours(-1), retiredAt: Noon);
        SegmentModelVersion inUse = Version(3, SegmentModelVersionState.Active, adoptedAt: Noon);

        Assert.Throws<InvalidOperationException>(() => SegmentModelVersion.Change([chosen, candidate], SegmentVersionChange.Adopting(4), Noon.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => SegmentModelVersion.Change([chosen, used], SegmentVersionChange.GoingBack(Measured), Noon.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => SegmentModelVersion.Change([inUse, used], SegmentVersionChange.Adopting(2), Noon.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => SegmentModelVersion.Change([inUse, candidate], SegmentVersionChange.GoingBack(Measured with { BackTo = 4 }), Noon.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => SegmentModelVersion.Change([inUse], SegmentVersionChange.Adopting(3), Noon.AddDays(1)));
    }

    [Fact(DisplayName = "a change naming a number no version has changes nothing, and the versions kept have exactly one in use")]
    public void AChangeNamingAnUnknownNumberChangesNothing()
    {
        SegmentModelVersion inUse = Version(1, SegmentModelVersionState.Active, adoptedAt: Noon);

        Assert.Null(SegmentModelVersion.Change([inUse], SegmentVersionChange.ChosenByHand(7), Noon.AddHours(1)));
        Assert.Throws<InvalidOperationException>(() => SegmentModelVersion.Change([Version(2, SegmentModelVersionState.Candidate)], SegmentVersionChange.ChosenByHand(2), Noon.AddHours(1)));
    }

    [Fact(DisplayName = "versions are numbered from 1, in one of the three states, and every time is in UTC")]
    public void VersionsAreNumberedFromOneInUtc()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Version(0, SegmentModelVersionState.Active, adoptedAt: Noon));
        Assert.Throws<ArgumentOutOfRangeException>(() => Version(1, (SegmentModelVersionState)9, adoptedAt: Noon));
        Assert.Throws<ArgumentException>(() => Version(1, SegmentModelVersionState.Active, adoptedAt: DateTime.SpecifyKind(Noon, DateTimeKind.Local)));
        Assert.Throws<ArgumentException>(() => Version(1, SegmentModelVersionState.Active, adoptedAt: Noon.AddHours(-2)));
    }

    private static readonly SegmentRollback Measured = new(0.09, 0.03, 25, 2);

    private static readonly SegmentEvaluation Evaluated = new(
        new SegmentMetrics(0.93, 0.85, 1.2, 2.1, [new KindMetrics(SegmentKind.Break, 0.95, 0.97)], null, 0),
        [new StationMetrics(new NetworkId(1), new ServiceId(1), 5, new SegmentMetrics(0.96, 0.9, 1, 1.4, [], null, 0))],
        new EvaluationEvidence(12, 148, Noon.AddDays(-1)),
        0.65);

    private static (SegmentModelVersionState, bool, DateTime?, DateTime?, DateTime?, DateTime?) Stand(SegmentModelVersion version)
        => (version.State, version.ByHand, version.AdoptedAt, version.RetiredAt, version.ChosenByHandAt, version.RolledBackAt);

    private static SegmentModelVersion Version(
        int number,
        SegmentModelVersionState state,
        DateTime? adoptedAt = null,
        DateTime? retiredAt = null,
        bool byHand = false,
        DateTime? chosenAt = null,
        DateTime? rolledBackAt = null,
        SegmentEvaluation? evaluation = null,
        SegmentRollback? rollback = null)
        => SegmentModelVersion.Rehydrate(number, state, byHand, SegmentModel.First, Noon.AddHours(-1), adoptedAt, retiredAt, chosenAt, rolledBackAt, evaluation, rollback);
}
