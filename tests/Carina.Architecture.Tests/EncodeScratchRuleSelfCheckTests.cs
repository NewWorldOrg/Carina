namespace Carina.Architecture.Tests;

public sealed class EncodeScratchRuleSelfCheckTests
{
    private const string Where = "Carina.Infrastructure/Encodings/EncodeScratchCleaner.cs";

    public static TheoryData<string, string> EveryOrdinaryWayOfWalkingADirectory => new()
    {
        { """foreach (string file in Directory.EnumerateFiles(room, "*.encoding")) File.Delete(file);""", "Directory.EnumerateFiles" },
        { """foreach (string file in Directory.GetFiles(room)) File.Delete(file);""", "Directory.GetFiles" },
        { """foreach (string entry in Directory.EnumerateFileSystemEntries(room)) File.Delete(entry);""", "Directory.EnumerateFileSystemEntries" },
        { """foreach (FileInfo file in room.GetFiles("*.encoding")) file.Delete();""", ".GetFiles(" },
        { """var options = new EnumerationOptions { RecurseSubdirectories = true };""", "EnumerationOptions" },
        { """var matcher = new Matcher().AddInclude("**/*.encoding");""", "Matcher" },
        { """using var watcher = new FileSystemWatcher(room);""", "FileSystemWatcher" },
    };

    public static TheoryData<string> EveryWayOfWalkingThatWalksStraightPast =>
    [
        """foreach (string file in survey.ListAsync(root).Files) File.Delete(file);""",
        """ProcessStartInfo find = AnotherProgramme.Describe("find", [room, "-name", "*.encoding", "-delete"]);""",
        """foreach (string file in listing) File.Delete(file);""",
        """string[] files = Walk(room);""",
    ];

    [Theory]
    [MemberData(nameof(EveryOrdinaryWayOfWalkingADirectory))]
    public void DetectsThisWayOfWalkingADirectory(string source, string reported)
    {
        using var tree = new SourceTree();
        tree.Write(Where, source);

        Assert.Equal([$"/{Where} {reported}"], EncodeScratchRules.WhatWalksADirectory(tree.Root));
    }

    [Fact]
    public void DetectsADirectoryInfoBothWhenItIsMadeAndWhenItIsWalked()
    {
        using var tree = new SourceTree();
        tree.Write(Where, """foreach (FileInfo file in new DirectoryInfo(room).EnumerateFiles()) file.Delete();""");

        Assert.Equal(
            [$"/{Where} .EnumerateFiles(", $"/{Where} newDirectoryInfo"],
            EncodeScratchRules.WhatWalksADirectory(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfWalkingThatWalksStraightPast))]
    public void CannotSeeThisWayOfWalkingADirectory(string source)
    {
        using var tree = new SourceTree();
        tree.Write(Where, source);

        Assert.Empty(EncodeScratchRules.WhatWalksADirectory(tree.Root));
    }

    [Theory]
    [InlineData("""File.Delete(path);""", "File.Delete")]
    [InlineData("""Directory.Delete(path, recursive: true);""", "Directory.Delete")]
    [InlineData("""new FileInfo(path).Delete();""", ".Delete(")]
    [InlineData("""await context.Set<EncodeScratchFile>().ExecuteDeleteAsync(ct);""", "ExecuteDeleteAsync(")]
    public void DetectsThisWayOfDeleting(string source, string reported)
    {
        using var tree = new SourceTree();
        tree.Write(Where, source);

        Assert.Equal([$"/{Where} {reported}"], EncodeScratchRules.WhatDeletes(tree.Root));
    }

    [Fact]
    public void ReadsOnlyTheFeatureFolderAndLeavesTheRestOfTheTreeAlone()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Integrity/LocalRecordingFileSurvey.cs", """foreach (string file in Directory.EnumerateFiles(root)) { }""");
        tree.Write("Carina.Infrastructure/Recordings/Eraser.cs", """File.Delete(path);""");

        Assert.Empty(EncodeScratchRules.WhatWalksADirectory(tree.Root));
        Assert.Empty(EncodeScratchRules.WhatDeletes(tree.Root));
        Assert.Empty(EncodeScratchRules.FilesInTheFeature(tree.Root));
    }

    [Fact]
    public void ReadsTheDomainHalfOfTheFeatureAsWellAsTheInfrastructureHalf()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Domain/Encodings/EncodeJob.cs", """string[] files = Directory.GetFiles(room);""");

        Assert.Equal(["/Carina.Domain/Encodings/EncodeJob.cs Directory.GetFiles"], EncodeScratchRules.WhatWalksADirectory(tree.Root));
    }

    [Fact]
    public void ReadsNothingOutOfBuildOutput()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/obj/Encodings/Generated.cs", """File.Delete(path);""");

        Assert.Empty(EncodeScratchRules.WhatDeletes(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-encode-scratch-rules-");

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
