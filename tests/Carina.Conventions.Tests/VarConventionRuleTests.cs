namespace Carina.Conventions.Tests;

public sealed class VarConventionRuleTests
{
    [Fact]
    public void ProductionCodeNamesWhatVarWouldHide()
    {
        Assert.Empty(VarConventionRules.NonApparentVarDeclarations(RepositoryPaths.SourceDirectory));
    }
}
