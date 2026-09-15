namespace Carina.Architecture.Tests;

public sealed class FileErasureRuleTests
{
    [Fact]
    public void TheOnlyPlacesThatAskForAFileToBeErasedAreTheTwoErasersThatAskTheDriver()
    {
        Assert.Equal(
            FileErasureRules.TheErasersThatAskTheDriver,
            FileErasureRules.WhatAsksTheDriverToEraseAFile(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void EachPortThatThrowsAFileAwayHasTheDriversEraserAsItsOnlyImplementation()
    {
        Assert.Equal(
            FileErasureRules.TheErasersThatAskTheDriver,
            FileErasureRules.WhatImplementsAnErasurePort(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void NothingOnTheWayAFindingIsThrownAwayChangesTheDiskItself()
    {
        Assert.Empty(FileErasureRules.TheWayAFindingIsThrownAway
            .SelectMany(relative => FileSystemRules
                .WhatCouldChangeWhatIsOnDiskIn(FileErasureRules.Read(RepositoryLayout.SourceDirectory, relative))
                .Select(reach => $"{relative} {reach}"))
            .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheProcessThatRemovesAFileNobodyOwnsIsTheOneThatOwnsTheRoot()
    {
        Assert.Equal(
            [
                "/Carina.Driver/Recording/RecordingEraser.cs File.Delete",
                "/Carina.Driver/Recording/StrayFileEraser.cs File.Delete",
            ],
            FileSystemRules.WhatCouldChangeWhatIsOnDisk(RepositoryLayout.SourceDirectory)
                .Where(entry => entry.StartsWith("/Carina.Driver/Recording/", StringComparison.Ordinal))
                .Where(entry => entry.EndsWith("File.Delete", StringComparison.Ordinal))
                .ToArray());
    }
}
