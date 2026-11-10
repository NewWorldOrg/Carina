namespace Carina.Architecture.Tests;

public sealed class WebhookUrlRuleTests
{
    [Fact(DisplayName = "BR-NA-003: the address of a webhook is read as written only where it is sealed and where it is sent to")]
    public void TheAddressOfAWebhookIsReadAsWrittenOnlyWhereItIsSealedAndWhereItIsSentTo()
    {
        Assert.Equal(
            [
                "Carina.Infrastructure/Notifications/NoticeLine.cs",
                "Carina.Infrastructure/Notifications/WebhookUrlSeal.cs",
            ],
            SourceScan.FilesMentioning(
                RepositoryLayout.SourceDirectory,
                [.. AuthenticationBypasses.WebhookAddressAsWritten]));
    }

    [Fact(DisplayName = "BR-NA-003: nothing that reads the address of a webhook as written also writes to a log")]
    public void NothingThatReadsTheAddressOfAWebhookAsWrittenAlsoWritesToALog()
    {
        Assert.Empty(SourceScan.FilesMentioningBoth(
            RepositoryLayout.SourceDirectory,
            AuthenticationBypasses.WebhookAddressAsWritten,
            AuthenticationBypasses.Logging));
    }

    [Fact(DisplayName = "BR-NA-003: nothing that answers a caller can even name the address of a webhook")]
    public void NothingThatAnswersACallerCanEvenNameTheAddressOfAWebhook()
    {
        Assert.Empty(SourceScan.FilesMentioning(
            Path.Combine(RepositoryLayout.SourceDirectory, "Carina.Api", "Responder"),
            "WebhookUrl",
            "AddressAsWritten",
            "SealedUrl"));
    }
}
