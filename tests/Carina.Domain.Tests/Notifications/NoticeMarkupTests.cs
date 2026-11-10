using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Notifications;

public sealed class NoticeMarkupTests
{
    private const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";

    [Theory(DisplayName = "BR-ND-002: Slack is given the three characters it reads as markup as entities")]
    [InlineData("A & B", "A &amp; B")]
    [InlineData("<!channel>", "&lt;!channel&gt;")]
    [InlineData("<https://elsewhere.example|here>", "&lt;https://elsewhere.example|here&gt;")]
    [InlineData("&amp;", "&amp;amp;")]
    public void SlackIsGivenEntities(string written, string escaped)
    {
        Assert.Equal(escaped, NoticeMarkup.Slack(written, NoticeMarkup.SlackSectionLongest));
    }

    [Theory(DisplayName = "BR-ND-002: the marks Slack reads as emphasis are replaced by their full-width forms, having no escape")]
    [InlineData("*港町*", "＊港町＊")]
    [InlineData("_港町_", "＿港町＿")]
    [InlineData("~港町~", "～港町～")]
    [InlineData("`港町`", "｀港町｀")]
    public void TheMarksSlackReadsAsEmphasisAreReplaced(string written, string escaped)
    {
        Assert.Equal(escaped, NoticeMarkup.Slack(written, NoticeMarkup.SlackSectionLongest));
    }

