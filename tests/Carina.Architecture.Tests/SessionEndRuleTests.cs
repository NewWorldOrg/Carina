namespace Carina.Architecture.Tests;

public sealed class SessionEndRuleTests
{
    [Fact]
    public void BRKD009_TheSeatSwapIsTheOnlyPlaceThatMovesASessionEndEarlierAndItDoesSoOnce()
    {
        Assert.Equal(
            [new SessionEndCaller(SessionEndRules.WhereItIsCalled, 1)],
            SessionEndRules.CallersThatMoveAnEndEarlier(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void BRKD009_TheOneMethodThatMovesAnEndEarlierIsStillDeclaredWhereTheRuleExpects()
    {
        Assert.True(
            SessionEndRules.DeclaresTheMethod(RepositoryLayout.SourceDirectory),
            $"{SessionEndRules.WhereItIsDeclared} no longer declares {SessionEndRules.TheOneWayAnEndMovesEarlier}, so the census above counts calls to nothing.");
    }
}
