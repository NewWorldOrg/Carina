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
    public void EachPortThatThrowsAFileAwayIsImplementedByTheDriversErasersAndTheOneForThePlacesTheAppWrites()
    {
        Assert.Equal(
            FileErasureRules.TheErasersThatAskTheDriver
                .Concat(FileErasureRules.TheErasersOfThePlacesTheAppWrites)
                .Order(StringComparer.Ordinal)
                .ToArray(),
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
}
