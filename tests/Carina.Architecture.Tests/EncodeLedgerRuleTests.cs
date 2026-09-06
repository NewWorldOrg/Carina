namespace Carina.Architecture.Tests;

public sealed class EncodeLedgerRuleTests
{
    [Fact]
    public void NothingInTheLibraryFeatureReadsTheEncodeLedgerForItself()
        => Assert.Empty(
            EncodeLedgerRules.TheEncodeLedgerReachedIntoFromTheLibraryFeature(RepositoryLayout.SourceDirectory));

    [Fact]
    public void ThereIsALibraryFeatureOnDiskForThatRuleToHaveRead()
        => Assert.NotEmpty(LibraryFeature.Files(RepositoryLayout.SourceDirectory));

    [Fact]
    public void TheOnePlaceTheLedgerIsReadFromIsStillFoundByTheSameMarks()
        => Assert.NotEmpty(
            EncodeLedgerRules.ReachesIn(
                File.ReadAllText(
                    Path.Combine(
                        RepositoryLayout.SourceDirectory,
                        EncodeLedgerRules.WhereTheLedgerLives.Replace('/', Path.DirectorySeparatorChar)))));
}
