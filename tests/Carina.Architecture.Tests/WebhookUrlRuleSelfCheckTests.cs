namespace Carina.Architecture.Tests;

public sealed class WebhookUrlRuleSelfCheckTests
{
    [Fact(DisplayName = "BR-NA-003: the rule sees a source that would hand the address of a webhook to a log")]
    public void DetectsASourceThatWouldHandTheAddressOfAWebhookToALog()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-webhook-address-");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Quiet.cs"),
                """
                namespace Sample;
                public static class Send
                {
                    public static Uri To(WebhookUrl webhook) => new(webhook.AddressAsWritten);
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Loud.cs"),
                """
                namespace Sample;
                public sealed class Send(ILogger<Send> logger)
                {
                    public void To(WebhookUrl webhook) => logger.LogInformation("{Address}", webhook.AddressAsWritten);
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Elsewhere.cs"),
                """
                namespace Sample;
                public sealed class Note(ILogger<Note> logger)
                {
                    public void Tail(WebhookUrl webhook) => logger.LogInformation("{Tail}", webhook.Tail);
                }
                """);

            Assert.Equal(
                ["Loud.cs"],
                SourceScan.FilesMentioningBoth(
                    directory.FullName,
                    AuthenticationBypasses.WebhookAddressAsWritten,
                    AuthenticationBypasses.Logging));
            Assert.Equal(
                ["Loud.cs", "Quiet.cs"],
                SourceScan.FilesMentioning(
                    directory.FullName,
                    [.. AuthenticationBypasses.WebhookAddressAsWritten]));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact(DisplayName = "BR-NA-003: the rule sees an answer that would carry the address of a webhook")]
    public void DetectsAnAnswerThatWouldCarryTheAddressOfAWebhook()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-webhook-answer-");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Telling.cs"),
                """
                namespace Sample;
                public sealed record DestinationResponder(string Name, WebhookUrl Address);
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Silent.cs"),
                """
                namespace Sample;
                public sealed record DestinationResponder(string Name, string Tail);
                """);

            Assert.Equal(
                ["Telling.cs"],
                SourceScan.FilesMentioning(directory.FullName, "WebhookUrl", "AddressAsWritten", "SealedUrl"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
