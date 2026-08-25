namespace Carina.Architecture.Tests;

public sealed class IntegrityRuleSelfCheckTests
{
    private const string TidiesUpAfterItself = """
        internal sealed class OrphanTidier
        {
            public void Tidy(string path) => File.Delete(path);
        }
        """;

    private const string TidiesUpThroughAnInfo = """
        internal sealed class OrphanTidier
        {
            public void Tidy(FileInfo found) => found.Delete();
        }
        """;

    private const string OnlyLooks = """
        internal sealed class OrphanFinder
        {
            public long Weigh(string path) => new FileInfo(path).Length;
        }
        """;

    private const string TruncatesAFile = """
        internal sealed class Trimmer
        {
            public void Trim(FileStream held) => held.SetLength(0);
        }
        """;

    private const string OpensForWriting = """
        internal sealed class Rewriter
        {
            public Stream Open(string path) => new FileStream(path, FileMode.Create);
        }
        """;

    [Fact]
    public void SeesAFileUnderTheFeatureThatDeletes()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Integrity/OrphanTidier.cs", TidiesUpAfterItself);
        tree.Write("Carina.Infrastructure/Integrity/OrphanFinder.cs", OnlyLooks);

        Assert.Equal(
            ["/Carina.Infrastructure/Integrity/OrphanTidier.cs"],
            IntegrityRules.FilesThatCouldDeleteSomething(tree.Root));
    }

    [Fact]
    public void SeesADeleteThatGoesThroughSomethingElseFirst()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Domain/Integrity/OrphanTidier.cs", TidiesUpThroughAnInfo);

        Assert.Equal(
            ["/Carina.Domain/Integrity/OrphanTidier.cs"],
            IntegrityRules.FilesThatCouldDeleteSomething(tree.Root));
    }

    [Fact]
    public void SeesNoDeleteWhenThereIsNone()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Integrity/OrphanFinder.cs", OnlyLooks);

        Assert.Empty(IntegrityRules.FilesThatCouldDeleteSomething(tree.Root));
    }

    [Fact]
    public void LeavesADeleteThatSitsOutsideTheFeature()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Library/RecordingRemoval.cs", TidiesUpAfterItself);

        Assert.Empty(IntegrityRules.FilesThatCouldDeleteSomething(tree.Root));
    }

    [Fact]
    public void SeesAFileUnderTheFeatureThatTruncates()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Integrity/Trimmer.cs", TruncatesAFile);

        Assert.Equal(
            ["/Carina.Infrastructure/Integrity/Trimmer.cs"],
            IntegrityRules.FilesThatCouldWriteSomethingTheyMayNot(tree.Root));
    }

    [Fact]
    public void SeesAFileUnderTheFeatureThatOpensForWriting()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Integrity/Rewriter.cs", OpensForWriting);

        Assert.Equal(
            ["/Carina.Infrastructure/Integrity/Rewriter.cs"],
            IntegrityRules.FilesThatCouldWriteSomethingTheyMayNot(tree.Root));
    }

    [Fact]
    public void SeesNoWriterWhenThereIsNone()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Integrity/OrphanFinder.cs", OnlyLooks);

        Assert.Empty(IntegrityRules.FilesThatCouldWriteSomethingTheyMayNot(tree.Root));
    }

    [Fact]
    public void LeavesAWriterThatSitsOutsideTheFeature()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Collection/GuideCache.cs", OpensForWriting);

        Assert.Empty(IntegrityRules.FilesThatCouldWriteSomethingTheyMayNot(tree.Root));
    }

    [Fact]
    public void SeesAWriterWhateverTheFileUnderTheFeatureIsCalled()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Integrity/IntegrityCheckJob.cs", OpensForWriting);

        Assert.Equal(
            ["/Carina.Infrastructure/Integrity/IntegrityCheckJob.cs"],
            IntegrityRules.FilesThatCouldWriteSomethingTheyMayNot(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-integrity-rules-");

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
