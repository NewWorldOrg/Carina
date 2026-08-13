namespace Carina.Architecture.Tests;

public sealed class ReferenceRuleSelfCheckTests
{
    private static ProjectGraph ViolatingGraph() => ProjectGraph.FromNodes(
        new ProjectNode("Carina.Contracts", [], ["Microsoft.EntityFrameworkCore"]),
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
    public void DetectsADomainThatDependsOnAnythingBeyondTheContract()
    {
        var graph = ViolatingGraph();

        Assert.Equal(
            ["Carina.Domain", "Carina.Infrastructure"],
            graph.ForbiddenReferencesOf("Carina.Domain", "Carina.Contracts"));
        Assert.NotEmpty(graph.Node("Carina.Domain").PackageReferences);
    }

    [Fact]
    public void DetectsAContractThatTakesOnAPackage()
    {
        var contracts = ViolatingGraph().Node("Carina.Contracts");

        Assert.Empty(contracts.ProjectReferences);
        Assert.NotEmpty(contracts.PackageReferences);
    }

    [Fact]
    public void DetectsASourceFileThatNamesATransportDetail()
    {
        var directory = Directory.CreateTempSubdirectory("carina-source-scan-");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Clean.cs"),
                "namespace Sample; public sealed record Vocabulary(string Name);");
            File.WriteAllText(
                Path.Combine(directory.FullName, "Leaky.cs"),
                "namespace Sample; public static class Leak { public const string P = DriverEndpoints.Health; }");

            Assert.Equal(
                ["Leaky.cs"],
                SourceScan.FilesMentioning(directory.FullName, "DriverEndpoints", "StatusCode"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
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
