namespace Carina.Domain.Notifications;

/// <summary>
/// Escapes the words of a notice so that the destination shows them as written, and cuts them to what it accepts.
/// </summary>
public static class NoticeMarkup
{
    public const int SlackSectionLongest = 3000;

    public const int SlackTitleLongest = SlackSectionLongest - 2;

    public const int DiscordTitleLongest = 256;

    public const int DiscordDescriptionLongest = 4096;

    public const int DiscordEmbedLongest = 6000;

    private static readonly IReadOnlyDictionary<char, string> ForSlack = new Dictionary<char, string>
    {
        ['&'] = "&amp;",
        ['<'] = "&lt;",
        ['>'] = "&gt;",
        ['*'] = "＊",
        ['_'] = "＿",
        ['~'] = "～",
        ['`'] = "｀",
    };

    private static readonly IReadOnlyDictionary<char, string> ForDiscord =
        @"\*_~`|><#[]-".ToDictionary(mark => mark, mark => $@"\{mark}");

    public static MarkedUpNotice For(WebhookTarget target, string title, string body)
        => target switch
        {
            WebhookTarget.Slack => new MarkedUpNotice(Slack(title, SlackTitleLongest), Slack(body, SlackSectionLongest)),
            WebhookTarget.Discord => ForAnEmbed(Discord(title, DiscordTitleLongest), body),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "A notice is shaped for Slack or for Discord."),
        };

    public static string Slack(string said, int longest) => Escaped(said, longest, ForSlack);

    public static string Discord(string said, int longest) => Escaped(said, longest, ForDiscord);

    private static MarkedUpNotice ForAnEmbed(string title, string body)
        => new(title, Discord(body, Math.Min(DiscordDescriptionLongest, DiscordEmbedLongest - title.Length)));

    private static string Escaped(string said, int longest, IReadOnlyDictionary<char, string> escapes)
    {
        ArgumentNullException.ThrowIfNull(said);

        IReadOnlyList<string> pieces =
        [
            .. NoticeText.Graphemes(NoticeText.CleanLines(said)).Select(grapheme => Escaped(grapheme, escapes)),
        ];

        return NoticeText.Fitted(pieces, longest);
    }

    private static string Escaped(string grapheme, IReadOnlyDictionary<char, string> escapes)
        => string.Concat(grapheme.Select(character => escapes.GetValueOrDefault(character, character.ToString())));
}
