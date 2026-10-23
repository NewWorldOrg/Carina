using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentBuiltInVersionTests
{
    private static readonly DateTime Started = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Earlier = Started.AddDays(-30);

    private static readonly SegmentModel Shipped = SegmentModel.BuiltIn;

    private static readonly SegmentModel ShippedBefore = new(
        SegmentModel.BuiltIn.Weights,
        SegmentModel.BuiltIn.StationOffsets,
        SegmentModel.BuiltIn.SeriesOffsets,
        SegmentThresholds.Initial,
        SegmentModel.BuiltIn.Standardisation);

    private static readonly SegmentEvaluation Evaluated = new(null, [], new EvaluationEvidence(12, 148, Earlier));

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
        SegmentModelVersion learned = SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Active, false, Shipped, Earlier, Earlier, null, null, null, Evaluated);

        Assert.Equal(2, SegmentModelVersion.KeepBuiltIn([learned], Shipped, Started)?.Added.Number);
    }

    [Fact(DisplayName = "BR-CS-003: a built-in version is never evaluated, and a learned version is never built in")]
    public void ABuiltInVersionIsNeverEvaluated()
    {
        Assert.Throws<ArgumentException>(() => SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Candidate, false, Shipped, Earlier, null, null, null, null, Evaluated, builtIn: true));
        Assert.False(SegmentModelVersion.Learned(2, Shipped, Evaluated, Started).BuiltIn);
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
        => SegmentModelVersion.Rehydrate(number, state, false, ShippedBefore, Earlier, adoptedAt, null, null, null, Evaluated);
}
