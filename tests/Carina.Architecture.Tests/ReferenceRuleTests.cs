namespace Carina.Architecture.Tests;

public sealed class ReferenceRuleTests
{
    private static readonly ProjectGraph Graph = ProjectGraph.Load(RepositoryLayout.SourceDirectory);

    [Fact]
    public void DriverReferencesContractsOnly()
    {
        Assert.Empty(Graph.ForbiddenReferencesOf("Carina.Driver", "Carina.Contracts"));
    }

    [Fact]
    public void DomainDependsOnNothing()
    {
        var domain = Graph.Node("Carina.Domain");

        Assert.Empty(domain.ProjectReferences);
        Assert.Empty(domain.PackageReferences);
    }

    [Fact]
    public void BroadcastDependsOnNothing()
    {
        var broadcast = Graph.Node("Carina.Broadcast");

        Assert.Empty(broadcast.ProjectReferences);
        Assert.Empty(broadcast.PackageReferences);
    }

    [Fact]
    public void ContractsHasNoProjectReferences()
    {
        Assert.Empty(Graph.Node("Carina.Contracts").ProjectReferences);
    }

    [Fact]
    public void InfrastructureDependsInwardsOnly()
    {
        Assert.Empty(Graph.ForbiddenReferencesOf(
            "Carina.Infrastructure",
            "Carina.Domain",
            "Carina.Broadcast",
            "Carina.Contracts"));
    }

    [Fact]
    public void ApiDependsInwardsOnly()
    {
        Assert.Empty(Graph.ForbiddenReferencesOf(
            "Carina.Api",
            "Carina.Domain",
            "Carina.Infrastructure",
            "Carina.Broadcast",
            "Carina.Contracts"));
    }

    [Fact]
    public void DbIsALeaf()
    {
        Assert.Empty(Graph.DependentsOf("Carina.Db"));
    }

    [Fact]
    public void EveryExpectedProjectIsPresent()
    {
        Assert.Equal(
            [
                "Carina.Api",
                "Carina.Broadcast",
                "Carina.Contracts",
                "Carina.Db",
                "Carina.Domain",
                "Carina.Driver",
                "Carina.Infrastructure",
            ],
            Graph.ProjectNames.Order(StringComparer.Ordinal));
    }
}
