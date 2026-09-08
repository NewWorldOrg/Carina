namespace Carina.Architecture.Tests;

public sealed class MigrationSourceRuleTests
{
    [Fact]
    public void WhatSaysHowToReachTheSourceIsReadWhereItIsReadAndWhereItIsSpentAndNowhereElse()
    {
        Assert.Equal(
            [
                "Carina.Infrastructure/Migration/MigrationSourceSettings.cs",
                "Carina.Infrastructure/Migration/MySqlMigrationSourceConnection.cs",
            ],
            SourceScan.FilesMentioning(
                RepositoryLayout.SourceDirectory,
                [.. MigrationSourceReach.ConnectionAsSupplied]));
    }

    [Fact]
    public void NothingThatReadsWhatSaysHowToReachTheSourceAlsoWritesToALog()
    {
        Assert.Empty(SourceScan.FilesMentioningBoth(
            RepositoryLayout.SourceDirectory,
            MigrationSourceReach.ConnectionAsSupplied,
            AuthenticationBypasses.Logging));
    }

    [Fact]
    public void TheSettingIsSpeltOutOnlyWhereItIsRead()
    {
        Assert.Equal(
            ["Carina.Infrastructure/Migration/MigrationSourceSettings.cs"],
            SourceScan.FilesMentioning(
                RepositoryLayout.SourceDirectory,
                [.. MigrationSourceReach.TheSetting]));
    }

    [Fact]
    public void NoHostDatabaseAccountOrPasswordForTheSourceIsWrittenDownAnywhereInTheApplication()
    {
        Assert.Empty(SourceScan.FilesMentioning(
            RepositoryLayout.SourceDirectory,
            [.. MigrationSourceReach.AConnectionOfItsOwn]));
    }
}
