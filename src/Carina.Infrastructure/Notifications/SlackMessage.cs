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
        => link
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "%3C", StringComparison.Ordinal)
            .Replace(">", "%3E", StringComparison.Ordinal)
            .Replace("|", "%7C", StringComparison.Ordinal);
}
