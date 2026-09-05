namespace Carina.Architecture.Tests;

public sealed class LibraryDiskRuleTests
{
    [Fact]
    public void TheListIsBuiltFromTheLedgerAloneSoNothingInTheLibraryAsksTheDiskAnything()
        => Assert.Empty(LibraryDiskRules.ReachesForTheDiskInsideTheLibraryFeature(RepositoryLayout.SourceDirectory));

    [Fact]
    public void ThereIsALibraryFeatureOnDiskForThatRuleToHaveRead()
        => Assert.NotEmpty(LibraryFeature.Files(RepositoryLayout.SourceDirectory));

    [Fact]
    public void TheOnePlaceThatDoesAskTheDiskAboutRecordingsIsStillFoundByTheSameMarks()
        => Assert.NotEmpty(
            LibraryDiskRules.ReachesIn(
                File.ReadAllText(
                    Path.Combine(
                        RepositoryLayout.SourceDirectory,
                        "Carina.Infrastructure",
                        "Integrity",
                        "LocalRecordingFileSurvey.cs"))));
}
