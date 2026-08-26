namespace Carina.Architecture.Tests;

public sealed class RecordingLeadRuleTests
{
    [Fact]
    public void TheHeadTheRecorderAllowsIsTheTimeTheDriverSpendsGettingToTheFirstByte()
    {
        Assert.Empty(RecordingLeadRules.WhereTheHeadDisagreesWithTheDriver(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheThreeFilesTheRuleReadsAreOnDiskForItToRead()
    {
        foreach (string relative in
            new[]
            {
                RecordingLeadRules.DriverSettings,
                RecordingLeadRules.DriverSessions,
                RecordingLeadRules.RecorderSettings,
            })
        {
            Assert.True(
                File.Exists(Path.Combine(RepositoryLayout.SourceDirectory, relative)),
                $"{relative} has moved, so the rule above reads nothing and passes having compared nothing.");
        }
    }
}
