namespace Carina.Architecture.Tests;

public sealed class EventStreamRuleTests
{
    private static readonly string[] Projects =
    [
        .. Directory
            .EnumerateDirectories(RepositoryLayout.SourceDirectory)
            .Select(directory => Path.GetFileName(directory))
            .Order(StringComparer.Ordinal),
    ];

    [Fact]
    public void NoEventStreamWritesAPayloadField()
    {
        Assert.Empty(Projects.SelectMany(project =>
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
            Projects
                .SelectMany(project => SourceScan
                    .FilesMentioning(Path.Combine(RepositoryLayout.SourceDirectory, project), EventStreamContracts.ContentType)
                    .Select(file => $"{project}/{file}"))
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void EveryProjectIsScanned()
    {
        Assert.Contains("Carina.Api", Projects);
        Assert.Equal(
            Directory.EnumerateDirectories(RepositoryLayout.SourceDirectory).Count(),
            Projects.Length);
    }
}
