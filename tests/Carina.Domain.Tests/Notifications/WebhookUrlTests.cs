using System.Reflection;

using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Notifications;

public sealed class WebhookUrlTests
{
    private const string OfSlack = "https://hooks.slack.com/services/T0000/B0000/made-up-token-wxyz";

    private const string OfDiscord = "https://discord.com/api/webhooks/1234/made-up-token-abcd";

    [Theory(DisplayName = "BR-NT-001: an https address of one of the three hosts on port 443 is taken")]
    [InlineData(OfSlack)]
    [InlineData("https://hooks.slack.com:443/services/T0000/B0000/made-up-token-wxyz")]
    [InlineData("https://HOOKS.SLACK.COM/services/T0000/B0000/made-up-token-wxyz")]
    [InlineData(OfDiscord)]
    [InlineData("https://discord.com:443/api/webhooks/1234/made-up-token-abcd")]
    [InlineData("https://discordapp.com/api/webhooks/1234/made-up-token-abcd")]
    [InlineData("https://discord.com/api/webhooks/1234/made-up-token-abcd?thread_id=5678")]
    public void AnAddressOfAKnownHostIsTaken(string written)
    {
        Assert.NotNull(WebhookUrl.Read(written));
    }

    [Theory(DisplayName = "BR-NT-001: any other address is not taken")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hooks.slack.com/services/T0000/B0000/made-up")]
    [InlineData("/services/T0000/B0000/made-up")]
    [InlineData("http://hooks.slack.com/services/T0000/B0000/made-up")]
    [InlineData("ftp://hooks.slack.com/services/T0000/B0000/made-up")]
    [InlineData("https://hooks.slack.com:8443/services/T0000/B0000/made-up")]
    [InlineData("https://hooks.slack.com:80/services/T0000/B0000/made-up")]
    [InlineData("https://someone@hooks.slack.com/services/T0000/B0000/made-up")]
    [InlineData("https://someone:secret@discord.com/api/webhooks/1234/made-up")]
    [InlineData("https://hooks.slack.com.evil.example/services/T0000/B0000/made-up")]
    [InlineData("https://evil.example/hooks.slack.com/services/made-up")]
    [InlineData("https://evil-hooks.slack.com/services/T0000/B0000/made-up")]
    [InlineData("https://hooks.slack.com./services/T0000/B0000/made-up")]
    [InlineData("https://slack.com/services/T0000/B0000/made-up")]
    [InlineData("https://ptb.discord.com/api/webhooks/1234/made-up")]
    [InlineData("https://discord.com.evil.example/api/webhooks/1234/made-up")]
    [InlineData("https://discord.com/api/channels/1234/messages")]
    [InlineData("https://discord.com/API/webhooks/1234/made-up")]
    [InlineData("https://discord.com/api/webhooks/../../users/@me")]
    [InlineData("https://discord.com/api/webhooks/%2e%2e/%2e%2e/users/@me")]
    [InlineData("https://discord.com/api/webhooks")]
    [InlineData("https://[::ffff:10.0.0.3]/services/T0000/B0000/made-up")]
    [InlineData("https://10.0.0.3/services/T0000/B0000/made-up")]
    [InlineData("https://127.0.0.1/api/webhooks/1234/made-up")]
    [InlineData("https://localhost/api/webhooks/1234/made-up")]
    [InlineData("https://hooks.slack.com\\@evil.example/services/made-up")]
    [InlineData("https://hooks.slack.com/services/T0000/B0000/made up")]
    [InlineData("https://hooks.slack.com/services/T0000/B0000/made-up\n")]
    [InlineData("https://hooks.slack.com/services/T0000/B0000/made-up‮gnp")]
    [InlineData("https://hooks.slack.com/services/T0000/B0000/作り物")]
    [InlineData("https://hooks.slack.com/services/T0000/B0000/made-up#part")]
    public void AnyOtherAddressIsNotTaken(string? written)
    {
        Assert.Null(WebhookUrl.Read(written));
    }

    [Fact(DisplayName = "BR-NT-001: an address longer than the ledger keeps is not taken")]
    public void AnAddressTooLongIsNotTaken()
    {
        string head = "https://hooks.slack.com/services/";

        Assert.NotNull(WebhookUrl.Read(head + new string('a', WebhookUrl.Longest - head.Length)));
        Assert.Null(WebhookUrl.Read(head + new string('a', WebhookUrl.Longest - head.Length + 1)));
    }

    [Theory(DisplayName = "BR-NT-002: the kind of a webhook is read from its host")]
    [InlineData(OfSlack, WebhookTarget.Slack)]
    [InlineData(OfDiscord, WebhookTarget.Discord)]
    [InlineData("https://discordapp.com/api/webhooks/1234/made-up-token-abcd", WebhookTarget.Discord)]
    public void TheKindIsReadFromTheHost(string written, WebhookTarget target)
    {
        Assert.Equal(target, WebhookUrl.Read(written)!.Target);
    }

    [Fact(DisplayName = "BR-NA-003: the tail of an address is its last four characters")]
    public void TheTailIsTheLastFourCharacters()
    {
        Assert.Equal("wxyz", WebhookUrl.Read(OfSlack)!.Tail);
        Assert.Equal("abcd", WebhookUrl.Read(OfDiscord)!.Tail);
        Assert.Equal(NotificationDestination.UrlTailLength, WebhookUrl.Read(OfSlack)!.Tail.Length);
    }

    [Fact(DisplayName = "BR-NA-003: an address written out says nothing of itself")]
    public void AnAddressWrittenOutSaysNothingOfItself()
    {
        WebhookUrl webhook = WebhookUrl.Read(OfSlack)!;

        Assert.Equal("(webhook address)", webhook.ToString());
        Assert.Equal("(webhook address)", $"{webhook}");
        Assert.DoesNotContain("made-up", webhook.ToString(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-NA-003: an address is read in the clear through one member only")]
    public void AnAddressIsReadInTheClearThroughOneMemberOnly()
    {
        string[] telling =
        [
            .. typeof(WebhookUrl).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(property => property.PropertyType == typeof(string) || property.PropertyType == typeof(Uri))
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal([nameof(WebhookUrl.InTheClear), nameof(WebhookUrl.Tail)], telling);
        Assert.Equal(OfSlack, WebhookUrl.Read(OfSlack)!.InTheClear);
    }

    [Fact(DisplayName = "BR-NA-003: an address is not compared by what it holds, so it is no key to look one up by")]
    public void TwoAddressesAreNotEqualByWhatTheyHold()
    {
        Assert.False(typeof(WebhookUrl).IsAssignableTo(typeof(IEquatable<WebhookUrl>)));
    }
}
