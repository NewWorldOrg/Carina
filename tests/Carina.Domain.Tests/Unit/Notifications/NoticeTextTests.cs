using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Unit.Notifications;

[Trait("Category", "Unit")]
public sealed class NoticeTextTests
{
    [Theory(DisplayName = "BR-ND-002: control characters and bidirectional controls are dropped from what a broadcaster wrote")]
    [InlineData("港町\u0000レシピ帖", "港町レシピ帖")]
    [InlineData("港町\r\nレシピ帖\t", "港町レシピ帖")]
    [InlineData("港町\u202Eレシピ帖\u202C", "港町レシピ帖")]
    [InlineData("\u2066港町\u2069\u200Eレシピ帖\u200F\u061C", "港町レシピ帖")]
    [InlineData("港町\u2028レシピ帖\u2029", "港町レシピ帖")]
    [InlineData("  港町レシピ帖  ", "港町レシピ帖")]
    public void WhatABroadcasterWroteIsCleaned(string written, string cleaned)
    {
        Assert.Equal(cleaned, NoticeText.Clean(written));
    }

    [Fact(DisplayName = "BR-ND-002: a joined emoji survives the cleaning whole")]
    public void AJoinedEmojiSurvivesTheCleaning()
    {
        const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";

        Assert.Equal($"家族{Family}", NoticeText.Clean($"家族{Family}"));
    }

    [Fact(DisplayName = "BR-ND-002: half of a surrogate pair is dropped")]
    public void HalfOfASurrogatePairIsDropped()
    {
        Assert.Equal("港町", NoticeText.Clean("港\uD83D町"));
    }

    [Fact(DisplayName = "nothing written cleans to nothing")]
    public void NothingWrittenCleansToNothing()
    {
        Assert.Equal(string.Empty, NoticeText.Clean(null));
        Assert.Equal(string.Empty, NoticeText.Clean(" \u0007 "));
    }

    [Fact(DisplayName = "BR-ND-002: the line breaks of a body are kept while other control characters are dropped")]
    public void TheLineBreaksOfABodyAreKept()
    {
        Assert.Equal("港町\nレシピ帖", NoticeText.CleanLines("港町\n\u0007レシピ帖\u202E\r"));
    }

    [Fact(DisplayName = "BR-ND-003: words exactly as long as the room are kept as they are")]
    public void WordsExactlyAsLongAsTheRoomAreKept()
    {
        string said = new('港', 12);

        Assert.Equal(said, NoticeText.Cut(said, 12));
    }

    [Fact(DisplayName = "BR-ND-003: words one longer than the room end in an ellipsis inside the room")]
    public void WordsLongerThanTheRoomEndInAnEllipsis()
    {
        string cut = NoticeText.Cut(new string('港', 13), 12);

        Assert.Equal(new string('港', 11) + "…", cut);
    }

    [Fact(DisplayName = "BR-ND-003: a cut never falls inside a grapheme")]
    public void ACutNeverFallsInsideAGrapheme()
    {
        const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
        string said = "港町" + Family + Family;

        string cut = NoticeText.Cut(said, 2 + Family.Length + 3);

        Assert.Equal("港町" + Family + "…", cut);
    }

    [Fact(DisplayName = "a room of nothing cannot be cut to")]
    public void ARoomOfNothingCannotBeCutTo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NoticeText.Cut("港町", 0));
    }
}
