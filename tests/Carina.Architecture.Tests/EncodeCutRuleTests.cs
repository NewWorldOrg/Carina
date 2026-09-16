namespace Carina.Architecture.Tests;

public sealed class EncodeCutRuleTests
{
    private static string Looking => Path.Combine(
        RepositoryLayout.SourceDirectory,
        "Carina.Infrastructure",
        "Encodings",
        "FfmpegChapterInvocation.cs");

    [Fact(DisplayName = "BR-ED2-007: nothing in the encode feature shortens what it writes — the one bound it asks for is the window the look decodes around a moment, which is read and thrown away rather than written, so a break that was marked leaves the artefact the length it would have been had nobody looked")]
    public void TheOnlyBoundTheEncodeFeatureAsksForIsTheWindowTheLookDecodes()
    {
        Assert.Equal(
            ["/Carina.Infrastructure/Encodings/FfmpegChapterInvocation.cs \"-t\""],
            EncodeCutRules.WhatShortensAnOutput(RepositoryLayout.SourceDirectory));

        string peeking = File.ReadAllText(Looking);
        string body = peeking[peeking.IndexOf("Peeking(", StringComparison.Ordinal)..];
        int bounded = body.IndexOf("\"-t\",", StringComparison.Ordinal);
        int read = body.IndexOf("\"-i\",", StringComparison.Ordinal);
        int thrownAway = body.IndexOf("\"null\",", StringComparison.Ordinal);

        Assert.True(bounded >= 0, "the look bounds the window it decodes");
        Assert.True(read > bounded, "the bound is asked for before the input, so it holds what is decoded rather than what is written");
        Assert.True(thrownAway > read, "the look writes its pictures nowhere, so the bound reaches no file");
    }

    [Fact]
    public void TheFeatureIsOnDiskForThatTripWireToRead()
    {
        IReadOnlyList<string> feature = EncodeCutRules.FilesInTheFeature(RepositoryLayout.SourceDirectory);

        Assert.Contains("/Carina.Infrastructure/Encodings/FfmpegEncodeInvocation.cs", feature, StringComparer.Ordinal);
        Assert.Contains("/Carina.Infrastructure/Encodings/FfmpegChapterInvocation.cs", feature, StringComparer.Ordinal);
        Assert.Contains("/Carina.Infrastructure/Encodings/EncodeJobRunner.cs", feature, StringComparer.Ordinal);
        Assert.Contains("/Carina.Infrastructure/Encodings/ChapterMetadataFile.cs", feature, StringComparer.Ordinal);
        Assert.True(feature.Count >= 40, $"the trip wire read {feature.Count} file(s) of the feature");
    }
}
