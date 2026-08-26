namespace Carina.Architecture.Tests;

public sealed class ThumbnailRuleSelfCheckTests
{
    private const string SaysHowItEnded = """
        using Carina.Domain.Recordings;
        internal sealed class ThumbnailFailureReporter
        {
            public void Failed(Recording recording)
                => recording.Settle(RecordingOutcome.Failed, 0, DateTime.UtcNow);
        }
        """;

    private const string SaysOnlyWhatThePictureIs = """
        using Carina.Domain.Recordings;
        internal sealed class ThumbnailWriter
        {
            public void Failed(Recording recording)
                => recording.Illustrate(ThumbnailState.Failed, ThumbnailFault.TimedOut);
        }
        """;

    private const string ReachesIntoTheFeature = """
        using Carina.Domain.Thumbnails;
        internal sealed class RecordingCompletion(IThumbnailRenderer renderer)
        {
            public Task DoneAsync() => renderer.RenderAsync(null!, default);
        }
        """;

    private const string NamesTheProgrammeRunner = """
        internal sealed class RecordingCompletion
        {
            public void DoneAsync() => FfmpegThumbnailRenderer.Draw();
        }
        """;

    [Fact]
    public void DetectsAThumbnailFileThatWritesHowTheRecordingEnded()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Thumbnails/ThumbnailFailureReporter.cs", SaysHowItEnded);

        Assert.Equal(
            ["/Carina.Infrastructure/Thumbnails/ThumbnailFailureReporter.cs .Settle("],
            ThumbnailRules.ThumbnailFilesThatSayHowARecordingEnded(tree.Root));
    }

    [Theory]
    [InlineData("recording.Note(detail);", ".Note(")]
    [InlineData("recording.Interrupt(fault, at);", ".Interrupt(")]
    [InlineData("recording.Resume(at);", ".Resume(")]
    [InlineData("recording.Abort(at);", ".Abort(")]
    [InlineData("recording.Measure(counters, positions, null, 0, at);", ".Measure(")]
    [InlineData("recording.Extend(endsAt);", ".Extend(")]
    [InlineData("recording.Wrote(written);", ".Wrote(")]
    [InlineData("recording.Acquire(tuner);", ".Acquire(")]
    [InlineData("recording . Settle (outcome, size, at);", ".Settle(")]
    public void DetectsEveryOtherWayOfSayingHowARecordingEnded(string source, string named)
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Domain/Thumbnails/Reaching.cs", source);

        Assert.Equal(
            [$"/Carina.Domain/Thumbnails/Reaching.cs {named}"],
            ThumbnailRules.ThumbnailFilesThatSayHowARecordingEnded(tree.Root));
    }

    [Fact]
    public void LeavesTheOneCallTheFeatureIsThereToMake()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Thumbnails/ThumbnailWriter.cs", SaysOnlyWhatThePictureIs);

        Assert.Empty(ThumbnailRules.ThumbnailFilesThatSayHowARecordingEnded(tree.Root));
    }

    [Fact]
    public void LeavesTheCompletionPathWritingItsOwnResult()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Recordings/RecordingCompletion.cs", SaysHowItEnded);

        Assert.Empty(ThumbnailRules.ThumbnailFilesThatSayHowARecordingEnded(tree.Root));
    }

    [Fact]
    public void DetectsSomethingOutsideTheFeatureCallingIntoIt()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Recordings/RecordingCompletion.cs", ReachesIntoTheFeature);

        Assert.Equal(
            ["/Carina.Infrastructure/Recordings/RecordingCompletion.cs"],
            ThumbnailRules.FilesOutsideTheFeatureThatReachIntoIt(tree.Root));
    }

    [Fact]
    public void DetectsSomethingOutsideTheFeatureNamingWhatRunsTheProgramme()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Api/Controllers/Recordings/StopRecordingAction.cs", NamesTheProgrammeRunner);

        Assert.Equal(
            ["/Carina.Api/Controllers/Recordings/StopRecordingAction.cs"],
            ThumbnailRules.FilesOutsideTheFeatureThatReachIntoIt(tree.Root));
    }

    [Fact]
    public void LeavesTheFeatureTalkingToItself()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Thumbnails/ThumbnailJob.cs", ReachesIntoTheFeature);

        Assert.Empty(ThumbnailRules.FilesOutsideTheFeatureThatReachIntoIt(tree.Root));
    }

    [Fact]
    public void LeavesTheTwoPlacesTheFeatureIsBuilt()
    {
        using var tree = new SourceTree();

        foreach (string allowed in ThumbnailRules.AllowedToNameTheMachinery)
        {
            tree.Write(allowed.TrimStart('/'), ReachesIntoTheFeature);
        }

        Assert.Empty(ThumbnailRules.FilesOutsideTheFeatureThatReachIntoIt(tree.Root));
    }

    [Fact]
    public void ReadsNothingOutOfAnEmptyTree()
    {
        using var tree = new SourceTree();

        Assert.Empty(ThumbnailRules.FilesInTheFeature(tree.Root));
        Assert.Empty(ThumbnailRules.ThumbnailFilesThatSayHowARecordingEnded(tree.Root));
        Assert.Empty(ThumbnailRules.FilesOutsideTheFeatureThatReachIntoIt(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-thumbnail-rules-");

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
