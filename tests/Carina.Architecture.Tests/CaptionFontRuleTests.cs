namespace Carina.Architecture.Tests;

public sealed class CaptionFontRuleTests
{
    private static string Dockerfile => File.ReadAllText(CaptionFontRules.Dockerfile);

    [Fact]
    public void TheImageFetchesOneFixedReleaseOfTheCaptionFaceChecksItTwiceAndInstallsItBeforeTheCacheIsBuilt()
    {
        Assert.Empty(CaptionFontRules.Violations(Dockerfile));
    }

    [Fact]
    public void TheRuleReadsTheVersionAndTheSha256TheDockerfileActuallyPins()
    {
        Assert.Equal("v3.2", CaptionFontRules.Version(Dockerfile));
        Assert.Equal(
            "c20f77bdad691223c50c43609df4b4e87916bfdc7d29fe7c4690f2fd6e72414c",
            CaptionFontRules.PinnedSha256(Dockerfile));
    }

    [Fact]
    public void TheLiveBuilderNamesTheFaceTheImageFetchesFirstAndTheFaceThePackageInstallsAfterIt()
    {
        string source = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Infrastructure",
            "Streaming",
            "FfmpegLiveInvocation.cs"));

        Assert.Contains(
            $"public const string Font = \"{CaptionFontRules.Face},{CaptionFontRules.FallbackFace}\";",
            source,
            StringComparison.Ordinal);
    }
}
