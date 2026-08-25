namespace Carina.Architecture.Tests;

public sealed class IntegrityRuleTests
{
    [Fact]
    public void NothingThatChecksTheLedgerAgainstTheFilesCanDeleteAnything()
    {
        Assert.Empty(IntegrityRules.FilesThatCouldDeleteSomething(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void NothingThatChecksTheLedgerAgainstTheFilesCanWriteAFileEither()
    {
        Assert.Empty(IntegrityRules.AllowedToWriteAFile);
        Assert.Empty(IntegrityRules.FilesThatCouldWriteSomethingTheyMayNot(RepositoryLayout.SourceDirectory));
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
        Assert.NotEmpty(SourceScan.FilesMentioning(RepositoryLayout.SourceDirectory, "new FileStream"));
        Assert.NotEmpty(SourceScan.FilesMentioning(RepositoryLayout.SourceDirectory, ".Delete("));
    }
}
