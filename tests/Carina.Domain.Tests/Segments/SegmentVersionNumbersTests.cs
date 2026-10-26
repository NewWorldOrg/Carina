using Carina.Domain.Segments;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentVersionNumbersTests
{
    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly SegmentEvaluation Evaluated = new(null, [], new EvaluationEvidence(12, 148, Noon.AddDays(-1)));

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
        SegmentModelVersion chosen = SegmentModelVersion.Rehydrate(3, SegmentModelVersionState.Active, true, HandMadeModels.First, Noon, Noon, null, Noon, null, Evaluated);
        SegmentModelVersion rolledBack = SegmentModelVersion.Rehydrate(4, SegmentModelVersionState.Retired, false, HandMadeModels.First, Noon, Noon, Noon.AddDays(1), null, Noon.AddDays(1), Evaluated, new SegmentRollback(0.4, 0.1, 25, 1));

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
        => SegmentModelVersion.Learned(number, HandMadeModels.First, Evaluated, Noon);

    private static SegmentModelVersion Active(int number)
        => SegmentModelVersion.Rehydrate(number, SegmentModelVersionState.Active, false, HandMadeModels.First, Noon, Noon, null, null, null, Evaluated);

    private static SegmentModelVersion Retired(int number)
        => SegmentModelVersion.Rehydrate(number, SegmentModelVersionState.Retired, false, HandMadeModels.First, Noon, Noon, Noon.AddDays(1), null, null, builtIn: true);
}
