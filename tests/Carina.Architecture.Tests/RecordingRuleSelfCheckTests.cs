namespace Carina.Architecture.Tests;

public sealed class RecordingRuleSelfCheckTests
{
    private const string ParsesSections = """
        using Carina.Broadcast.Tables;
        internal sealed class RecordingGuideReader
        {
            public void Saw(Section section) => EventInformationTable.Read(section);
        }
        """;

    private const string SubscribesToTheWatcher = """
        using Carina.Domain.Recordings;
        internal sealed class ProgrammeExtensionFollower
        {
            public void Saw(PresentChange change) => recording.Extend(change.EndsAt);
        }
        """;

    private const string WritesTheGuide = """
        using Carina.Domain.Recordings;
        internal sealed class RecordingProgrammeWriter(IProgrammeRepository programmes)
        {
            public Task SaveAsync(Programme programme) => programmes.SaveAsync(programme, default);
        }
        """;

    [Fact]
    public void DetectsARecordingFileThatReadsSectionsForItself()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Recordings/RecordingGuideReader.cs", ParsesSections);
        tree.Write("Carina.Infrastructure/Recordings/ProgrammeExtensionFollower.cs", SubscribesToTheWatcher);

        Assert.Equal(
            ["/Carina.Infrastructure/Recordings/RecordingGuideReader.cs"],
            RecordingRules.EitReadersInsideTheRecordingFeature(tree.Root));
    }

    [Fact]
    public void DetectsASectionReaderInRecordingCodeThatSitsOutsideTheFeatureFolder()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Collection/RecordingRideAlong.cs",
            "using Carina.Domain.Recordings;\nprivate readonly SectionReader reader = new(0x12);");

        Assert.Equal(
            ["/Carina.Infrastructure/Collection/RecordingRideAlong.cs"],
            RecordingRules.EitReadersInsideTheRecordingFeature(tree.Root));
    }

    [Fact]
    public void LeavesTheGuideReadingItsOwnSections()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Collection/StreamHarvest.cs", ParsesSections);

        Assert.Empty(RecordingRules.EitReadersInsideTheRecordingFeature(tree.Root));
    }

    [Fact]
    public void DetectsARecordingFileThatWritesIntoTheGuide()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Recordings/RecordingProgrammeWriter.cs", WritesTheGuide);

        Assert.Equal(
            ["/Carina.Infrastructure/Recordings/RecordingProgrammeWriter.cs"],
            RecordingRules.GuideWritersInsideTheRecordingFeature(tree.Root));
    }

    [Fact]
    public void DetectsARecordingFileThatWritesIntoTheGuideInSql()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Recordings/RecordingBackfill.cs",
            """
            using Carina.Domain.Recordings;
            const string Sql = "UPDATE programme SET end_at = @endAt WHERE event_id = @eventId";
            """);

        Assert.Equal(
            ["/Carina.Infrastructure/Recordings/RecordingBackfill.cs"],
            RecordingRules.GuideWritersInsideTheRecordingFeature(tree.Root));
    }

    [Fact]
    public void LeavesARecordingThatOnlyFollowsWhatTheWatcherSays()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Recordings/ProgrammeExtensionFollower.cs", SubscribesToTheWatcher);

        Assert.Empty(RecordingRules.GuideWritersInsideTheRecordingFeature(tree.Root));
        Assert.Empty(RecordingRules.EitReadersInsideTheRecordingFeature(tree.Root));
    }

    [Fact]
    public void ReadsTheRepositoryOnDisk()
    {
        Assert.NotEmpty(Directory.EnumerateFiles(
            Path.Combine(RepositoryLayout.SourceDirectory, "Carina.Domain", "Recordings"),
            "*.cs"));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-recording-rules-");

        public string Root => directory.FullName;

        public void Write(string path, string source)
        {
            string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, source);
        }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
