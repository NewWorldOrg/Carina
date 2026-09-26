namespace Carina.Architecture.Tests;

public sealed class QualityRuleTests
{
    [Fact]
    public void ThereIsAQualityFeatureOnDiskForTheseRulesToHaveRead()
        => Assert.NotEmpty(QualityRules.FilesInTheFeature(RepositoryLayout.SourceDirectory));

    [Fact]
    public void TheQualityTablesAreLaidOutInTheFilesTheseRulesRead()
        => Assert.Equal(
            QualityRules.WhereTheQualityTablesAreLaidOut,
            QualityRules.FilesLayingOutTheQualityTables(RepositoryLayout.SourceDirectory));

    [Fact(DisplayName = "no quality table declares a foreign key")]
    public void NoQualityTableDeclaresAForeignKey()
        => Assert.Empty(QualityRules.WhatDeclaresAForeignKey(RepositoryLayout.SourceDirectory));

    [Fact(DisplayName = "nothing in the quality feature writes a ledger it does not own")]
    public void NothingInTheQualityFeatureWritesALedgerItDoesNotOwn()
        => Assert.Empty(QualityRules.WhatWritesAnotherDomainsLedger(RepositoryLayout.SourceDirectory));

    [Fact(DisplayName = "nothing in the quality feature offers a way to delete anything")]
    public void NothingInTheQualityFeatureOffersAWayToDeleteAnything()
        => Assert.Empty(QualityRules.WhatOffersAWayToDeleteSomething(RepositoryLayout.SourceDirectory));

    [Fact(DisplayName = "nothing in the quality feature takes a tuner of its own")]
    public void NothingInTheQualityFeatureTakesATunerOfItsOwn()
        => Assert.Empty(QualityRules.WhatTakesATunerOfItsOwn(RepositoryLayout.SourceDirectory));

    [Fact]
    public void TheMarksThatLookForATunerBeingTakenStillFindOneWhereItIs()
        => Assert.NotEmpty(QualityRules.WhatTakesATunerOfItsOwnIn(Source("Carina.Infrastructure/Streaming/DriverLiveSupply.cs")));

    [Fact(DisplayName = "nothing in the quality feature decides an anomaly another domain owns")]
    public void NothingInTheQualityFeatureDecidesAnAnomalyAnotherDomainOwns()
        => Assert.Empty(QualityRules.WhatDecidesAnAnomalyItDoesNotOwn(RepositoryLayout.SourceDirectory));

    [Fact(DisplayName = "nothing in the quality feature writes what it measured to a file")]
    public void NothingInTheQualityFeatureWritesWhatItMeasuredToAFile()
        => Assert.Empty(QualityRules.WhatWritesWhatItMeasuredToAFile(RepositoryLayout.SourceDirectory));

    [Fact]
    public void TheMarksThatLookForAFileBeingWrittenStillFindOneWhereItIs()
        => Assert.NotEmpty(QualityRules.WhatWritesWhatItMeasuredToAFileIn(Source("Carina.Infrastructure/Migration/HardLinkMigrationCarrier.cs")));

    [Fact]
    public void TheMarksThatLookForAForeignKeyStillFindOneWhereTheyAreDeclared()
        => Assert.NotEmpty(QualityRules.WhatDeclaresAForeignKeyIn(Source("Carina.Infrastructure/Persistence/Configurations/EncodeJobConfiguration.cs")));

    [Fact]
    public void TheMarksThatLookForAnAnomalyStillFindTheDomainThatOwnsOne()
        => Assert.NotEmpty(QualityRules.WhatDecidesAnAnomalyItDoesNotOwnIn(Source("Carina.Domain/Recordings/RecordingQuality.cs")));

    private static string Source(string relative)
        => File.ReadAllText(
            Path.Combine(RepositoryLayout.SourceDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
}
