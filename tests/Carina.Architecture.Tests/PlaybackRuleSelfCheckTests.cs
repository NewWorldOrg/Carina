namespace Carina.Architecture.Tests;

public sealed class PlaybackRuleSelfCheckTests
{
    public static TheoryData<string> EveryWayOfTranscodingWhilePlaying
    {
        get
        {
            var named = new TheoryData<string>();

            foreach (string machinery in PlaybackRules.WaysToTranscodeWhilePlaying)
            {
                named.Add(machinery);
            }

            return named;
        }
    }

    [Fact]
    public void DetectsASecondFileSpellingTheDeliveryPath()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Api/Playback/VideoDelivery.cs", "public const string Path = \"/api/videos/{id}\";");
        tree.Write("Carina.Api/Controllers/Recordings/GetRecordingAction.cs", "string at = $\"/api/videos/{id}\";");

        Assert.Equal(
            [
                "/Carina.Api/Controllers/Recordings/GetRecordingAction.cs",
                "/Carina.Api/Playback/VideoDelivery.cs",
            ],
            PlaybackRules.FilesSpellingTheDeliveryPath(tree.Root));
    }

    [Fact]
    public void DetectsASecondFileReachingForTheDelivery()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Api/Playback/VideoDelivery.cs", "public static class VideoDelivery;");
        tree.Write("Carina.Api/Responder/Recordings/RecordingResponder.cs", "VideoDelivery.Path;");

        Assert.Equal(
            [
                "/Carina.Api/Playback/VideoDelivery.cs",
                "/Carina.Api/Responder/Recordings/RecordingResponder.cs",
            ],
            PlaybackRules.FilesNamingTheDelivery(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfTranscodingWhilePlaying))]
    public void DetectsTheDeliveryReachingForATranscoder(string machinery)
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Api/Playback/VideoDelivery.cs", $"internal sealed class Handing({machinery} making);");

        Assert.Equal(
            ["/Carina.Api/Playback/VideoDelivery.cs"],
            PlaybackRules.WhatTheDeliveryTranscodes(tree.Root));
    }

    [Fact]
    public void LeavesTheLivePathTranscodingWhereItBelongs()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Streaming/LiveTranscoderFactory.cs",
            "internal sealed class LiveTranscoderFactory : ILiveTranscoderFactory;");

        Assert.Empty(PlaybackRules.WhatTheDeliveryTranscodes(tree.Root));
    }

    [Fact]
    public void CannotSeeThePathPutTogetherOutOfPieces()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Api/Controllers/Recordings/GetRecordingAction.cs",
            "string at = \"/api/\" + \"videos/\" + id;");

        Assert.Empty(PlaybackRules.FilesSpellingTheDeliveryPath(tree.Root));
    }

    [Fact]
    public void CannotSeeAPlayerOutsideThisRepositoryAskingForThePath()
    {
        using var tree = new SourceTree();
        tree.Write("web/player.ts", "const source = `/api/videos/${id}`;");

        Assert.Empty(PlaybackRules.FilesSpellingTheDeliveryPath(tree.Root));
        Assert.Empty(PlaybackRules.FilesNamingTheDelivery(tree.Root));
    }

    [Fact]
    public void ReadsNothingOutOfAnEmptyTree()
    {
        using var tree = new SourceTree();

        Assert.Empty(PlaybackRules.FilesSpellingTheDeliveryPath(tree.Root));
        Assert.Empty(PlaybackRules.FilesNamingTheDelivery(tree.Root));
        Assert.Empty(PlaybackRules.WhatTheDeliveryTranscodes(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-playback-rules-");

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
