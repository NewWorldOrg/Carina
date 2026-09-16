namespace Carina.Architecture.Tests;

public sealed class EncodeCutRuleSelfCheckTests
{
    private const string InTheFolder = "Carina.Infrastructure/Encodings/FfmpegEncodeInvocation.cs";

    private const string NamedForIt = "Carina.Api/Services/EncodeService.cs";

    public static TheoryData<string, string> EveryOrdinaryWayOfShorteningAnOutput => new()
    {
        { """return ["-i", source, "-t", Rendered(programme), destination];""", "\"-t\"" },
        { """arguments.Add("-to");""", "\"-to\"" },
        { """return ["-fs", "1000000"];""", "\"-fs\"" },
        { """return ["-frames:v", "1"];""", "\"-frames:v\"" },
        { """return ["-frames", "1"];""", "\"-frames\"" },
        { """return ["-vframes", "1"];""", "\"-vframes\"" },
        { """return ["-aframes", "1"];""", "\"-aframes\"" },
        { """return ["-segment_time", "600"];""", "\"-segment_time\"" },
        { """return ["-segment_times", "600"];""", "\"-segment_times\"" },
        { """return ["-f", "segment", destination];""", "\"segment\"" },
        { """string filter = "trim=start=0:end=90";""", "trim=" },
        { """string filter = "atrim=start=0:end=90";""", "atrim=" },
        { """string filter = "select=between(t,0,90)";""", "between(t" },
        { """string filter = "concat=n=2:v=1:a=1";""", "concat" },
    };

    public static TheoryData<string> EveryWayOfNamingSomethingThatIsNotACut =>
    [
        """return ["-threads", threads, "-filter_threads", threads];""",
        """foreach (ChapterSegment segment in segments) { written.Append(segment.Starts); }""",
        """string joined = string.Concat(one, other);""",
        """return ["-f", "null", "-"];""",
        """return ["-map_chapters", ChaptersInput];""",
        """return ["-f", "ffmetadata", "-i", chapters];""",
    ];

    public static TheoryData<string> EveryWayOfShorteningThatWalksStraightPast =>
    [
        """string bound = "-" + "t"; arguments.Add(bound);""",
        """arguments.Add(Window);""",
        """return ["-t:v", "6"];""",
    ];

    [Theory]
    [MemberData(nameof(EveryOrdinaryWayOfShorteningAnOutput))]
    public void DetectsThisWayOfShorteningAnOutput(string source, string reported)
    {
        using var tree = new SourceTree();
        tree.Write(InTheFolder, source);

        Assert.Equal([$"/{InTheFolder} {reported}"], EncodeCutRules.WhatShortensAnOutput(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfNamingSomethingThatIsNotACut))]
    public void DoesNotReportThisAsAShortening(string source)
    {
        using var tree = new SourceTree();
        tree.Write(InTheFolder, source);

        Assert.Empty(EncodeCutRules.WhatShortensAnOutput(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfShorteningThatWalksStraightPast))]
    public void CannotSeeThisWayOfShorteningAnOutput(string source)
    {
        using var tree = new SourceTree();
        tree.Write(InTheFolder, source);

        Assert.Empty(EncodeCutRules.WhatShortensAnOutput(tree.Root));
    }

    [Fact]
    public void ReadsAFileNamedForTheFeatureWhereverItSitsAndNothingOutsideIt()
    {
        using var tree = new SourceTree();
        tree.Write(NamedForIt, """return ["-t", "600"];""");
        tree.Write("Carina.Api/Services/RecordingService.cs", """return ["-t", "600"];""");

        Assert.Equal([$"/{NamedForIt} \"-t\""], EncodeCutRules.WhatShortensAnOutput(tree.Root));
        Assert.Equal([$"/{NamedForIt}"], EncodeCutRules.FilesInTheFeature(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-encode-cut-rules-");

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
