using System.Text;
using System.Text.Json;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;

namespace Carina.Infrastructure.Tests.Notifications;

public sealed class NoticePayloadTests
{
    private const string Title = "録画が終わりました";

    private const string Body = "作り物の番組\nつくりものテレビ 11/09(月) 21:00〜21:54";

    private const string Link = "https://carina.example/recordings/0123?tab=a&b=c";

    [Fact(DisplayName = "BR-ND-007: Slack is told with one coloured attachment that holds the title, the body and the link")]
    public void SlackIsToldWithOneColouredAttachment()
    {
        string payload = Written(WebhookTarget.Slack, NoticeTone.Good, Title, Body, Link);

        Assert.Equal(
            """{"attachments":[{"color":"#2E7D32","fallback":"録画が終わりました","blocks":["""
            + """{"type":"section","text":{"type":"mrkdwn","text":"*録画が終わりました*"}},"""
            + """{"type":"section","text":{"type":"mrkdwn","text":"作り物の番組\nつくりものテレビ 11/09(月) 21:00〜21:54"}},"""
            + """{"type":"section","text":{"type":"mrkdwn","text":"<https://carina.example/recordings/0123?tab=a&amp;b=c|画面で開く>"}}]}]}""",
            payload);
    }

    [Fact(DisplayName = "BR-ND-007: Slack is told without the link when the address of the screens is not known")]
    public void SlackIsToldWithoutTheLinkWhenThereIsNone()
    {
        string payload = Written(WebhookTarget.Slack, NoticeTone.Failure, Title, Body, null);

        Assert.Equal(
            """{"attachments":[{"color":"#C62828","fallback":"録画が終わりました","blocks":["""
            + """{"type":"section","text":{"type":"mrkdwn","text":"*録画が終わりました*"}},"""
            + """{"type":"section","text":{"type":"mrkdwn","text":"作り物の番組\nつくりものテレビ 11/09(月) 21:00〜21:54"}}]}]}""",
            payload);
    }

    [Fact(DisplayName = "BR-ND-007: Discord is told with one coloured embed, and the list of whom to mention is empty")]
    public void DiscordIsToldWithOneColouredEmbed()
    {
        string payload = Written(WebhookTarget.Discord, NoticeTone.Good, Title, Body, Link);

        Assert.Equal(
            """{"embeds":[{"title":"録画が終わりました","description":"作り物の番組\nつくりものテレビ 11/09(月) 21:00〜21:54","color":"""
            + """3046706,"url":"https://carina.example/recordings/0123?tab=a&b=c"}],"allowed_mentions":{"parse":[]}}""",
            payload);
    }

    [Fact(DisplayName = "BR-ND-007: Discord is told without the address when the address of the screens is not known")]
    public void DiscordIsToldWithoutTheAddressWhenThereIsNone()
    {
        string payload = Written(WebhookTarget.Discord, NoticeTone.Caution, Title, Body, null);

        Assert.Equal(
            """{"embeds":[{"title":"録画が終わりました","description":"作り物の番組\nつくりものテレビ 11/09(月) 21:00〜21:54","color":"""
            + """16361509}],"allowed_mentions":{"parse":[]}}""",
            payload);
    }

    [Theory(DisplayName = "BR-ND-002: whatever the words say, Discord is told to mention nobody and is given no content")]
    [InlineData("@everyone", "@here")]
    [InlineData("<@&1234>", "<@1234> <#5678>")]
    [InlineData("作り物の番組", "ふつうの文")]
    public void DiscordIsToldToMentionNobody(string title, string body)
    {
        using JsonDocument payload = JsonDocument.Parse(Written(WebhookTarget.Discord, NoticeTone.Informative, title, body, Link));

        JsonElement mentions = payload.RootElement.GetProperty("allowed_mentions");
        Assert.Equal(["parse"], mentions.EnumerateObject().Select(property => property.Name));
        Assert.Equal(0, mentions.GetProperty("parse").GetArrayLength());
        Assert.False(payload.RootElement.TryGetProperty("content", out _));
        Assert.Equal(1, payload.RootElement.GetProperty("embeds").GetArrayLength());
    }

