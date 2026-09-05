using Carina.Domain.Library;

namespace Carina.Domain.Tests.Library;

public sealed class DeletionResultTests
{
    [Fact]
    public void ADeletionThatFinishedNamesNoReasonAndLeavesNothingBehind()
    {
        DeletionResult result = DeletionResult.Done();

        Assert.True(result.Deleted);
        Assert.Null(result.Refusal);
        Assert.Empty(result.LeftBehind);
    }

    [Theory]
    [InlineData(DeletionRefusal.NotFound)]
    [InlineData(DeletionRefusal.StillRecording)]
    [InlineData(DeletionRefusal.RootUnavailable)]
    public void ADeletionThatNeverStartedNamesWhyInAClassRatherThanASentence(DeletionRefusal refusal)
    {
        DeletionResult result = DeletionResult.Refused(refusal);

        Assert.False(result.Deleted);
        Assert.Equal(refusal, result.Refusal);
        Assert.Empty(result.LeftBehind);
    }

    [Fact]
    public void ADeletionThatGotPartOfTheWayNamesWhatIsStillOnTheDisk()
    {
        DeletionResult result = DeletionResult.Refused(DeletionRefusal.PartialFailure, ["a.m2ts"]);

        Assert.False(result.Deleted);
        Assert.Equal(["a.m2ts"], result.LeftBehind);
    }

    [Fact]
    public void APartialFailureThatNamesNothingIsNotOne()
        => Assert.Throws<ArgumentException>(() => DeletionResult.Refused(DeletionRefusal.PartialFailure));

    [Fact]
    public void ARefusalThatNeverReachedAFileCannotHaveLeftOneBehind()
        => Assert.Throws<ArgumentException>(
            () => DeletionResult.Refused(DeletionRefusal.RootUnavailable, ["a.m2ts"]));

    [Fact]
    public void AReasonOutsideTheOnesThisTypeHoldsIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => DeletionResult.Refused((DeletionRefusal)9));
}
