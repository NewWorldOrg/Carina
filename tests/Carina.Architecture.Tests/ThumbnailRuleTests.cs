namespace Carina.Architecture.Tests;

public sealed class ThumbnailRuleTests
{
    [Fact]
    public void NothingNamedForThumbnailsReachesForARecordingsResult()
    {
        Assert.Empty(ThumbnailRules.WhatNamedForThumbnailsReachesForARecordingsResult(
            RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheOnlyFilesOutsideTheFeatureThatKnowItExistsAreWhereItIsBuilt()
    {
        Assert.Equal(
            [
                "/Carina.Infrastructure/Configuration/ThumbnailOptions.cs",
                "/Carina.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs",
                "/Carina.Api/Services/RecordingService.cs",
                "/Carina.Api/Responder/Recordings/RecordingDetailResponder.cs",
                "/Carina.Infrastructure/Recordings/DriverRecordingFileEraser.cs",
                "/Carina.Api/Playback/ScrubDelivery.cs",
                "/Carina.Api/Program.cs",
            ],
            ThumbnailRules.AllowedToNameTheMachinery);
        Assert.Empty(ThumbnailRules.FilesOutsideTheFeatureThatReachIntoIt(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheFeatureIsOnDiskForThoseTripWiresToRead()
    {
        IReadOnlyList<string> feature = ThumbnailRules.FilesInTheFeature(RepositoryLayout.SourceDirectory);

        Assert.Contains("/Carina.Domain/Thumbnails/ThumbnailPlan.cs", feature, StringComparer.Ordinal);
        Assert.Contains(
            "/Carina.Infrastructure/Thumbnails/FfmpegThumbnailRenderer.cs",
            feature,
            StringComparer.Ordinal);
        Assert.True(feature.Count >= 11, $"the trip wires read {feature.Count} file(s) of the feature");
    }

    [Fact]
    public void TheWiderScanReachesTheFilesNamedForThumbnailsOutsideTheFeatureFolderToo()
    {
        IReadOnlyList<string> named = ThumbnailRules.FilesNamedForThumbnails(RepositoryLayout.SourceDirectory);

        Assert.Contains("/Carina.Domain/Recordings/ThumbnailFault.cs", named, StringComparer.Ordinal);
        Assert.Contains("/Carina.Infrastructure/Configuration/ThumbnailOptions.cs", named, StringComparer.Ordinal);
        Assert.True(
            named.Count > ThumbnailRules.FilesInTheFeature(RepositoryLayout.SourceDirectory).Count,
            "the wider scan read no more than the feature folder did");
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
    public void EveryWayOfSayingHowARecordingEndedIsStillCalledWhatTheTripWireCallsIt(string named)
    {
        Assert.Contains(named, ThumbnailRules.WaysToSayHowARecordingEnded, StringComparer.Ordinal);
        Assert.Contains($"public void {named}(", Entity, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOnlyCallTheFeatureIsAllowedToMakeIsNotOneOfThem()
    {
        Assert.Contains("public void Illustrate(", Entity, StringComparison.Ordinal);
        Assert.DoesNotContain("Illustrate", ThumbnailRules.WaysToSayHowARecordingEnded, StringComparer.Ordinal);
    }

    [Fact]
    public void EveryTypeTheFeatureDeclaresIsOneTheTripWireWatches()
    {
        IReadOnlyList<string> declared = ThumbnailRules.TypesTheFeatureDeclares(RepositoryLayout.SourceDirectory);

        Assert.Equal(22, declared.Count);
        Assert.Contains("ThumbnailIntent", declared, StringComparer.Ordinal);
        Assert.Contains("ThumbnailRequest", declared, StringComparer.Ordinal);
        Assert.Contains("ThumbnailValidation", declared, StringComparer.Ordinal);
        Assert.Contains("IThumbnailRemaker", declared, StringComparer.Ordinal);
        Assert.Contains("IScrubFrames", declared, StringComparer.Ordinal);
        Assert.Contains("Scrubber", declared, StringComparer.Ordinal);
        Assert.Empty(declared.Except(ThumbnailRules.Machinery, StringComparer.Ordinal));
        Assert.Empty(ThumbnailRules.Machinery.Except(declared, StringComparer.Ordinal));
    }

    private static string Entity { get; } = File.ReadAllText(Path.Combine(
        RepositoryLayout.SourceDirectory,
        "Carina.Domain",
        "Recordings",
        "Recording.cs"));
}