    [Theory(DisplayName = "BR-ND-002: Discord is given a backslash before each character it reads as markdown")]
    [InlineData("*港町*", @"\*港町\*")]
    [InlineData("_港町_ ~~灯台~~", @"\_港町\_ \~\~灯台\~\~")]
    [InlineData("`港町` ||灯台||", @"\`港町\` \|\|灯台\|\|")]
    [InlineData("[here](https://elsewhere.example)", @"\[here\](https://elsewhere.example)")]
    [InlineData("# 港町", @"\# 港町")]
    [InlineData("> 港町", @"\> 港町")]
    [InlineData("- 港町", @"\- 港町")]
    [InlineData("<@&1>", @"\<@&1\>")]
    [InlineData(@"港町\", @"港町\\")]
    public void DiscordIsGivenBackslashes(string written, string escaped)
    {
        Assert.Equal(escaped, NoticeMarkup.Discord(written, NoticeMarkup.DiscordDescriptionLongest));
    }

    [Fact(DisplayName = "BR-ND-002: control characters and bidirectional controls do not reach either shape, and line breaks do")]
    public void ControlCharactersDoNotReachEitherShape()
    {
        const string Written = "港町\u202Eレシピ帖\u0007\n灯台";

        Assert.Equal("港町レシピ帖\n灯台", NoticeMarkup.Slack(Written, NoticeMarkup.SlackSectionLongest));
        Assert.Equal("港町レシピ帖\n灯台", NoticeMarkup.Discord(Written, NoticeMarkup.DiscordDescriptionLongest));
    }

    [Fact(DisplayName = "BR-ND-003: a Slack section of exactly 3000 characters is kept as it is")]
    public void ASlackSectionExactlyAtTheLimitIsKept()
    {
        string said = new('港', 3000);

        Assert.Equal(said, NoticeMarkup.For(WebhookTarget.Slack, "題", said).Body);
    }

    [Fact(DisplayName = "BR-ND-003: a Slack section of 3001 characters is cut to 3000, ending in an ellipsis")]
    public void ASlackSectionOverTheLimitIsCut()
    {
        string body = NoticeMarkup.For(WebhookTarget.Slack, "題", new string('港', 3001)).Body;

        Assert.Equal(new string('港', 2999) + "…", body);
    }

    [Fact(DisplayName = "BR-ND-003: the limit of a Slack section is counted after escaping, and no entity is cut in two")]
    public void TheLimitOfASlackSectionIsCountedAfterEscaping()
    {
        string exactly = NoticeMarkup.For(WebhookTarget.Slack, "題", new string('&', 600)).Body;
        string over = NoticeMarkup.For(WebhookTarget.Slack, "題", new string('&', 601)).Body;

        Assert.Equal(3000, exactly.Length);
        Assert.Equal(string.Concat(Enumerable.Repeat("&amp;", 599)) + "…", over);
    }

    [Fact(DisplayName = "BR-ND-003: a Discord description of exactly 4096 characters is kept as it is")]
    public void ADiscordDescriptionExactlyAtTheLimitIsKept()
    {
        string said = new('港', 4096);

        Assert.Equal(said, NoticeMarkup.For(WebhookTarget.Discord, "題", said).Body);
    }

    [Fact(DisplayName = "BR-ND-003: a Discord description of 4097 characters is cut to 4096, ending in an ellipsis")]
    public void ADiscordDescriptionOverTheLimitIsCut()
    {
        string body = NoticeMarkup.For(WebhookTarget.Discord, "題", new string('港', 4097)).Body;

        Assert.Equal(new string('港', 4095) + "…", body);
    }

    [Fact(DisplayName = "BR-ND-003: the limit of a Discord description is counted after escaping, and no escape is cut in two")]
    public void TheLimitOfADiscordDescriptionIsCountedAfterEscaping()
    {
        string exactly = NoticeMarkup.For(WebhookTarget.Discord, "題", new string('*', 2048)).Body;
        string over = NoticeMarkup.For(WebhookTarget.Discord, "題", new string('*', 2049)).Body;

        Assert.Equal(4096, exactly.Length);
        Assert.Equal(string.Concat(Enumerable.Repeat(@"\*", 2047)) + "…", over);
    }

    [Fact(DisplayName = "BR-ND-003: a Discord title is cut to 256 characters, and exactly 256 is kept")]
    public void ADiscordTitleIsCut()
    {
        string exactly = new('題', 256);

        Assert.Equal(exactly, NoticeMarkup.For(WebhookTarget.Discord, exactly, "本文").Title);
        Assert.Equal(new string('題', 255) + "…", NoticeMarkup.For(WebhookTarget.Discord, exactly + "題", "本文").Title);
    }

    [Fact(DisplayName = "BR-ND-003: a Discord title and description together stay within 6000 characters")]
    public void ADiscordTitleAndDescriptionStayWithinTheTotal()
    {
        MarkedUpNotice shaped = NoticeMarkup.For(WebhookTarget.Discord, new string('*', 300), new string('*', 9000));

        Assert.True(shaped.Title.Length <= NoticeMarkup.DiscordTitleLongest);
        Assert.True(shaped.Body.Length <= NoticeMarkup.DiscordDescriptionLongest);
        Assert.True(shaped.Title.Length + shaped.Body.Length <= NoticeMarkup.DiscordEmbedLongest);
    }

    [Theory(DisplayName = "BR-ND-003: a cut never falls inside a grapheme, whichever shape it is for")]
    [InlineData(WebhookTarget.Slack, NoticeMarkup.SlackSectionLongest)]
    [InlineData(WebhookTarget.Discord, NoticeMarkup.DiscordDescriptionLongest)]
    public void ACutNeverFallsInsideAGrapheme(WebhookTarget target, int longest)
    {
        string said = new string('港', longest - Family.Length) + Family + Family;

        string body = NoticeMarkup.For(target, "題", said).Body;

        Assert.Equal(new string('港', longest - Family.Length) + "…", body);
    }

    [Fact(DisplayName = "the same words are shaped for either kind of destination")]
    public void TheSameWordsAreShapedForEitherKindOfDestination()
    {
        MarkedUpNotice slack = NoticeMarkup.For(WebhookTarget.Slack, "録画を始めました", "港町 <1> & *2*");
        MarkedUpNotice discord = NoticeMarkup.For(WebhookTarget.Discord, "録画を始めました", "港町 <1> & *2*");

        Assert.Equal(new MarkedUpNotice("録画を始めました", "港町 &lt;1&gt; &amp; ＊2＊"), slack);
        Assert.Equal(new MarkedUpNotice("録画を始めました", @"港町 \<1\> & \*2\*"), discord);
    }

    [Fact(DisplayName = "a kind of destination the ledger does not hold is refused")]
    public void AnUnknownKindOfDestinationIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NoticeMarkup.For((WebhookTarget)99, "題", "本文"));
    }
}
