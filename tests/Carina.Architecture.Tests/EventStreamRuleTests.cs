namespace Carina.Architecture.Tests;

public sealed class EventStreamRuleTests
{
    private const string DriverProject = "Carina.Driver";

    private static readonly string[] AppSideProjects =
    [
        .. Directory
            .EnumerateDirectories(RepositoryLayout.SourceDirectory)
            .Select(directory => Path.GetFileName(directory))
            .Where(name => !string.Equals(name, DriverProject, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal),
    ];

    [Fact]
    public void NoAppSideEventStreamWritesAPayloadField()
    {
        Assert.Empty(AppSideProjects.SelectMany(project =>
            SourceScan.FilesMentioningAll(
                Path.Combine(RepositoryLayout.SourceDirectory, project),
                EventStreamContracts.ContentType,
                EventStreamContracts.PayloadField)));
    }

    [Fact]
    public void EveryProjectButTheDriverIsScanned()
    {
        Assert.Contains("Carina.Api", AppSideProjects);
        Assert.DoesNotContain(DriverProject, AppSideProjects);
        Assert.Equal(
            Directory.EnumerateDirectories(RepositoryLayout.SourceDirectory).Count() - 1,
            AppSideProjects.Length);
    }
}
