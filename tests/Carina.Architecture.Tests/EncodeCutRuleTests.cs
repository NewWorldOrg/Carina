namespace Carina.Architecture.Tests;

public sealed class EncodeCutRuleTests
{
    [Fact(DisplayName = "nothing in the encode feature shortens what it writes, so a break that was marked leaves the artefact the length it would have been had nothing been marked")]
    public void NothingInTheEncodeFeatureShortensWhatItWrites()
        => Assert.Empty(EncodeCutRules.WhatShortensAnOutput(RepositoryLayout.SourceDirectory));

    [Fact]
    public void TheFeatureIsOnDiskForThatTripWireToRead()
    {
        IReadOnlyList<string> feature = EncodeCutRules.FilesInTheFeature(RepositoryLayout.SourceDirectory);

        Assert.Contains("/Carina.Infrastructure/Encodings/FfmpegEncodeInvocation.cs", feature, StringComparer.Ordinal);
        Assert.Contains("/Carina.Infrastructure/Encodings/EncodeJobRunner.cs", feature, StringComparer.Ordinal);
        Assert.Contains("/Carina.Infrastructure/Encodings/ChapterMetadataFile.cs", feature, StringComparer.Ordinal);
        Assert.True(feature.Count >= 40, $"the trip wire read {feature.Count} file(s) of the feature");
    }
}
