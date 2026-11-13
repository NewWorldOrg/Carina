using Carina.Domain.Channels;
using Carina.Domain.Segments;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Segments;

[Trait("Category", "Unit")]
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
        Assert.Equal(HandMadeModels.First, version.Model);
        Assert.Equal(Noon.AddHours(-1), version.TrainedAt);
        Assert.Equal(Noon, version.AdoptedAt);
        Assert.Null(version.RetiredAt);
    }

    [Fact(DisplayName = "BR-CS-003: a version learning makes is a candidate, not chosen by hand, carrying how it was judged")]
    public void AVersionLearningMakesIsACandidate()
    {
        SegmentEvaluation judged = new(null, [], new EvaluationEvidence(3, 20, Noon.AddHours(-2)));

        SegmentModelVersion learned = SegmentModelVersion.Learned(5, HandMadeModels.First, judged, Noon);

        Assert.Equal((5, SegmentModelVersionState.Candidate, false, Noon, (DateTime?)null), (learned.Number, learned.State, learned.ByHand, learned.TrainedAt, learned.AdoptedAt));
        Assert.Equal(judged.Evidence, learned.Evaluation?.Evidence);
        Assert.Throws<ArgumentNullException>(() => SegmentModelVersion.Learned(5, HandMadeModels.First, null!, Noon));
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

    [Fact(DisplayName = "BR-CD-008: a version evaluated keeps its figures overall and by station, and what they stand on; one never evaluated keeps none")]
    public void AVersionEvaluatedKeepsItsFigures()
    {
        SegmentModelVersion evaluated = Version(5, SegmentModelVersionState.Candidate, evaluation: Evaluated);
        SegmentModelVersion never = Version(1, SegmentModelVersionState.Active, adoptedAt: Noon);

        Assert.Equal(Evaluated.Metrics, evaluated.Metrics);
        Assert.Equal(Evaluated.Stations, evaluated.Stations);
        Assert.Equal(Evaluated.Evidence, evaluated.Evidence);
        Assert.Equal((Evaluated.Metrics, Evaluated.Evidence), (evaluated.Evaluation!.Metrics, evaluated.Evaluation.Evidence));
        Assert.Equal(Evaluated.Stations, evaluated.Evaluation.Stations);
        Assert.Null(never.Evaluation);
        Assert.Null(never.Metrics);
        Assert.Null(never.Stations);
        Assert.Null(never.Evidence);
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
        Assert.Equal((Evaluated.Metrics, Evaluated.Evidence), (changed.Active.Metrics, changed.Active.Evidence));
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
        new SegmentMetrics(0.93, 0.85, [new KindMetrics(SegmentKind.Break, 0.95)], null, 0),
        [new StationMetrics(new NetworkId(1), new ServiceId(1), 5, new SegmentMetrics(0.96, 0.9, [], null, 0))],
        new EvaluationEvidence(12, 148, Noon.AddDays(-1)));

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
        => SegmentModelVersion.Rehydrate(number, state, byHand, HandMadeModels.First, Noon.AddHours(-1), adoptedAt, retiredAt, chosenAt, rolledBackAt, evaluation, rollback);

    private static readonly DateTime Started = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Earlier = Started.AddDays(-30);

    private static readonly SegmentModel Shipped = SegmentModel.BuiltIn;

    private static readonly SegmentModel ShippedBefore = new(
        SegmentModel.BuiltIn.Weights,
        SegmentModel.BuiltIn.StationOffsets,
        SegmentModel.BuiltIn.SeriesOffsets,
        SegmentThresholds.Initial,
        SegmentModel.BuiltIn.Standardisation);

    private static readonly SegmentEvaluation EvaluatedAMonthEarlier = new(null, [], new EvaluationEvidence(12, 148, Earlier));

    [Fact(DisplayName = "BR-CS-003: with no version kept, the built-in version is kept as the first and put in use, marked built in and never evaluated")]
    public void WithNoVersionTheBuiltInIsTheFirstInUse()
    {
        SegmentBuiltInKept kept = SegmentModelVersion.KeepBuiltIn([], Shipped, Started)!;

        Assert.Equal(
            (SegmentModelVersion.FirstNumber, SegmentModelVersionState.Active, true, false, Started, (DateTime?)Started),
            (kept.Added.Number, kept.Added.State, kept.Added.BuiltIn, kept.Added.ByHand, kept.Added.TrainedAt, kept.Added.AdoptedAt));
        Assert.Equal(Shipped, kept.Added.Model);
        Assert.Null(kept.Added.Evaluation);
        Assert.Empty(kept.Moved);
        Assert.Same(kept.Added, kept.Active);
    }

    [Theory(DisplayName = "BR-CS-003: a built-in version holding the same parameters, in whatever state, is the built-in version already kept, and nothing is written")]
    [InlineData(SegmentModelVersionState.Active)]
    [InlineData(SegmentModelVersionState.Candidate)]
    [InlineData(SegmentModelVersionState.Retired)]
    public void TheSameParametersBuiltInAreKeptOnce(SegmentModelVersionState state)
    {
        SegmentModelVersion same = state switch
        {
            SegmentModelVersionState.Active => BuiltIn(4, Shipped, SegmentModelVersionState.Active, Earlier),
            SegmentModelVersionState.Candidate => BuiltIn(4, Shipped, SegmentModelVersionState.Candidate),
            _ => BuiltIn(4, Shipped, SegmentModelVersionState.Retired, Earlier, Earlier.AddDays(1)),
        };
        SegmentModelVersion[] kept = state is SegmentModelVersionState.Active ? [same] : [same, Learned(5, SegmentModelVersionState.Active, Earlier.AddDays(1))];

        Assert.Null(SegmentModelVersion.KeepBuiltIn(kept, Shipped, Started));
    }

    [Fact(DisplayName = "BR-CS-003: a built-in version holding other parameters, in use and not chosen by hand, gives way to the built-in version, kept under the number after the highest and put in use")]
    public void TheBuiltInTakesThePlaceOfAnEarlierBuiltInInUse()
    {
        SegmentModelVersion[] kept = [BuiltIn(1, ShippedBefore, SegmentModelVersionState.Active, Earlier), Learned(2, SegmentModelVersionState.Candidate)];

        SegmentBuiltInKept added = SegmentModelVersion.KeepBuiltIn(kept, Shipped, Started)!;

        Assert.Equal((3, SegmentModelVersionState.Active, true, false, (DateTime?)Started), (added.Added.Number, added.Added.State, added.Added.BuiltIn, added.Added.ByHand, added.Added.AdoptedAt));
        Assert.Equal(Shipped, added.Added.Model);
        SegmentModelVersion left = Assert.Single(added.Moved);
        Assert.Equal((1, SegmentModelVersionState.Retired, (DateTime?)Earlier, (DateTime?)Started, true), (left.Number, left.State, left.AdoptedAt, left.RetiredAt, left.BuiltIn));
        Assert.Same(added.Added, added.Active);
    }

    [Fact(DisplayName = "BR-CS-003: beside a built-in version chosen by hand, the built-in version is kept as a candidate and the version in use stays")]
    public void BesideAVersionChosenByHandTheBuiltInIsACandidate()
    {
        SegmentModelVersion chosen = SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Active, true, ShippedBefore, Earlier, Earlier, null, Earlier.AddDays(1), null, builtIn: true);

        SegmentBuiltInKept added = SegmentModelVersion.KeepBuiltIn([chosen], Shipped, Started)!;

        Assert.Equal((2, SegmentModelVersionState.Candidate, true, (DateTime?)null), (added.Added.Number, added.Added.State, added.Added.BuiltIn, added.Added.AdoptedAt));
        Assert.Empty(added.Moved);
        Assert.Same(chosen, added.Active);
    }

    [Fact(DisplayName = "BR-CS-003: beside a learned version in use, the built-in version is kept as a candidate and the version in use stays")]
    public void BesideALearnedVersionInUseTheBuiltInIsACandidate()
    {
        SegmentModelVersion learned = Learned(2, SegmentModelVersionState.Active, Earlier.AddDays(1));
        SegmentModelVersion[] kept = [BuiltIn(1, ShippedBefore, SegmentModelVersionState.Retired, Earlier, Earlier.AddDays(1)), learned];

        SegmentBuiltInKept added = SegmentModelVersion.KeepBuiltIn(kept, Shipped, Started)!;

        Assert.Equal((3, SegmentModelVersionState.Candidate), (added.Added.Number, added.Added.State));
        Assert.Empty(added.Moved);
        Assert.Same(learned, added.Active);
    }

    [Fact(DisplayName = "BR-CS-003: a learned version holding the same parameters is not the built-in version, which is kept beside it")]
    public void ALearnedVersionIsNeverTheBuiltIn()
    {
        SegmentModelVersion learned = SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Active, false, Shipped, Earlier, Earlier, null, null, null, EvaluatedAMonthEarlier);

        Assert.Equal(2, SegmentModelVersion.KeepBuiltIn([learned], Shipped, Started)?.Added.Number);
    }

    [Fact(DisplayName = "BR-CS-003: a built-in version is never evaluated, and a learned version is never built in")]
    public void ABuiltInVersionIsNeverEvaluated()
    {
        Assert.Throws<ArgumentException>(() => SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Candidate, false, Shipped, Earlier, null, null, null, null, EvaluatedAMonthEarlier, builtIn: true));
        Assert.False(SegmentModelVersion.Learned(2, Shipped, EvaluatedAMonthEarlier, Started).BuiltIn);
    }

    [Fact(DisplayName = "BR-CS-003: a built-in version keeps its mark through every change of the version in use")]
    public void ABuiltInVersionKeepsItsMarkThroughAChange()
    {
        SegmentModelVersion[] kept = [BuiltIn(1, Shipped, SegmentModelVersionState.Active, Earlier), BuiltIn(2, ShippedBefore, SegmentModelVersionState.Candidate)];

        SegmentVersionChanged changed = SegmentModelVersion.Change(kept, SegmentVersionChange.ChosenByHand(2), Started)!;

        Assert.All(changed.Written, version => Assert.True(version.BuiltIn));
    }

    [Fact(DisplayName = "the versions kept have no more than one in use")]
    public void NoMoreThanOneVersionIsInUse()
    {
        SegmentModelVersion[] kept = [BuiltIn(1, ShippedBefore, SegmentModelVersionState.Active, Earlier), Learned(2, SegmentModelVersionState.Active, Earlier)];

        Assert.Throws<InvalidOperationException>(() => SegmentModelVersion.KeepBuiltIn(kept, Shipped, Started));
    }

    private static SegmentModelVersion BuiltIn(int number, SegmentModel model, SegmentModelVersionState state, DateTime? adoptedAt = null, DateTime? retiredAt = null)
        => SegmentModelVersion.Rehydrate(number, state, false, model, Earlier, adoptedAt, retiredAt, null, null, builtIn: true);

    private static SegmentModelVersion Learned(int number, SegmentModelVersionState state, DateTime? adoptedAt = null)
        => SegmentModelVersion.Rehydrate(number, state, false, ShippedBefore, Earlier, adoptedAt, null, null, null, EvaluatedAMonthEarlier);

    private static readonly SegmentEvaluation EvaluatedTheDayBefore = new(null, [], new EvaluationEvidence(12, 148, Noon.AddDays(-1)));

    [Theory(DisplayName = "BR-CS-003: a new version is numbered after the highest number given, and the first while none has been")]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(14, 15)]
    public void ANewVersionIsNumberedAfterTheHighestGiven(int highestGiven, int number)
    {
        Assert.Equal(number, SegmentModelVersion.NumberAfter(highestGiven));
    }

    [Fact(DisplayName = "BR-CS-003: no number below none is given")]
    public void NoNumberBelowNoneIsGiven()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SegmentModelVersion.NumberAfter(-1));
    }

    [Fact(DisplayName = "BR-CS-003: once learning keeps a candidate, it lets go of every candidate before it that it made and nobody put in use or chose")]
    public void LearningLetsGoOfTheCandidatesBeforeTheLatest()
    {
        SegmentModelVersion[] kept =
        [
            Retired(1),
            Candidate(2),
            Candidate(5),
            Active(10),
            Candidate(11),
            Candidate(14),
            Candidate(15),
        ];

        Assert.Equal([2, 5, 11, 14], SegmentModelVersion.LetGoBefore(kept, 15));
    }

    [Fact(DisplayName = "BR-CS-003: the latest candidate stays until the next one is kept, and so does any number after it")]
    public void TheLatestCandidateStays()
    {
        SegmentModelVersion[] kept = [Active(1), Candidate(4), Candidate(6)];

        Assert.Equal([4], SegmentModelVersion.LetGoBefore(kept, 6));
        Assert.Empty(SegmentModelVersion.LetGoBefore(kept, 4));
    }

    [Fact(DisplayName = "BR-CS-003: a version built in, the version in use, one chosen by hand and the versions used before are never let go")]
    public void UsedAndBuiltInVersionsAreNeverLetGo()
    {
        SegmentModelVersion builtIn = SegmentModelVersion.Rehydrate(2, SegmentModelVersionState.Candidate, false, HandMadeModels.First, Noon, null, null, null, null, builtIn: true);
        SegmentModelVersion chosen = SegmentModelVersion.Rehydrate(3, SegmentModelVersionState.Active, true, HandMadeModels.First, Noon, Noon, null, Noon, null, EvaluatedTheDayBefore);
        SegmentModelVersion rolledBack = SegmentModelVersion.Rehydrate(4, SegmentModelVersionState.Retired, false, HandMadeModels.First, Noon, Noon, Noon.AddDays(1), null, Noon.AddDays(1), EvaluatedTheDayBefore, new SegmentRollback(0.4, 0.1, 25, 1));

        Assert.Empty(SegmentModelVersion.LetGoBefore([Retired(1), builtIn, chosen, rolledBack, Candidate(9)], 9));
    }

    [Fact(DisplayName = "BR-CS-003: the built-in version is numbered after the highest number given, one let go or remembered elsewhere included, and the numbers kept do not move")]
    public void TheBuiltInIsNumberedAfterEveryNumberGiven()
    {
        SegmentModelVersion learned = Active(3);

        SegmentBuiltInKept kept = SegmentModelVersion.KeepBuiltIn([learned], SegmentModel.BuiltIn, Noon, highestGiven: 7)!;

        Assert.Equal((8, SegmentModelVersionState.Candidate), (kept.Added.Number, kept.Added.State));
        Assert.Same(learned, kept.Active);
        Assert.Equal(4, SegmentModelVersion.KeepBuiltIn([learned], SegmentModel.BuiltIn, Noon, highestGiven: 2)?.Added.Number);
    }

    [Theory(DisplayName = "BR-CS-003: with no version kept, the built-in version is the first only while no number is remembered, and otherwise goes on from the highest remembered")]
    [InlineData(0, 1)]
    [InlineData(4, 5)]
    public void WithNoVersionKeptTheBuiltInGoesOnFromWhatIsRemembered(int highestGiven, int number)
    {
        SegmentBuiltInKept kept = SegmentModelVersion.KeepBuiltIn([], SegmentModel.BuiltIn, Noon, highestGiven)!;

        Assert.Equal((number, SegmentModelVersionState.Active), (kept.Added.Number, kept.Added.State));
        Assert.Same(kept.Added, kept.Active);
    }

    private static SegmentModelVersion Candidate(int number)
        => SegmentModelVersion.Learned(number, HandMadeModels.First, EvaluatedTheDayBefore, Noon);

    private static SegmentModelVersion Active(int number)
        => SegmentModelVersion.Rehydrate(number, SegmentModelVersionState.Active, false, HandMadeModels.First, Noon, Noon, null, null, null, EvaluatedTheDayBefore);

    private static SegmentModelVersion Retired(int number)
        => SegmentModelVersion.Rehydrate(number, SegmentModelVersionState.Retired, false, HandMadeModels.First, Noon, Noon, Noon.AddDays(1), null, null, builtIn: true);
}
