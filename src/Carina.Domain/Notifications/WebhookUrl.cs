namespace Carina.Domain.Notifications;

/// <summary>
/// The address of a webhook, which is a secret: whoever knows it can post to the channel. It is read in the clear
/// through <see cref="InTheClear"/> only, to seal it and to send to it.
/// </summary>
public sealed class WebhookUrl
{
    public const int Longest = 2048;

    private const int HttpsPort = 443;

    private const string DiscordPath = "/api/webhooks/";

    private static readonly IReadOnlyDictionary<string, WebhookTarget> ByHost = new Dictionary<string, WebhookTarget>(StringComparer.Ordinal)
    {
        ["hooks.slack.com"] = WebhookTarget.Slack,
        ["discord.com"] = WebhookTarget.Discord,
        ["discordapp.com"] = WebhookTarget.Discord,
    };

    private WebhookUrl(string written, WebhookTarget target)
    {
        InTheClear = written;
        Target = target;
        Tail = written[^NotificationDestination.UrlTailLength..];
    }

    public string InTheClear { get; }

    public WebhookTarget Target { get; }

    public string Tail { get; }

    /// <summary>
    /// The address as written, or nothing where it is not an https address of a host that takes webhooks.
    /// </summary>
    public static WebhookUrl? Read(string? written)
    {
        if (string.IsNullOrEmpty(written)
            || written.Length > Longest
            || !written.All(IsPlain)
            || !Uri.TryCreate(written, UriKind.Absolute, out Uri? read)
            || !IsToAWebhook(read))
        {
            return null;
        }

        bool known = ByHost.TryGetValue(read.IdnHost, out WebhookTarget target);
        bool placed = target is not WebhookTarget.Discord
            || (read.AbsolutePath.StartsWith(DiscordPath, StringComparison.Ordinal) && read.AbsolutePath.Length > DiscordPath.Length);

        return known && placed ? new WebhookUrl(written, target) : null;
    }

    public override string ToString() => "(webhook address)";

    private static bool IsPlain(char letter) => letter is > ' ' and <= '~' and not '\\';

    private static bool IsToAWebhook(Uri read)
        => read.Scheme == Uri.UriSchemeHttps
           && read.Port == HttpsPort
           && read.UserInfo.Length is 0
           && read.Fragment.Length is 0;
}
