namespace Carina.Conventions.Tests;

public sealed class NestingConventionRuleTests
{
    [Fact]
    public void ProductionCodeJoinsNoMoreThanTwoConditionalsInOneExpression()
    {
        Assert.Empty(NestingConventionRules.ConditionalsJoinedPastTheLimit(RepositoryPaths.SourceDirectory));
    }

    [Fact]
    public void TestCodeJoinsNoMoreThanTwoConditionalsInOneExpression()
    {
        Assert.Empty(NestingConventionRules.ConditionalsJoinedPastTheLimit(RepositoryPaths.TestDirectory));
    }

    [Fact]
    public void ProductionCodeNestsNoMoreThanThreeBlocksInOneFunction()
    {
        Assert.Empty(NestingConventionRules.BlocksNestedPastTheLimit(RepositoryPaths.SourceDirectory));
    }

    [Fact]
    public void TestCodeNestsNoMoreThanThreeBlocksInOneFunction()
    {
        Assert.Empty(NestingConventionRules.BlocksNestedPastTheLimit(RepositoryPaths.TestDirectory));
    }
}
