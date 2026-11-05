using System.Xml.Linq;

namespace Carina.Architecture.Tests;

public sealed class ReferenceRuleTests
{
    private const string Contract = "Puppis.Contracts";

    private const string Driver = "Puppis.Driver";

    private static readonly ProjectGraph Graph = ProjectGraph.Load(RepositoryLayout.SourceDirectory);

    [Fact]
    public void DomainReferencesTheContractPackageAndNothingElse()
    {
        ProjectNode domain = Graph.Node("Carina.Domain");

        Assert.Empty(domain.ProjectReferences);
        Assert.Equal([Contract], domain.PackageReferences);
    }

    [Theory]
    [InlineData(Contract)]
    [InlineData(Driver)]
    public void EachPuppisPackageIsPinnedOnceAtTheRootAndEveryProjectTakesThatVersion(string package)
    {
        XDocument pinned = XDocument.Load(Path.Combine(RepositoryLayout.Root, "Directory.Packages.props"));

        Assert.Single(
            pinned.Descendants("PackageVersion"),
            version => (string?)version.Attribute("Include") == package);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(RepositoryLayout.SourceDirectory, "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(RepositoryLayout.TestDirectory, "*.csproj", SearchOption.AllDirectories)),
            project => XDocument.Load(project)
                .Descendants("PackageReference")
                .Any(reference => (string?)reference.Attribute("Include") == package
                    && (reference.Attribute("Version") is not null || reference.Attribute("VersionOverride") is not null)));
    }

    [Fact]
    public void TheDriverAndTheContractArePinnedToOneVersion()
    {
        XDocument pinned = XDocument.Load(Path.Combine(RepositoryLayout.Root, "Directory.Packages.props"));

        Assert.Equal(
            PinnedVersion(pinned, Contract),
            PinnedVersion(pinned, Driver));
    }

    private static string? PinnedVersion(XDocument pinned, string package)
        => (string?)pinned.Descendants("PackageVersion")
            .Single(version => (string?)version.Attribute("Include") == package)
            .Attribute("Version");

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
    public void InfrastructureDependsInwardsOnly()
    {
        Assert.Empty(Graph.ForbiddenReferencesOf(
            "Carina.Infrastructure",
            "Carina.Domain",
            "Carina.Broadcast"));
    }

    [Fact]
    public void ApiDependsInwardsOnly()
    {
        Assert.Empty(Graph.ForbiddenReferencesOf(
            "Carina.Api",
            "Carina.Domain",
            "Carina.Infrastructure",
            "Carina.Broadcast"));
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
                "Carina.Db",
                "Carina.Domain",
                "Carina.Infrastructure",
            ],
            Graph.ProjectNames.Order(StringComparer.Ordinal));
    }
}
