namespace Carina.Architecture.Tests;

public sealed class EncodeScratchRuleTests
{
    [Fact(DisplayName = "BR-ED2-010: nothing in the encode feature walks a directory, so nothing there can find a file the ledger did not name")]
    public void NothingInTheEncodeFeatureWalksADirectory()
    {
        Assert.Empty(EncodeScratchRules.WhatWalksADirectory(RepositoryLayout.SourceDirectory));
    }

    [Fact(DisplayName = "BR-ED2-010: the one place the encode feature deletes a file is the sweep that reads the ledger, and the probe takes back only its own empty directory")]
    public void TheOnePlaceTheEncodeFeatureDeletesAFileIsTheSweepThatReadsTheLedger()
    {
        Assert.Equal(
            [
                "/Carina.Infrastructure/Encodings/EncodeScratchCleaner.cs File.Delete",
                "/Carina.Infrastructure/Encodings/RenameProbe.cs Directory.Delete",
            ],
            EncodeScratchRules.WhatDeletes(RepositoryLayout.SourceDirectory));

        string sweep = File.ReadAllText(Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Infrastructure",
            "Encodings",
            "EncodeScratchCleaner.cs"));

        Assert.Contains("ListOwedAsync(", sweep, StringComparison.Ordinal);
        Assert.Contains("File.Delete(path)", sweep, StringComparison.Ordinal);
        Assert.Equal(1, sweep.Split("File.Delete(").Length - 1);
    }

    [Fact]
    public void TheFeatureIsOnDiskForThoseTripWiresToRead()
    {
        IReadOnlyList<string> feature = EncodeScratchRules.FilesInTheFeature(RepositoryLayout.SourceDirectory);

        Assert.Contains("/Carina.Domain/Encodings/EncodeScratchFile.cs", feature, StringComparer.Ordinal);
        Assert.Contains("/Carina.Infrastructure/Encodings/EncodeScratchCleaner.cs", feature, StringComparer.Ordinal);
        Assert.Contains("/Carina.Infrastructure/Encodings/EncodeArtefactPlacer.cs", feature, StringComparer.Ordinal);
        Assert.True(feature.Count >= 30, $"the trip wires read {feature.Count} file(s) of the feature");
    }
}
