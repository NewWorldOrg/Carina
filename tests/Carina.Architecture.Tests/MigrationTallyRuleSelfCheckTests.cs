namespace Carina.Architecture.Tests;

public sealed class MigrationTallyRuleSelfCheckTests
{
    [Fact]
    public void DetectsAnotherDomainAddingWhatAMigrationLeftBehindIntoItsOwnCount()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-migration-tally-");

        try
        {
            Directory.CreateDirectory(Path.Combine(directory.FullName, "Carina.Domain", "Migration"));
            Directory.CreateDirectory(Path.Combine(directory.FullName, "Carina.Domain", "Quality"));

            File.WriteAllText(
                Path.Combine(directory.FullName, "Carina.Domain", "Migration", "Own.cs"),
                """
                namespace Sample;
                public static class Own
                {
                    public static int Left(MigrationTally tally) => tally.NotCarried;
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Carina.Domain", "Quality", "Mixed.cs"),
                """
                namespace Sample;
                public static class Mixed
                {
                    public static int Wrong(int faults, MigrationTally tally) => faults + tally.NotCarried;
                }
                """);

            Assert.Equal(
                ["Carina.Domain/Quality/Mixed.cs"],
                SourceScan.FilesMentioning(
                        directory.FullName,
                        [.. MigrationTallyReach.WhatARunCounted])
                    .Where(MigrationTallyReach.ReadsOutsideTheRecord)
                    .ToArray());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
