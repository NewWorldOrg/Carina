using System.Text.Json.Nodes;

using Carina.Domain.Notifications;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// A notice as Discord takes it: one coloured embed, with nobody mentioned whatever the words say.
/// </summary>
public static class DiscordMessage
{
    public static JsonObject Of(int colour, MarkedUpNotice words, string? link)
    {
        ArgumentNullException.ThrowIfNull(words);

        JsonObject embed = new()
        {
            ["title"] = words.Title,
            ["description"] = words.Body,
            ["color"] = colour,
        };

        if (link is not null)
        {
            embed["url"] = link;
        }

        return new JsonObject
        {
            ["embeds"] = new JsonArray(embed),
            ["allowed_mentions"] = new JsonObject { ["parse"] = new JsonArray() },
        };
    }
}
