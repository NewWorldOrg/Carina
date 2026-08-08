namespace Carina.Architecture.Tests;

/// <summary>
/// Checks the rule engine itself against a graph that violates the rules, so that
/// the green result of <see cref="ReferenceRuleTests"/> means the rules hold rather
/// than that the checks silently inspect nothing.
/// </summary>
public sealed class ReferenceRuleSelfCheckTests
{
    private static ProjectGraph ViolatingGraph() => ProjectGraph.FromNodes(
        new ProjectNode("Carina.Contracts", [], []),
        new ProjectNode("Carina.Domain", ["Carina.Infrastructure"], ["Microsoft.EntityFrameworkCore"]),
        new ProjectNode("Carina.Infrastructure", ["Carina.Domain"], []),
        new ProjectNode("Carina.Db", [], []),
        new ProjectNode("Carina.Api", ["Carina.Db"], []),
        new ProjectNode("Carina.Driver", ["Carina.Contracts", "Carina.Infrastructure"], []));

    [Fact]
    public void DetectsADriverThatReachesTheDomain()
    {
        var forbidden = ViolatingGraph().ForbiddenReferencesOf("Carina.Driver", "Carina.Contracts");

        Assert.Equal(["Carina.Domain", "Carina.Infrastructure"], forbidden);
    }

    [Fact]
    public void DetectsADomainThatDependsOnAnything()
    {
        var domain = ViolatingGraph().Node("Carina.Domain");

        Assert.NotEmpty(domain.ProjectReferences);
        Assert.NotEmpty(domain.PackageReferences);
    }

    [Fact]
    public void DetectsAMigrationProjectThatIsReferenced()
    {
        Assert.Equal(["Carina.Api"], ViolatingGraph().DependentsOf("Carina.Db"));
    }

    [Fact]
    public void ReadsTheProjectFilesOnDisk()
    {
        var graph = ProjectGraph.Load(RepositoryLayout.SourceDirectory);

        Assert.Contains("Carina.Contracts", graph.Node("Carina.Driver").ProjectReferences, StringComparer.Ordinal);
        Assert.Contains("Carina.Domain", graph.TransitiveReferencesOf("Carina.Api"));
    }
}
