namespace Carina.Architecture.Tests;

public sealed class FfmpegArgumentRuleSelfCheckTests
{
    public static TheoryData<string, string> EveryWayOfPuttingAValueIntoAnArgument => new()
    {
        { """arguments.Add($"scale={size.Width}:{size.Height}");""", "{size.Width}" },
        { """arguments.Add($"-canvas_size {programme.Width}x{programme.Height}");""", "{programme.Width}" },
        { """arguments.Add($"-metadata title={programme.Name}");""", "{programme.Name}" },
        { """arguments.Add($"subtitles={path}");""", "{path}" },
        { """arguments.Add("scale=" + width);""", "+" },
        { """arguments.Add(width + "x" + height);""", "+" },
        { """arguments.Add(string.Concat("scale=", width));""", "string.Concat(" },
        { """arguments.Add(string.Join(',', steps));""", "string.Join(" },
        { """arguments.Add(string.Format(culture, "scale={0}", width));""", "string.Format(" },
        { """var filter = new StringBuilder("scale=");""", "newStringBuilder" },
    };

    public static TheoryData<string> EveryWayOfWritingItThatWalksStraightPast =>
    [
        """arguments.Add($@"-metadata title={programme.Name}");""",
        """"
        arguments.Add($$"""-metadata title={{programme.Name}}""");
        """",
        """arguments.Add(Filtering(programme));""",
        """arguments.AddRange(["-metadata", title]);""",
        """arguments.Add(steps.Aggregate((one, next) => one += next));""",
    ];

    [Theory]
    [MemberData(nameof(EveryWayOfPuttingAValueIntoAnArgument))]
    public void DetectsThisWayOfPuttingAValueIntoACommandLine(string source, string filler)
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Streaming/FfmpegLiveInvocation.cs", source);

        Assert.Contains(
            $"/Carina.Infrastructure/Streaming/FfmpegLiveInvocation.cs {filler}",
            FfmpegArgumentRules.WhatFillsACommandLine(tree.Root),
            StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryWayOfWritingItThatWalksStraightPast))]
    public void CannotSeeThisWayOfPuttingAValueIntoACommandLine(string source)
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Streaming/FfmpegLiveInvocation.cs", source);

        Assert.Empty(FfmpegArgumentRules.WhatFillsACommandLine(tree.Root));
    }

    [Fact]
    public void CannotSeeABuilderWhoseFileIsNamedAnythingElse()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Streaming/FfmpegCommand.cs",
            """arguments.Add($"-metadata title={programme.Name}");""");

        Assert.Empty(FfmpegArgumentRules.BuildersOfACommandLine(tree.Root));
        Assert.Empty(FfmpegArgumentRules.WhatFillsACommandLine(tree.Root));
    }

    [Fact]
    public void ReadsABuilderWhereverInTheTreeItSits()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Api/Live/StartUpInvocation.cs", """arguments.Add($"-r {rate}");""");

        Assert.Equal(["/Carina.Api/Live/StartUpInvocation.cs"], FfmpegArgumentRules.BuildersOfACommandLine(tree.Root));
        Assert.Equal(["/Carina.Api/Live/StartUpInvocation.cs {rate}"], FfmpegArgumentRules.WhatFillsACommandLine(tree.Root));
    }

    [Fact]
    public void ReadsTheSameHoleWrittenTwiceAsOne()
    {
        Assert.Equal(
            ["{width}"],
            FfmpegArgumentRules.WhatFillsACommandLineIn("""$"scale={width}:trunc({width}/dar/2)*2" """));
    }

    [Fact]
    public void LeavesABraceThatWasWrittenToBeAnActualBrace()
    {
        Assert.Empty(FfmpegArgumentRules.WhatFillsACommandLineIn("""$"drawtext=text='{{}}'" """));
    }

    [Fact]
    public void LeavesTextThatIsHandedOverAsOneArgumentOfItsOwn()
    {
        Assert.Empty(FfmpegArgumentRules.WhatFillsACommandLineIn("""["-i", source.Value]"""));
    }

    [Fact]
    public void DetectsACanvasBeingGivenASizeWhereverItIsWritten()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Api/Live/Subtitles.cs", """arguments.Add("-canvas_size");""");

        Assert.Equal(
            ["/Carina.Api/Live/Subtitles.cs -canvas_size"],
            FfmpegArgumentRules.WhatSetsASubtitleCanvas(tree.Root));
    }

    [Fact]
    public void DetectsAFontBeingNamedWhereverItIsWritten()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Api/Live/Subtitles.cs", """arguments.Add("-font");""");

        Assert.Equal(
            ["/Carina.Api/Live/Subtitles.cs -font"],
            FfmpegArgumentRules.WhatNamesAFont(tree.Root));
    }

    [Theory]
    [InlineData("""arguments.Add("-fontfile");""")]
    [InlineData("""arguments.Add("-fontsdir");""")]
    [InlineData("""// a non-font matter""")]
    public void DoesNotMistakeAnotherWordForAFontBeingNamed(string source)
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Api/Live/Subtitles.cs", source);

        Assert.Empty(FfmpegArgumentRules.WhatNamesAFont(tree.Root));
    }

    [Theory]
    [InlineData("""UseShellExecute = true,""", "UseShellExecute=true")]
    [InlineData("""new ProcessStartInfo("/bin/sh")""", "/bin/sh")]
    [InlineData("""new ProcessStartInfo("/bin/bash")""", "/bin/bash")]
    [InlineData("""start.Arguments = Joined(arguments);""", "Arguments=")]
    public void DetectsACommandBeingHandedOverAsOnePieceOfText(string source, string reported)
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Streaming/Transcoder.cs", source);

        Assert.Equal(
            [$"/Carina.Infrastructure/Streaming/Transcoder.cs {reported}"],
            FfmpegArgumentRules.WhatCouldMakeACommandBeReadAgainAsText(tree.Root));
    }

    [Fact]
    public void CannotSeeAShellReachedThroughSomethingItIsNotNamedBy()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Streaming/Transcoder.cs",
            """var start = new ProcessStartInfo(settings.Programme) { UseShellExecute = asked, };""");

        Assert.Empty(FfmpegArgumentRules.WhatCouldMakeACommandBeReadAgainAsText(tree.Root));
    }

    [Fact]
    public void ReadsNothingOutOfAnEmptyTree()
    {
        using var tree = new SourceTree();

        Assert.Empty(FfmpegArgumentRules.BuildersOfACommandLine(tree.Root));
        Assert.Empty(FfmpegArgumentRules.WhatFillsACommandLine(tree.Root));
        Assert.Empty(FfmpegArgumentRules.WhatSetsASubtitleCanvas(tree.Root));
        Assert.Empty(FfmpegArgumentRules.WhatNamesAFont(tree.Root));
        Assert.Empty(FfmpegArgumentRules.WhatCouldMakeACommandBeReadAgainAsText(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-ffmpeg-argument-rules-");

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