    [Theory(DisplayName = "BR-ND-002: the marks of the writer reach Slack escaped, and a link cannot be closed early")]
    [InlineData("<!channel> & <@U1>", "&lt;!channel&gt; &amp; &lt;@U1&gt;")]
    [InlineData("*太字* _斜体_ ~消し~ `code`", "＊太字＊ ＿斜体＿ ～消し～ ｀code｀")]
    public void TheMarksOfTheWriterReachSlackEscaped(string written, string shown)
    {
        using JsonDocument payload = JsonDocument.Parse(Written(WebhookTarget.Slack, NoticeTone.Good, written, written, "https://carina.example/a|b<c>d"));

        JsonElement attachment = payload.RootElement.GetProperty("attachments")[0];
        JsonElement blocks = attachment.GetProperty("blocks");
        Assert.Equal(shown, attachment.GetProperty("fallback").GetString());
        Assert.Equal($"*{shown}*", blocks[0].GetProperty("text").GetProperty("text").GetString());
        Assert.Equal(shown, blocks[1].GetProperty("text").GetProperty("text").GetString());
        Assert.Equal("<https://carina.example/a%7Cb%3Cc%3Ed|画面で開く>", blocks[2].GetProperty("text").GetProperty("text").GetString());
    }

    [Theory(DisplayName = "BR-ND-007: each tone has its own colour, the same one at Slack and at Discord")]
    [InlineData(NoticeTone.Good, "#2E7D32", 0x2E7D32)]
    [InlineData(NoticeTone.Informative, "#1565C0", 0x1565C0)]
    [InlineData(NoticeTone.Caution, "#F9A825", 0xF9A825)]
    [InlineData(NoticeTone.Failure, "#C62828", 0xC62828)]
    public void EachToneHasItsOwnColour(NoticeTone tone, string atSlack, int atDiscord)
    {
        using JsonDocument slack = JsonDocument.Parse(Written(WebhookTarget.Slack, tone, Title, Body, null));
        using JsonDocument discord = JsonDocument.Parse(Written(WebhookTarget.Discord, tone, Title, Body, null));

        Assert.Equal(atSlack, slack.RootElement.GetProperty("attachments")[0].GetProperty("color").GetString());
        Assert.Equal(atDiscord, discord.RootElement.GetProperty("embeds")[0].GetProperty("color").GetInt32());
    }

    [Fact(DisplayName = "BR-ND-003: the longest notice still fits what Slack and Discord accept")]
    public void TheLongestNoticeStillFits()
    {
        string title = new('&', Notice.TitleLongest);
        string body = new('*', Notice.BodyLongest);

        using JsonDocument slack = JsonDocument.Parse(Written(WebhookTarget.Slack, NoticeTone.Good, title, body, null));
        using JsonDocument discord = JsonDocument.Parse(Written(WebhookTarget.Discord, NoticeTone.Good, title, body, null));

        JsonElement blocks = slack.RootElement.GetProperty("attachments")[0].GetProperty("blocks");
        JsonElement embed = discord.RootElement.GetProperty("embeds")[0];
        Assert.All(blocks.EnumerateArray(), block => Assert.InRange(block.GetProperty("text").GetProperty("text").GetString()!.Length, 1, 3000));
        Assert.InRange(embed.GetProperty("title").GetString()!.Length, 1, 256);
        Assert.InRange(embed.GetProperty("description").GetString()!.Length, 1, 4096);
    }

    [Fact(DisplayName = "BR-ND-007: a notice is shaped from what was worded once, for the kind of the destination")]
    public void ANoticeIsShapedFromWhatWasWordedOnce()
    {
        Notice notice = Notice.Rehydrate(
            NoticeId.New(),
            NoticeKind.RecordingCompleted,
            Guid.NewGuid(),
            new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc),
            NoticeTone.Good,
            Title,
            Body,
            Link,
            new DateTime(2026, 11, 9, 12, 0, 5, DateTimeKind.Utc));

        Assert.Equal(Written(WebhookTarget.Slack, NoticeTone.Good, Title, Body, Link), Encoding.UTF8.GetString(NoticePayload.For(WebhookTarget.Slack, notice)));
        Assert.Equal(Written(WebhookTarget.Discord, NoticeTone.Good, Title, Body, Link), Encoding.UTF8.GetString(NoticePayload.For(WebhookTarget.Discord, notice)));
    }

    private static string Written(WebhookTarget target, NoticeTone tone, string title, string body, string? link)
        => Encoding.UTF8.GetString(NoticePayload.For(target, tone, title, body, link));
}
