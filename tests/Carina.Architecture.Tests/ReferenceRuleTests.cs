namespace Carina.Architecture.Tests;

/// <summary>
/// Structural rules that the two release streams and the layering depend on.
/// A violation here is a build break, not a review comment.
/// </summary>
public sealed class ReferenceRuleTests
{
    private static readonly ProjectGraph Graph = ProjectGraph.Load(RepositoryLayout.SourceDirectory);

    // The privileged process ships on its own tag and must keep running while the app
    // is replaced. Reaching into the app's layers would tie the two together again.
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

    // The parsing library is exercised by large table-driven tests over fixed
    // fixtures, which only stays possible while it has nothing to wire up.
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

    // Migrations are applied by their own process; nothing may take a dependency on
    // that entry point and drag the tooling into the served processes.
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
