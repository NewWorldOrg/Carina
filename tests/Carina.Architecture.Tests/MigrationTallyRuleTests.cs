namespace Carina.Architecture.Tests;

public sealed class MigrationTallyRuleTests
{
    [Fact]
    public void WhatAMigrationCouldNotCarryIsCountedOnlyOnItsOwnRecord()
        => Assert.Equal(
            [],
            SourceScan.FilesMentioning(
                    RepositoryLayout.SourceDirectory,
                    [.. MigrationTallyReach.WhatARunCounted])
                .Where(MigrationTallyReach.ReadsOutsideTheRecord)
                .ToArray());

    [Fact]
    public void WhatARunCountedIsSpeltSomewhereInTheSourceForTheRuleAboveToReach()
        => Assert.NotEmpty(SourceScan.FilesMentioning(
            RepositoryLayout.SourceDirectory,
            [.. MigrationTallyReach.WhatARunCounted]));
}
