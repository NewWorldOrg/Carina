using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

using Carina.Domain.Notifications;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// The body of the request that tells a webhook of a notice, in the shape its kind takes.
/// </summary>
public static class NoticePayload
{
    private static readonly JsonSerializerOptions Written = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly IReadOnlyDictionary<NoticeTone, int> Colours = new Dictionary<NoticeTone, int>
    {
        [NoticeTone.Good] = 0x2E7D32,
        [NoticeTone.Informative] = 0x1565C0,
        [NoticeTone.Caution] = 0xF9A825,
        [NoticeTone.Failure] = 0xC62828,
    };

    public static byte[] For(WebhookTarget target, Notice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return For(target, notice.Tone, notice.Title, notice.Body, notice.Link);
    }

    public static byte[] For(WebhookTarget target, NoticeTone tone, string title, string body, string? link)
    {
        MarkedUpNotice words = NoticeMarkup.For(target, title, body);
        int colour = Colours[NoticeTones.Named(tone)];

        return target switch
        {
            WebhookTarget.Slack => Bytes(SlackMessage.Of(colour, words, link)),
            _ => Bytes(DiscordMessage.Of(colour, words, link)),
        };
    }

    private static byte[] Bytes(JsonObject message) => JsonSerializer.SerializeToUtf8Bytes(message, Written);
}
