namespace Carina.Architecture.Tests;

public sealed class ReferenceRuleSelfCheckTests
{
    private static ProjectGraph ViolatingGraph() => ProjectGraph.FromNodes(
        new ProjectNode("Carina.Domain", ["Carina.Infrastructure"], ["Puppis.Contracts", "Microsoft.EntityFrameworkCore"]),
        new ProjectNode("Carina.Infrastructure", ["Carina.Domain"], []),
        new ProjectNode("Carina.Db", [], []),
        new ProjectNode("Carina.Api", ["Carina.Db"], []));

    [Fact]
    public void DetectsAProjectOnDiskTheSolutionDoesNotList()
    {
        DirectoryInfo held = Directory.CreateTempSubdirectory();

        try
        {
            Laid(held, "Carina.Kept.Tests");
            Laid(held, "Carina.Forgotten.Tests");

            string solution = Path.Combine(held.FullName, "Carina.slnx");
            File.WriteAllText(
                solution,
                """
                <Solution>
                  <Project Path="tests/Carina.Kept.Tests/Carina.Kept.Tests.csproj" />
                </Solution>
                """);

            Assert.Equal(
                ["tests/Carina.Forgotten.Tests/Carina.Forgotten.Tests.csproj"],
                ProjectGraph.ProjectsOutsideTheSolution(solution, Path.Combine(held.FullName, "tests")));
        }
        finally
        {
            held.Delete(recursive: true);
        }
    }

    private static void Laid(DirectoryInfo held, string project)
    {
        string directory = Path.Combine(held.FullName, "tests", project);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{project}.csproj"), "<Project />");
    }

    [Fact]
    public void DetectsADomainThatDependsOnAnythingBeyondTheContract()
    {
        ProjectGraph graph = ViolatingGraph();

        Assert.Equal(["Carina.Domain", "Carina.Infrastructure"], graph.ForbiddenReferencesOf("Carina.Domain"));
        Assert.NotEqual(["Puppis.Contracts"], graph.Node("Carina.Domain").PackageReferences);
    }

    [Fact]
    public void DetectsASourceFileThatNamesATransportDetail()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-source-scan-");

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
    public void DetectsATestProjectThatBorrowsFromAnotherTestProject()
    {
        var graph = ProjectGraph.FromNodes(
            new ProjectNode("Carina.TestSupport", ["Carina.Domain"], []),
            new ProjectNode("Carina.Domain", [], []),
            new ProjectNode("Carina.Api.Tests", ["Carina.Infrastructure.Tests"], []),
            new ProjectNode("Carina.Infrastructure.Tests", ["Carina.TestSupport"], []));

        Assert.Equal(
            ["Carina.Api.Tests -> Carina.Infrastructure.Tests"],
            graph.TestProjectsReferencingAnotherTestProject());
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

        Assert.Equal(["Puppis.Contracts"], graph.Node("Carina.Domain").PackageReferences);
        Assert.Contains("Carina.Domain", graph.TransitiveReferencesOf("Carina.Api"));
    }
}
