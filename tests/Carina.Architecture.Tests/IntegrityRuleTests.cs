namespace Carina.Architecture.Tests;

public sealed class IntegrityRuleTests
{
    [Fact]
    public void NothingThatChecksTheLedgerAgainstTheFilesCanDeleteAnything()
    {
        Assert.Empty(IntegrityRules.FilesThatCouldDeleteSomething(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheOnlyFileAllowedToWriteIsTheOneThatKeepsTheReport()
    {
        Assert.Equal(
            ["/Carina.Infrastructure/Integrity/JsonIntegrityReportStore.cs"],
            IntegrityRules.AllowedToWriteAFile);
        Assert.Empty(IntegrityRules.FilesThatCouldWriteSomethingTheyMayNot(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheOneFileThatWritesWritesWhereTheSettingsSayAndNowhereNearAnOutputRoot()
    {
        string store = Path.Combine(
            RepositoryLayout.SourceDirectory,
            "Carina.Infrastructure",
            "Integrity",
            "JsonIntegrityReportStore.cs");

        Assert.True(File.Exists(store));

        string source = File.ReadAllText(store);

        Assert.Contains("settings.ReportPath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OutputRoots", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFeatureIsOnDiskForThoseRulesToHaveRead()
    {
        IReadOnlyList<string> feature = SourceScan.FilesMentioning(
            RepositoryLayout.SourceDirectory,
            "namespace Carina.Domain.Integrity;");

        Assert.Contains("Carina.Domain/Integrity/IntegrityScan.cs", feature, StringComparer.Ordinal);
        Assert.NotEmpty(Directory.EnumerateFiles(
            Path.Combine(RepositoryLayout.SourceDirectory, "Carina.Infrastructure", "Integrity"),
            "*.cs"));
    }

    [Fact]
    public void TheRestOfTheRepositoryStillHasSomethingForThoseRulesToHaveFound()
    {
        Assert.NotEmpty(SourceScan.FilesMentioning(RepositoryLayout.SourceDirectory, "File.WriteAllTextAsync"));
    }
}
