namespace Carina.Architecture.Tests;

public sealed class LibraryQualityRuleTests
{
    [Fact(DisplayName = "nothing that builds or filters a library row decides a quality standing of its own")]
    public void NothingThatBuildsOrFiltersALibraryRowDecidesAQualityStandingOfItsOwn()
        => Assert.Empty(LibraryQualityRules.WhatDecidesAStandingWhereARowIsBuilt(RepositoryLayout.SourceDirectory));

    [Fact(DisplayName = "the one place that asks quality for a verdict is still found by the same marks")]
    public void TheOnePlaceThatAsksQualityForAVerdictIsStillFoundByTheSameMarks()
        => Assert.NotEmpty(
            LibraryQualityRules.WhatDecidesAStandingIn(
                LibraryQualityRules.WhatTheOneAskingPlaceReads(RepositoryLayout.SourceDirectory)));

    [Fact(DisplayName = "no share a recording could be judged against is written where a library row is built")]
    public void NoShareARecordingCouldBeJudgedAgainstIsWrittenWhereALibraryRowIsBuilt()
        => Assert.Empty(LibraryQualityRules.SharesWhereARowIsBuilt(RepositoryLayout.SourceDirectory));

    [Fact(DisplayName = "the library reads the four levels and none of quality's other standings")]
    public void TheLibraryReadsTheFourLevelsAndNoneOfQualitysOtherStandings()
        => Assert.Equal(
            LibraryQualityRules.TheFourLevelsTheLibraryReads,
            LibraryQualityRules.TheLevelsTheLibraryReads(RepositoryLayout.SourceDirectory));

    [Fact(DisplayName = "no standing is stored on the recording table, so moving a level needs no migration")]
    public void NoStandingIsStoredOnTheRecordingTableSoMovingALevelNeedsNoMigration()
        => Assert.Empty(LibraryQualityRules.WhatStoresAStandingOnTheRecordingTable(RepositoryLayout.SourceDirectory));

    [Fact]
    public void TheRecordingTableStillDeclaresComputedColumnsForThatRuleToHaveLookedPast()
        => Assert.NotEmpty(LibraryQualityRules.ComputedColumnsOnTheRecordingTable(RepositoryLayout.SourceDirectory));

    [Fact]
    public void EveryFileTheseRulesReadIsStillWhereTheyLookForIt()
        => Assert.Empty(LibraryQualityRules.FilesMissingFromTheRowPath(RepositoryLayout.SourceDirectory));
}
