namespace Carina.Architecture.Tests;

public sealed class RecordingRetryRuleTests
{
    [Fact(DisplayName = "trying a start again never settles, begins, claims or opens a recording of its own")]
    public void TryingAStartAgainNeverSettlesBeginsClaimsOrOpensARecordingOfItsOwn()
        => Assert.Empty(RecordingRetryRules.RetryFilesThatReachARecordingOfTheirOwn(RepositoryLayout.SourceDirectory));

    [Fact(DisplayName = "the files the retry is made of are the ones the rule above reads")]
    public void TheFilesTheRetryIsMadeOfAreTheOnesTheRuleAboveReads()
        => Assert.Equal(
            [
                "/Carina.Domain/Recordings/RetryAttempt.cs",
                "/Carina.Domain/Recordings/RetryGiveUp.cs",
                "/Carina.Domain/Recordings/RetryPolicy.cs",
                "/Carina.Domain/Recordings/StartRetry.cs",
                "/Carina.Infrastructure/Configuration/RecordingRetryOptions.cs",
                "/Carina.Infrastructure/Recordings/RecordingRetries.cs",
            ],
            RecordingRetryRules.FilesOfTheRetry(RepositoryLayout.SourceDirectory));
}
