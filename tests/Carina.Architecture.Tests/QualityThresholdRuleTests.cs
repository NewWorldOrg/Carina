namespace Carina.Architecture.Tests;

public sealed class QualityThresholdRuleTests
{
    [Fact]
    public void NothingInTheLibraryFeatureCarriesANumberThatCouldDecideHowGoodARecordingIs()
        => Assert.Empty(
            QualityThresholdRules.QualityNumbersInsideTheLibraryFeature(RepositoryLayout.SourceDirectory));

    [Fact]
    public void ThereIsALibraryFeatureOnDiskForThatRuleToHaveRead()
        => Assert.NotEmpty(QualityThresholdRules.FilesOfTheLibraryFeature(RepositoryLayout.SourceDirectory));

    [Fact]
    public void TheOnePlaceThoseNumbersDoLiveIsStillFoundByTheSameMarks()
        => Assert.NotEmpty(
            QualityThresholdRules.NumbersIn(
                File.ReadAllText(
                    Path.Combine(
                        RepositoryLayout.SourceDirectory,
                        QualityThresholdRules.WhereTheNumbersLive.Replace('/', Path.DirectorySeparatorChar)))));
}
