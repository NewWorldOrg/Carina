namespace Carina.Architecture.Tests;

public sealed class FfmpegArgumentRuleTests
{
    private static readonly string[] Builders =
    [
        "/Carina.Infrastructure/Streaming/FfmpegCaptionInvocation.cs",
        "/Carina.Infrastructure/Streaming/FfmpegLiveInvocation.cs",
        "/Carina.Infrastructure/Streaming/FfmpegPlaybackInvocation.cs",
        "/Carina.Infrastructure/Streaming/FfprobeInvocation.cs",
        "/Carina.Infrastructure/Streaming/VaapiProbeInvocation.cs",
        "/Carina.Infrastructure/Thumbnails/FfmpegInvocation.cs",
    ];

    private static readonly string[] Inventory =
    [
        "/Carina.Infrastructure/Streaming/FfmpegCaptionInvocation.cs {programNumber}",
        "/Carina.Infrastructure/Streaming/FfmpegCaptionInvocation.cs {size.Height}",
        "/Carina.Infrastructure/Streaming/FfmpegCaptionInvocation.cs {size.Width}",
        "/Carina.Infrastructure/Streaming/FfmpegLiveInvocation.cs string.Join(",
        "/Carina.Infrastructure/Streaming/FfmpegLiveInvocation.cs {kilobitsPerSecond}",
        "/Carina.Infrastructure/Streaming/FfmpegLiveInvocation.cs {programNumber}",
        "/Carina.Infrastructure/Streaming/FfmpegLiveInvocation.cs {size.Height}",
        "/Carina.Infrastructure/Streaming/FfmpegLiveInvocation.cs {size.Width}",
        "/Carina.Infrastructure/Streaming/FfmpegPlaybackInvocation.cs {SoundKilobitsPerSecond}",
        "/Carina.Infrastructure/Streaming/FfmpegPlaybackInvocation.cs {programNumber}",
        "/Carina.Infrastructure/Thumbnails/FfmpegInvocation.cs {programNumber}",
        "/Carina.Infrastructure/Thumbnails/FfmpegInvocation.cs {width}",
    ];

    private static readonly string[] WordsForTextSomebodyElseWrote =
    [
        "Programme",
        "programme",
        "Channel",
        "channel",
        "Service",
        "service",
        "Title",
        "title",
        "Name",
        "name",
        "Summary",
        "summary",
        "Description",
        "description",
        "User",
        "user",
        "Query",
        "query",
        "Request",
        "request",
    ];

    private static IEnumerable<string> Fillers
        => Inventory.Select(entry => entry[(entry.IndexOf(' ', StringComparison.Ordinal) + 1)..]);

    [Fact]
    public void EveryPlaceThisRepositoryBuildsACommandLineForAnotherProgrammeIsOneOfThese()
    {
        Assert.Equal(Builders, FfmpegArgumentRules.BuildersOfACommandLine(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void EveryValueThatReachesACommandLineIsWrittenDownHere()
    {
        Assert.Equal(Inventory, FfmpegArgumentRules.WhatFillsACommandLine(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void NothingFillingACommandLineIsNamedForTextSomebodyElseWrote()
    {
        Assert.All(
            Fillers,
            filler => Assert.DoesNotContain(
                WordsForTextSomebodyElseWrote,
                word => filler.Contains(word, StringComparison.Ordinal)));
    }

    [Fact]
    public void NoCommandLineIsBuiltByAddingOnePieceOfTextToAnother()
    {
        Assert.DoesNotContain("+", Fillers);
    }

    [Fact]
    public void TheOnlyThingPutTogetherIsTheFilterChainAndItIsPutTogetherOutOfSteps()
    {
        Assert.Equal(
            ["/Carina.Infrastructure/Streaming/FfmpegLiveInvocation.cs string.Join("],
            Inventory.Where(entry => entry.EndsWith("string.Join(", StringComparison.Ordinal)).ToArray());

        string source = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Infrastructure",
            "Streaming",
            "FfmpegLiveInvocation.cs"));

        Assert.Contains("return string.Join(',', steps);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOnePlaceThatAsksAStreamAboutItselfFillsNothingIn()
    {
        Assert.DoesNotContain(Inventory, entry => entry.Contains("Ffprobe", StringComparison.Ordinal));
    }

    [Fact]
    public void TheOnlyPlaceThatNamesASubtitleCanvasIsTheCaptionBuilderAndItFillsItFromAMeasuredSizeAlone()
    {
        Assert.Equal(
            ["/Carina.Infrastructure/Streaming/FfmpegCaptionInvocation.cs -canvas_size"],
            FfmpegArgumentRules.WhatSetsASubtitleCanvas(RepositoryLayout.SourceDirectory));

        string source = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Infrastructure",
            "Streaming",
            "FfmpegCaptionInvocation.cs"));

        Assert.Contains("internal static string Canvas(VideoSize size)", source, StringComparison.Ordinal);
        Assert.Equal(
            ["{programNumber}", "{size.Height}", "{size.Width}"],
            FfmpegArgumentRules.WhatFillsACommandLineIn(source));
    }

    [Fact]
    public void TheOnlyPlaceThatNamesAFontIsTheCaptionBuilderAndItNamesTheOneFaceTheImageInstallsAsAConstant()
    {
        Assert.Equal(
            ["/Carina.Infrastructure/Streaming/FfmpegCaptionInvocation.cs -font"],
            FfmpegArgumentRules.WhatNamesAFont(RepositoryLayout.SourceDirectory));

        string source = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Infrastructure",
            "Streaming",
            "FfmpegCaptionInvocation.cs"));

        Assert.Contains("public const string Font = \"Noto Sans CJK JP\";", source, StringComparison.Ordinal);
        Assert.Contains("\"-font\",\n            Font,", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoCommandIsHandedOverAsOnePieceOfTextForSomethingElseToReadAgain()
    {
        Assert.Empty(FfmpegArgumentRules.WhatCouldMakeACommandBeReadAgainAsText(RepositoryLayout.SourceDirectory));
    }
}
