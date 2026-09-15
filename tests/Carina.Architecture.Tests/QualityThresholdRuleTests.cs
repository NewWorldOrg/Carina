namespace Carina.Architecture.Tests;

public sealed class QualityThresholdRuleTests
{
    [Fact]
    public void NothingInTheLibraryFeatureCarriesANumberThatCouldDecideHowGoodARecordingIs()
        => Assert.Empty(
            QualityThresholdRules.QualityNumbersInsideTheLibraryFeature(RepositoryLayout.SourceDirectory));

    [Fact]
    public void ThereIsALibraryFeatureOnDiskForThatRuleToHaveRead()
        => Assert.NotEmpty(LibraryFeature.Files(RepositoryLayout.SourceDirectory));

    [Fact]
    public void TheOnePlaceThoseNumbersDoLiveIsStillFoundByTheSameMarks()
        => Assert.NotEmpty(QualityThresholdRules.NumbersIn(TheThresholdTable()));

    [Fact(DisplayName = "BR-QD-003: a share scrambling or loss is judged against is written down in the threshold table and nowhere else")]
    public void AShareScramblingOrLossIsJudgedAgainstIsWrittenDownInTheThresholdTableAndNowhereElse()
        => Assert.Empty(
            QualityThresholdRules.SharesOfWhatARecordingIsJudgedOnOutsideTheirTable(RepositoryLayout.SourceDirectory));

    [Fact]
    public void TheThresholdTableIsStillFoundByTheMarksThatRuleLooksFor()
        => Assert.NotEmpty(QualityThresholdRules.SharesOfWhatARecordingIsJudgedOnIn(TheThresholdTable()));

    private static string TheThresholdTable()
        => File.ReadAllText(
            Path.Combine(
                RepositoryLayout.SourceDirectory,
                QualityThresholdRules.WhereTheNumbersLive.Replace('/', Path.DirectorySeparatorChar)));
}
