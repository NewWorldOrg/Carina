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
    public void TheHubIsTheOnlyEventStreamTheAppServes()
    {
        Assert.Equal(
            ["Carina.Api/Events/AppEventStream.cs"],
            AppSideProjects
                .SelectMany(project => SourceScan
                    .FilesMentioning(Path.Combine(RepositoryLayout.SourceDirectory, project), EventStreamContracts.ContentType)
                    .Select(file => $"{project}/{file}"))
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void TheDriverHasAnEventStreamOfItsOwnWhichIsWhyTheRuleAboveStopsAtTheApp()
    {
        Assert.NotEmpty(SourceScan.FilesMentioning(
            Path.Combine(RepositoryLayout.SourceDirectory, DriverProject),
            EventStreamContracts.ContentType));
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
