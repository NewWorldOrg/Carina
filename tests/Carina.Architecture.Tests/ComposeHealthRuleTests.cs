namespace Carina.Architecture.Tests;

public sealed class ComposeHealthRuleTests
{
    [Fact]
    public void BothHostsAreAskedWhetherTheyAnswerAndNothingDependsOnTheDriver()
    {
        Assert.Empty(ComposeHealthRules.Violations(File.ReadAllText(ComposeHealthRules.ComposeFile)));
    }

    [Fact]
    public void TheRuleReadsTheServicesTheComposeFileActuallyHas()
    {
        Assert.Equal(
            ["app", "db", "driver"],
            ComposeHealthRules.ServiceNames(File.ReadAllText(ComposeHealthRules.ComposeFile)).Order(StringComparer.Ordinal));
    }
}
