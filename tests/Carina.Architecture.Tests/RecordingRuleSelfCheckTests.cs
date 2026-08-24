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

    private const string DeleteAction = """
        [ApiController]
        [Route("api/recordings/{recordingId:guid}")]
        public sealed class DeleteRecordingAction : ControllerBase
        {
            [HttpDelete]
            public Task<IActionResult> Invoke(Guid recordingId) => throw new NotImplementedException();
        }
        """;

    private const string ActiveAction = """
        [ApiController]
        [Route("api/recordings/active")]
        public sealed class GetActiveRecordingsAction : ControllerBase
        {
            [HttpGet]
            public Task<IActionResult> Invoke() => throw new NotImplementedException();
        }
        """;

    private const string LibraryDeleteAction = """
        using Carina.Domain.Recordings;
        [ApiController]
        [Route("api/library/recordings/{recordingId:guid}")]
        public sealed class DeleteLibraryRecordingAction : ControllerBase
        {
            [HttpDelete]
            public Task<IActionResult> Invoke(RecordingId recordingId) => throw new NotImplementedException();
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
    public void DetectsADeleteEndpointOnTheRecordingSurface()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Api/Controllers/Recordings/DeleteRecordingAction.cs", DeleteAction);
        tree.Write("Carina.Api/Controllers/Recordings/GetActiveRecordingsAction.cs", ActiveAction);

        Assert.Equal(
            ["/Carina.Api/Controllers/Recordings/DeleteRecordingAction.cs"],
            RecordingRules.DeleteEndpointsOnTheRecordingSurface(tree.Root));
    }

    [Fact]
    public void DetectsADeleteRoutedAtRecordingsFromAFolderThatDoesNotSaySo()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Api/Controllers/Maintenance/PurgeAction.cs", DeleteAction);

        Assert.Equal(
            ["/Carina.Api/Controllers/Maintenance/PurgeAction.cs"],
            RecordingRules.DeleteEndpointsOnTheRecordingSurface(tree.Root));
    }

    [Fact]
    public void LeavesTheLibraryOwningTheDeletion()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Api/Controllers/Library/DeleteLibraryRecordingAction.cs", LibraryDeleteAction);

        Assert.Empty(RecordingRules.DeleteEndpointsOnTheRecordingSurface(tree.Root));
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
