using System.Globalization;
using System.Text.Json.Nodes;

using Carina.Domain.Notifications;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// A notice as Slack takes it: one coloured attachment that holds the title, the body and, when there is one, the link.
/// </summary>
public static class SlackMessage
{
    public const string LinkLabel = "画面で開く";

    private static readonly IReadOnlyDictionary<char, string> InALink = new Dictionary<char, string>
    {
        ['&'] = "&amp;",
        ['<'] = "%3C",
        ['>'] = "%3E",
        ['|'] = "%7C",
    };

    public static JsonObject Of(int colour, MarkedUpNotice words, string? link)
    {
        ArgumentNullException.ThrowIfNull(words);

        JsonArray blocks = [Section($"*{words.Title}*"), Section(words.Body)];

        if (link is not null)
        {
            blocks.Add(Section($"<{Linked(link)}|{LinkLabel}>"));
        }

        return new JsonObject
        {
            ["attachments"] = new JsonArray(new JsonObject
            {
                ["color"] = string.Create(CultureInfo.InvariantCulture, $"#{colour:X6}"),
                ["fallback"] = words.Title,
                ["blocks"] = blocks,
            }),
        };
    }

    private static JsonObject Section(string text)
        => new()
        {
            ["type"] = "section",
            ["text"] = new JsonObject { ["type"] = "mrkdwn", ["text"] = text },
        };

    private static string Linked(string link)
        => string.Concat(link.Select(character => InALink.GetValueOrDefault(character, character.ToString())));
}
