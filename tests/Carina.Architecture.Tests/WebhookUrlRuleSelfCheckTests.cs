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

    [Fact(DisplayName = "BR-NA-003: the rule sees a source that would hand what a browser subscribed with to a log")]
    public void DetectsASourceThatWouldHandWhatABrowserSubscribedWithToALog()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-push-subscription-");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Quiet.cs"),
                """
                namespace Sample;
                public static class Send
                {
                    public static Uri To(PushSubscription subscription) => new(subscription.EndpointAsWritten);
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "LoudAboutTheEndpoint.cs"),
                """
                namespace Sample;
                public sealed class Send(ILogger<Send> logger)
                {
                    public void To(PushSubscription subscription) => logger.LogInformation("{Endpoint}", subscription.EndpointAsWritten);
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "LoudAboutTheKey.cs"),
                """
                namespace Sample;
                public sealed class Send(ILogger<Send> logger)
                {
                    public void To(PushSubscription subscription) => logger.LogWarning("{Key}", subscription.KeyAsWritten);
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "LoudAboutTheSecret.cs"),
                """
                namespace Sample;
                public sealed class Send(ILogger<Send> logger)
                {
                    public void To(PushSubscription subscription) => logger.LogError("{Secret}", subscription.SecretAsWritten);
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Elsewhere.cs"),
                """
                namespace Sample;
                public sealed class Note(ILogger<Note> logger)
                {
                    public void Tail(PushSubscription subscription) => logger.LogInformation("{Tail}", subscription.Tail);
                }
                """);

            Assert.Equal(
                ["LoudAboutTheEndpoint.cs", "LoudAboutTheKey.cs", "LoudAboutTheSecret.cs"],
                SourceScan.FilesMentioningBoth(
                    directory.FullName,
                    AuthenticationBypasses.PushSubscriptionAsWritten,
                    AuthenticationBypasses.Logging));
            Assert.Equal(
                ["LoudAboutTheEndpoint.cs", "LoudAboutTheKey.cs", "LoudAboutTheSecret.cs", "Quiet.cs"],
                SourceScan.FilesMentioning(
                    directory.FullName,
                    [.. AuthenticationBypasses.PushSubscriptionAsWritten]));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact(DisplayName = "BR-NA-003: the rule sees an answer that would carry what a browser subscribed with")]
    public void DetectsAnAnswerThatWouldCarryWhatABrowserSubscribedWith()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-push-answer-");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Telling.cs"),
                """
                namespace Sample;
                public sealed record DestinationResponder(string Name, PushSubscription Subscription);
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Silent.cs"),
                """
                namespace Sample;
                public sealed record DestinationResponder(string Name, string Tail, string? Fingerprint);
                """);

            Assert.Equal(
                ["Telling.cs"],
                SourceScan.FilesMentioning(
                    directory.FullName,
                    "PushSubscription",
                    "EndpointAsWritten",
                    "KeyAsWritten",
                    "SecretAsWritten"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
