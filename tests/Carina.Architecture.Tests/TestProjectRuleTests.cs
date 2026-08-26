namespace Carina.Architecture.Tests;

public sealed class TestProjectRuleTests
{
    private static readonly ProjectGraph Graph = ProjectGraph.Load(
        RepositoryLayout.SourceDirectory,
        RepositoryLayout.TestDirectory);

    [Fact]
    public void EveryProjectOnDiskIsOneTheSolutionBuilds()
    {
        Assert.Empty(ProjectGraph.ProjectsOutsideTheSolution(
            RepositoryLayout.SolutionFile,
            RepositoryLayout.SourceDirectory,
            RepositoryLayout.TestDirectory));
    }

    [Fact]
    public void NoTestProjectReferencesAnotherTestProject()
    {
        Assert.Empty(Graph.TestProjectsReferencingAnotherTestProject());
    }

    [Fact]
    public void TheSharedTestSupportReachesNoFurtherThanTheDomain()
    {
        Assert.Empty(Graph.ForbiddenReferencesOf(
            "Carina.TestSupport",
            "Carina.Domain",
            "Carina.Contracts"));
    }

    [Fact]
    public void TheSharedTestSupportIsWhereBothSidesOfTheSeamMeet()
    {
        Assert.Contains(
            "Carina.TestSupport",
            Graph.Node("Carina.Api.Tests").ProjectReferences,
            StringComparer.Ordinal);
        Assert.Contains(
            "Carina.TestSupport",
            Graph.Node("Carina.Infrastructure.Tests").ProjectReferences,
            StringComparer.Ordinal);
    }
}
