using System.Text;
using System.Text.Json;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;

namespace Carina.Infrastructure.Tests.Notifications;

public sealed class PushPayloadTests
{
    private static readonly DateTime At = new(2031, 4, 2, 3, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "D-11: a browser is told the title, the body, the path to open and the id of the notice, and nothing else")]
    public void ABrowserIsToldTheTitleTheBodyThePathAndTheId()
    {
        Notice notice = Told("録画が終わりました", "作り物の番組\n作り物の局", "https://screens.example/recordings/0f8fad5b-d9cb-469f-a165-70867728950e");

        using JsonDocument read = JsonDocument.Parse(PushPayload.For(notice));

        Assert.Equal(["title", "body", "path", "id"], read.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("録画が終わりました", read.RootElement.GetProperty("title").GetString());
        Assert.Equal("作り物の番組\n作り物の局", read.RootElement.GetProperty("body").GetString());
        Assert.Equal("/recordings/0f8fad5b-d9cb-469f-a165-70867728950e", read.RootElement.GetProperty("path").GetString());
        Assert.Equal(notice.Id.Value.ToString("D"), read.RootElement.GetProperty("id").GetString());
    }

    [Theory(DisplayName = "D-11: the path is the link of the notice without its origin, query and fragment kept")]
    [InlineData("https://screens.example/settings/notifications", "/settings/notifications")]
    [InlineData("https://screens.example/guide?at=2031-04-02#now", "/guide?at=2031-04-02#now")]
    [InlineData("http://screens.example:8080/recordings", "/recordings")]
    [InlineData("https://screens.example/", "/")]
    public void ThePathIsTheLinkWithoutItsOrigin(string link, string path)
    {
        using JsonDocument read = JsonDocument.Parse(PushPayload.For(Told("A title", "A body", link)));

        Assert.Equal(path, read.RootElement.GetProperty("path").GetString());
        Assert.DoesNotContain("screens.example", Encoding.UTF8.GetString(PushPayload.For(Told("A title", "A body", link))), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "D-11: a notice without a link opens the first screen")]
    public void ANoticeWithoutALinkOpensTheFirstScreen()
    {
        using JsonDocument read = JsonDocument.Parse(PushPayload.For(Told("A title", "A body", link: null)));

        Assert.Equal("/", read.RootElement.GetProperty("path").GetString());
    }

    [Fact(DisplayName = "D-11: the words are written as they are, without the escapes a chat takes")]
    public void TheWordsAreWrittenWithoutTheEscapesAChatTakes()
    {
        const string Title = "<録画> *A & B* _c_ ~d~ `e` @here";

        byte[] written = PushPayload.For(Told(Title, "「これ」と \"それ\"", link: null));

        using JsonDocument read = JsonDocument.Parse(written);
        string asText = Encoding.UTF8.GetString(written);
        Assert.Equal(Title, read.RootElement.GetProperty("title").GetString());
        Assert.Equal("「これ」と \"それ\"", read.RootElement.GetProperty("body").GetString());
        Assert.Contains("<録画> *A & B*", asText, StringComparison.Ordinal);
        Assert.DoesNotContain("&amp;", asText, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", asText, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "D-11: a message is at most 3000 bytes, cut at whole characters, with the path and the id left whole")]
    [InlineData("あ", "い")]
    [InlineData("a", "b")]
    [InlineData("👨‍👩‍👧‍👦", "🇯🇵")]
    [InlineData("\"", "\\")]
    public void AMessageIsAtMost3000Bytes(string titlePiece, string bodyPiece)
    {
        string title = string.Concat(Enumerable.Repeat(titlePiece, 4000));
        string body = string.Concat(Enumerable.Repeat(bodyPiece, 4000));
        Guid id = Guid.NewGuid();

        byte[] written = PushPayload.For(title, body, "/recordings/0f8fad5b-d9cb-469f-a165-70867728950e", id);

        using JsonDocument read = JsonDocument.Parse(written);
        string cutTitle = read.RootElement.GetProperty("title").GetString()!;
        string cutBody = read.RootElement.GetProperty("body").GetString()!;
        Assert.Equal(3000, PushPayload.Longest);
        Assert.InRange(written.Length, 2000, PushPayload.Longest);
        Assert.EndsWith(titlePiece + "…", cutTitle, StringComparison.Ordinal);
        Assert.EndsWith(bodyPiece + "…", cutBody, StringComparison.Ordinal);
        Assert.Equal(string.Concat(Enumerable.Repeat(titlePiece, (cutTitle.Length - 1) / titlePiece.Length)) + "…", cutTitle);
        Assert.Equal(string.Concat(Enumerable.Repeat(bodyPiece, (cutBody.Length - 1) / bodyPiece.Length)) + "…", cutBody);
        Assert.Equal("/recordings/0f8fad5b-d9cb-469f-a165-70867728950e", read.RootElement.GetProperty("path").GetString());
        Assert.Equal(id.ToString("D"), read.RootElement.GetProperty("id").GetString());
    }

    [Fact(DisplayName = "D-11: a notice as long as a notice can be keeps its title whole, and its body is cut to fit")]
    public void ANoticeAsLongAsANoticeCanBeIsCutToFit()
    {
        string title = new('あ', Notice.TitleLongest);
        Notice notice = Told(title, new string('い', Notice.BodyLongest), "https://screens.example/recordings/0f8fad5b-d9cb-469f-a165-70867728950e");

        byte[] written = PushPayload.For(notice);

        using JsonDocument read = JsonDocument.Parse(written);
        string cutBody = read.RootElement.GetProperty("body").GetString()!;
        Assert.InRange(written.Length, 2900, PushPayload.Longest);
        Assert.Equal(title, read.RootElement.GetProperty("title").GetString());
        Assert.Equal(new string('い', cutBody.Length - 1) + "…", cutBody);
        Assert.Equal(notice.Id.Value.ToString("D"), read.RootElement.GetProperty("id").GetString());
    }

    [Fact(DisplayName = "D-11: a message that fits is not cut")]
    public void AMessageThatFitsIsNotCut()
    {
        string body = string.Concat(Enumerable.Repeat("あ", 800));

        using JsonDocument read = JsonDocument.Parse(PushPayload.For(Told("A title", body, link: null)));

        Assert.Equal(body, read.RootElement.GetProperty("body").GetString());
    }

    [Fact(DisplayName = "D-11: a trial names a path and no notice")]
    public void ATrialNamesAPathAndNoNotice()
    {
        using JsonDocument read = JsonDocument.Parse(PushPayload.For("テスト送信", "見本です。", "/settings/notifications", id: null));

        Assert.Equal("テスト送信", read.RootElement.GetProperty("title").GetString());
        Assert.Equal("/settings/notifications", read.RootElement.GetProperty("path").GetString());
        Assert.Equal(JsonValueKind.Null, read.RootElement.GetProperty("id").ValueKind);
    }

    [Fact(DisplayName = "D-11: a path that leaves no room for a message of 3000 bytes is refused rather than cut")]
    public void APathThatLeavesNoRoomIsRefused()
    {
        string path = "/" + new string('a', PushPayload.Longest);

        Assert.Throws<ArgumentException>(() => PushPayload.For("A title", "A body", path, Guid.NewGuid()));
    }

    private static Notice Told(string title, string body, string? link)
        => Notice.Take(NoticeId.New(), NoticeKind.RecordingCompleted, Guid.NewGuid(), At, new NoticeWords(title, body, link), At);
}
