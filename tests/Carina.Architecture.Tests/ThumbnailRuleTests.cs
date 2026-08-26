namespace Carina.Architecture.Tests;

public sealed class ThumbnailRuleTests
{
    [Fact]
    public void NothingThatDrawsAPictureSaysHowTheRecordingEnded()
    {
        Assert.Empty(ThumbnailRules.ThumbnailFilesThatSayHowARecordingEnded(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheOnlyFilesOutsideTheFeatureThatKnowItExistsAreWhereItIsBuilt()
    {
        Assert.Equal(
            [
                "/Carina.Infrastructure/Configuration/ThumbnailOptions.cs",
                "/Carina.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs",
            ],
            ThumbnailRules.AllowedToNameTheMachinery);
        Assert.Empty(ThumbnailRules.FilesOutsideTheFeatureThatReachIntoIt(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheFeatureIsOnDiskForThoseRulesToRead()
    {
        IReadOnlyList<string> feature = ThumbnailRules.FilesInTheFeature(RepositoryLayout.SourceDirectory);

        Assert.Contains("/Carina.Domain/Thumbnails/ThumbnailPlan.cs", feature, StringComparer.Ordinal);
        Assert.Contains(
            "/Carina.Infrastructure/Thumbnails/FfmpegThumbnailRenderer.cs",
            feature,
            StringComparer.Ordinal);
        Assert.True(feature.Count >= 8, $"the rules read {feature.Count} file(s) of the feature");
    }

    [Theory]
    [InlineData("Settle")]
    [InlineData("Note")]
    [InlineData("Interrupt")]
    [InlineData("Resume")]
    [InlineData("Abort")]
    [InlineData("Measure")]
    [InlineData("Extend")]
    [InlineData("Wrote")]
    [InlineData("Acquire")]
    public void EveryWayOfSayingHowARecordingEndedIsStillCalledWhatTheRuleCallsIt(string named)
        => Assert.Contains($"public void {named}(", Entity, StringComparison.Ordinal);

    [Fact]
    public void TheOneCallTheFeatureIsAllowedToMakeIsStillThere()
        => Assert.Contains("public void Illustrate(", Entity, StringComparison.Ordinal);

    private static string Entity { get; } = File.ReadAllText(Path.Combine(
        RepositoryLayout.SourceDirectory,
        "Carina.Domain",
        "Recordings",
        "Recording.cs"));
}
