namespace Carina.Architecture.Tests;

public sealed class ReferenceRuleTests
{
    private static readonly ProjectGraph Graph = ProjectGraph.Load(RepositoryLayout.SourceDirectory);

    [Fact]
    public void DriverHasNoDomainReferenceAndNothingBeyondTheContract()
    {
        Assert.Empty(Graph.ForbiddenReferencesOf("Carina.Driver", "Carina.Contracts"));
    }

    [Fact]
    public void DomainReferencesTheIpcContractOnly()
    {
        Assert.Empty(Graph.ForbiddenReferencesOf("Carina.Domain", "Carina.Contracts"));
        Assert.Empty(Graph.Node("Carina.Domain").PackageReferences);
    }

    [Fact]
    public void TheDomainNamesNoTransportDetail()
    {
        Assert.Empty(SourceScan.FilesMentioning(
            Path.Combine(RepositoryLayout.SourceDirectory, "Carina.Domain"),
            "DriverEndpoints",
            "DriverJson",
            "HttpClient",
            "HttpRequestException",
            "StatusCode"));
    }

    [Fact]
    public void BroadcastDependsOnNothing()
    {
        ProjectNode broadcast = Graph.Node("Carina.Broadcast");

        Assert.Empty(broadcast.ProjectReferences);
        Assert.Empty(broadcast.PackageReferences);
    }

    [Fact]
    public void ContractsDependsOnNothing()
    {
        ProjectNode contracts = Graph.Node("Carina.Contracts");

        Assert.Empty(contracts.ProjectReferences);
        Assert.Empty(contracts.PackageReferences);
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
