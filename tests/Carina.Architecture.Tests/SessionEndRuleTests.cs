namespace Carina.Architecture.Tests;

public sealed class SessionEndRuleTests
{
    [Fact]
    public void BRKD009_NothingMovesASessionEndEarlierThanTheOneItWasGiven()
    {
        Assert.Empty(SessionEndRules.CallersThatMoveAnEndEarlier(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void BRKD009_ThereIsNoLongerAWayToMoveASessionEndEarlierAtAll()
    {
        Assert.False(
            SessionEndRules.DeclaresTheMethod(RepositoryLayout.SourceDirectory),
            $"{SessionEndRules.WhereItWasDeclared} declares {SessionEndRules.TheOneWayAnEndMovesEarlier} again. A session now keeps the window it asked for whoever else lets go of the tuner, so bringing the method back is a decision to be made rather than a change to slip in.");
    }
}
