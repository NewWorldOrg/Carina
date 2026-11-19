namespace Carina.Conventions.Tests;

public sealed class CommentConventionRuleTests
{
    [Fact]
    public void ProductionCodeCarriesNoComment()
    {
        Assert.Empty(CommentConventionRules.CommentsInCode(RepositoryPaths.SourceDirectory));
    }

    [Fact]
    public void TestCodeCarriesNoComment()
    {
        Assert.Empty(CommentConventionRules.CommentsInCode(RepositoryPaths.TestDirectory));
    }
}
