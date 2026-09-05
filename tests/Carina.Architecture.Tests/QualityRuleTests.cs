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

    [Fact(DisplayName = "BR-QD-013: no quality table declares a foreign key")]
    public void NoQualityTableDeclaresAForeignKey()
        => Assert.Empty(QualityRules.WhatDeclaresAForeignKey(RepositoryLayout.SourceDirectory));

    [Fact(DisplayName = "BR-QA-001: nothing in the quality feature writes a ledger it does not own")]
    public void NothingInTheQualityFeatureWritesALedgerItDoesNotOwn()
        => Assert.Empty(QualityRules.WhatWritesAnotherDomainsLedger(RepositoryLayout.SourceDirectory));

    [Fact(DisplayName = "BR-QA-001: nothing in the quality feature offers a way to delete anything")]
    public void NothingInTheQualityFeatureOffersAWayToDeleteAnything()
        => Assert.Empty(QualityRules.WhatOffersAWayToDeleteSomething(RepositoryLayout.SourceDirectory));

    [Fact(DisplayName = "BR-QD-002: nothing in the quality feature decides an anomaly another domain owns")]
    public void NothingInTheQualityFeatureDecidesAnAnomalyAnotherDomainOwns()
        => Assert.Empty(QualityRules.WhatDecidesAnAnomalyItDoesNotOwn(RepositoryLayout.SourceDirectory));

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
