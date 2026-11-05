namespace Carina.Architecture.Tests;

public sealed class RecordingLeadRuleTests
{
    [Fact]
    public void TheHeadTheRecorderAllowsIsTheTimeTheDriverSpendsGettingToTheFirstByte()
    {
        Assert.Empty(RecordingLeadRules.WhereTheHeadDisagreesWithTheDriver(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheFileTheRuleReadsIsOnDiskForItToRead()
    {
        Assert.True(
            File.Exists(Path.Combine(RepositoryLayout.SourceDirectory, RecordingLeadRules.RecorderSettings)),
            $"{RecordingLeadRules.RecorderSettings} has moved, so the rule above reads nothing and passes having compared nothing.");
    }
}
