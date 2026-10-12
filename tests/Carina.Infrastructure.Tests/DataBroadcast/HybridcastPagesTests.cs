using System.Text;

using Carina.Infrastructure.DataBroadcast;

namespace Carina.Infrastructure.Tests.DataBroadcast;

public sealed class HybridcastPagesTests
{
    private const string Relay = "/api/live/32736-1024/data-broadcast/app/ticket";

    private const string Injected = "<script src=\"/receiver.js\"></script>";

    private static readonly Uri At = new("http://hc.station.example.jp/p/index.html");

    [Fact(DisplayName = "BR-BA-004: the receiver's script is put first into the head, once")]
    public void TheReceiverScriptIsPutFirstIntoTheHeadOnce()
    {
        string page = Document("<!DOCTYPE html><html><head><title>t</title></head><body><head></head></body></html>");

        Assert.Equal(1, Count(page, Injected));
        Assert.StartsWith("<!DOCTYPE html><html><head>" + Injected + "<title>", page, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-BA-004: a head written with attributes and in capitals is still the head")]
    public void AHeadWithAttributesIsStillTheHead()
    {
        string page = Document("<HTML lang=\"ja\"><HEAD profile=\"x\"><META charset=\"utf-8\"></HEAD><body></body></HTML>");

        Assert.Contains("<HEAD profile=\"x\">" + Injected + "<META", page, StringComparison.Ordinal);
        Assert.Equal(1, Count(page, Injected));
    }

    [Theory(DisplayName = "BR-BA-004: a document without a head is given the script once, before anything it shows")]
    [InlineData("<html><body><p>a</p></body></html>", "<html>" + Injected + "<body>")]
    [InlineData("<!doctype html><body><p>a</p></body>", "<!doctype html>" + Injected + "<body>")]
    [InlineData("<p>a</p>", Injected + "<p>a</p>")]
    [InlineData("<header>top</header>", Injected + "<header>")]
    public void ADocumentWithoutAHeadIsGivenTheScriptOnce(string document, string expected)
    {
        string page = Document(document);

        Assert.Equal(1, Count(page, Injected));
        Assert.Contains(expected, page, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-BA-004: a head written inside a comment or a script is not the head, and one after a script comes too late")]
    public void AHeadInACommentOrAScriptIsNotTheHead()
    {
        string page = Document("<html><!-- <head> --><script>var s = '<head>';</script><head></head></html>");

        Assert.Equal(1, Count(page, Injected));
        Assert.Contains("<!-- <head> -->", page, StringComparison.Ordinal);
        Assert.StartsWith("<html>" + Injected + "<!-- <head> -->", page, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-BA-004: a UTF-8 byte order mark stays first")]
    public void AByteOrderMarkStaysFirst()
    {
        byte[] page = HybridcastPages.Document([0xEF, 0xBB, 0xBF, .. "<p>a</p>"u8], At, target => HybridcastPages.Address(Relay, target), Injected);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, page[..3]);
        Assert.StartsWith(Injected, Encoding.Latin1.GetString(page[3..]), StringComparison.Ordinal);
    }

    [Theory(DisplayName = "FR-016: a path from the root, a relative path and a whole address are all relayed where they point")]
    [InlineData("<script src=\"/common/jquery.js\"></script>", "http://hc.station.example.jp/common/jquery.js")]
    [InlineData("<img src='img/a.png'>", "http://hc.station.example.jp/p/img/a.png")]
    [InlineData("<link rel=stylesheet href=../css/a.css>", "http://hc.station.example.jp/css/a.css")]
    [InlineData("<a href=\"https://hc.station.example/hc-home/index.html\">", "https://hc.station.example/hc-home/index.html")]
    [InlineData("<form action=\"/vote?x=1&amp;y=2\">", "http://hc.station.example.jp/vote?x=1&y=2")]
    [InlineData("<script SRC = \"//cdn.station.example.jp/a.js\"></script>", "http://cdn.station.example.jp/a.js")]
    public void AddressesAreRelayedWhereTheyPoint(string tag, string target)
    {
        string page = Document(tag);

        Assert.Contains($"\"{Relay}?uri={Uri.EscapeDataString(target)}\"", page, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "FR-016: a fragment stays a fragment of the relayed address")]
    public void AFragmentStaysAFragment()
    {
        string page = Document("<a href=\"next.html#top\">");

        Assert.Contains($"\"{Relay}?uri={Uri.EscapeDataString("http://hc.station.example.jp/p/next.html")}#top\"", page, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "FR-016: what is not an address on the web is left as it is written")]
    [InlineData("<a href=\"#top\">")]
    [InlineData("<a href=\"javascript:void(0)\">")]
    [InlineData("<img src=\"data:image/png;base64,AAAA\">")]
    [InlineData("<a href=\"mailto:a@station.example.jp\">")]
    [InlineData("<img src=\"\">")]
    [InlineData("<div title=\"/common/a.js\">")]
    public void WhatIsNotAnAddressOnTheWebIsLeftAsItIs(string tag)
    {
        Assert.Equal(Injected + tag, Document(tag));
    }

    [Fact(DisplayName = "FR-016: a script's own text is left alone, and an address inside it is not touched")]
    public void AScriptsTextIsLeftAlone()
    {
        string script = "<script>if (a<b && c>d) { load(\"/x.js\"); } var t = '<img src=\"/y.png\">';</script>";

        Assert.Equal(Injected + script, Document(script));
    }

    [Fact(DisplayName = "FR-016: a style sheet in the document and in an attribute is relayed where its addresses point")]
    public void AStyleSheetInTheDocumentIsRelayed()
    {
        string page = Document("<style>body { background: url(/img/bg.png) } @import \"a.css\";</style><div style=\"background:url('b.png')\">");

        Assert.Contains($"url(\"{Relay}?uri={Uri.EscapeDataString("http://hc.station.example.jp/img/bg.png")}\")", page, StringComparison.Ordinal);
        Assert.Contains($"@import \"{Relay}?uri={Uri.EscapeDataString("http://hc.station.example.jp/p/a.css")}\"", page, StringComparison.Ordinal);
        Assert.Contains($"url('{Relay}?uri={Uri.EscapeDataString("http://hc.station.example.jp/p/b.png")}')", page, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "FR-016: a style sheet of its own resolves its addresses against where it was taken from")]
    public void AStyleSheetOfItsOwnResolvesAgainstItsAddress()
    {
        byte[] sheet = HybridcastPages.Style("a { background: url(../img/c.png) } b { background: url(data:image/png;base64,AA) }"u8, new Uri("http://hc.station.example.jp/css/main.css"), target => HybridcastPages.Address(Relay, target));

        string text = Encoding.Latin1.GetString(sheet);
        Assert.Contains($"url(\"{Relay}?uri={Uri.EscapeDataString("http://hc.station.example.jp/img/c.png")}\")", text, StringComparison.Ordinal);
        Assert.Contains("url(data:image/png;base64,AA)", text, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "FR-016: text in Shift_JIS, EUC-JP or UTF-8 passes through byte for byte")]
    public void TextPassesThroughByteForByte()
    {
        byte[] shiftJis = [0x3C, 0x70, 0x3E, 0x83, 0x5C, 0x82, 0xA0, 0x3C, 0x2F, 0x70, 0x3E];
        byte[] utf8 = Encoding.UTF8.GetBytes("<p>天気</p>");

        Assert.Equal(shiftJis, HybridcastPages.Document(shiftJis, At, target => HybridcastPages.Address(Relay, target), Injected)[Injected.Length..]);
        Assert.Equal(utf8, HybridcastPages.Document(utf8, At, target => HybridcastPages.Address(Relay, target), Injected)[Injected.Length..]);
    }

    [Theory(DisplayName = "BR-BA-004: a document is HTML or XHTML, and a style sheet is CSS")]
    [InlineData("text/html; charset=utf-8", true, false)]
    [InlineData("application/xhtml+xml", true, false)]
    [InlineData("text/css", false, true)]
    [InlineData("application/javascript", false, false)]
    [InlineData(null, false, false)]
    public void ADocumentIsHtmlAndAStyleSheetIsCss(string? contentType, bool document, bool style)
    {
        Assert.Equal(document, HybridcastPages.IsDocument(contentType));
        Assert.Equal(style, HybridcastPages.IsStyle(contentType));
    }

    [Fact(DisplayName = "BR-BA-004: the receiver's script tells it where the relay and the socket are and which document it sits in")]
    public void TheInjectionNamesTheRelayTheSocketAndTheDocument()
    {
        string injection = HybridcastPages.Injection("/hybridcast/receiver.js", Relay, "/api/live/32736-1024/data-broadcast/ws/ticket", new Uri("http://hc.station.example.jp/p/index.html?a=1&b=\"2\""));

        Assert.Equal(
            "<script src=\"/hybridcast/receiver.js\" data-hybridcast-relay=\"/api/live/32736-1024/data-broadcast/app/ticket\" "
            + "data-hybridcast-socket=\"/api/live/32736-1024/data-broadcast/ws/ticket\" "
            + "data-hybridcast-document=\"http://hc.station.example.jp/p/index.html?a=1&amp;b=%222%22\"></script>",
            injection);
    }

    private static string Document(string document)
        => Encoding.Latin1.GetString(HybridcastPages.Document(Encoding.Latin1.GetBytes(document), At, target => HybridcastPages.Address(Relay, target), Injected));

    private static int Count(string text, string part)
    {
        int count = 0;

        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
