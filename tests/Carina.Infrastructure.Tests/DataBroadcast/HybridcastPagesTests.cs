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

    [Fact(DisplayName = "FR-016: an address inside a script's own text is not touched")]
    public void AScriptsTextIsLeftAlone()
    {
        string script = "<script>if (a<b && c>d) { load(\"/x.js\"); } var t = '<img src=\"/y.png\">';</script>";

        Assert.Equal(Injected + script, Document(script));
    }

    [Theory(DisplayName = "BR-BA-004: a script in the document is pointed at the frame's stand-in for its location, by whichever type it is named a script")]
    [InlineData("<script>if(location.hostname==\"a\"){}</script>", "<script>if(__hcLocation.hostname==\"a\"){}</script>")]
    [InlineData("<script type=\"text/javascript\">var h = window.location.host;</script>", "<script type=\"text/javascript\">var h = window.__hcLocation.host;</script>")]
    [InlineData("<SCRIPT type=module>location.reload()</SCRIPT>", "<SCRIPT type=module>__hcLocation.reload()</SCRIPT>")]
    [InlineData("<script type='text/javascript; charset=Shift_JIS'>location.href</script>", "<script type='text/javascript; charset=Shift_JIS'>__hcLocation.href</script>")]
    [InlineData("<script language=\"JavaScript\">\n<!--\nlocation.href = 'a.html';\n//-->\n</script>", "<script language=\"JavaScript\">\n<!--\n__hcLocation.href = 'a.html';\n//-->\n</script>")]
    public void AScriptInTheDocumentIsPointedAtTheStandIn(string document, string expected)
    {
        Assert.Equal(Injected + expected, Document(document));
    }

    [Theory(DisplayName = "BR-BA-004: a script block of another type, and a script with no location to point, are left as they are")]
    [InlineData("<script type=\"text/template\"><p>{{location.href}}</p></script>")]
    [InlineData("<script type=\"application/ld+json\">{\"a\": location}</script>")]
    [InlineData("<script>var a = 'location.href';</script>")]
    public void AScriptBlockOfAnotherTypeIsLeftAsItIs(string document)
    {
        Assert.Equal(Injected + document, Document(document));
    }

    [Theory(DisplayName = "BR-BA-004: an event handler is pointed at the stand-in, its entities read and kept as they were written")]
    [InlineData("<a onclick=\"location.href='a.html'\">", "<a onclick=\"__hcLocation.href='a.html'\">")]
    [InlineData("<a onclick=\"location.href=&quot;a.html&quot;\">", "<a onclick=\"__hcLocation.href=&quot;a.html&quot;\">")]
    [InlineData("<body ONLOAD='if(location.search){go()}'>", "<body ONLOAD='if(__hcLocation.search){go()}'>")]
    [InlineData("<button onclick=location.reload()>", "<button onclick=\"__hcLocation.reload()\">")]
    [InlineData("<a onclick=\"go&#40;location&#41;\">", "<a onclick=\"go&#40;__hcLocation&#41;\">")]
    [InlineData("<a onclick=\"go(&#x6c;ocation)\">", "<a onclick=\"go(__hcLocation)\">")]
    [InlineData("<a onclick=\"x=a&location;\">", "<a onclick=\"x=a&__hcLocation;\">")]
    public void AnEventHandlerIsPointedAtTheStandIn(string document, string expected)
    {
        Assert.Equal(Injected + expected, Document(document));
    }

    [Theory(DisplayName = "BR-BA-004: a handler naming the location only in a string, and an attribute that is not a handler, are left as they are")]
    [InlineData("<a onclick=\"alert(&quot;location.href&quot;)\">")]
    [InlineData("<a onclick=\"alert('location')\">")]
    [InlineData("<div title=\"location.href\" data-onclick=\"location\" on=\"location\">")]
    [InlineData("<button onclick=location.href='a.html'>")]
    public void AHandlerNamingTheLocationOnlyInAStringIsLeftAsItIs(string document)
    {
        Assert.Equal(Injected + document, Document(document));
    }

    [Theory(DisplayName = "BR-BA-004: a document in Shift_JIS, by its type, by its own meta element or by its bytes, has its scripts read with their second bytes kept apart")]
    [InlineData("text/html; charset=Shift_JIS", "")]
    [InlineData("text/html; charset=Shift_JIS", "<meta charset=\"utf-8\">")]
    [InlineData("text/html", "<meta charset=\"Shift_JIS\">")]
    [InlineData("text/html", "<meta http-equiv=\"Content-Type\" content=\"text/html; charset=shift_jis\">")]
    [InlineData("text/html", "")]
    public void ADocumentInShiftJisKeepsItsSecondBytesApart(string contentType, string meta)
    {
        byte[] document = [.. Encoding.ASCII.GetBytes($"<head>{meta}<script>var a=\""), 0x95, 0x5C, .. "\",b=\"location\";location.href</script></head>"u8];

        string page = Encoding.Latin1.GetString(HybridcastPages.Document(document, At, target => HybridcastPages.Address(Relay, target), Injected, HybridcastPages.DeclaredShiftJis(contentType)));

        Assert.Contains("b=\"location\";__hcLocation.href</script>", page, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "BR-BA-004: a document whose type names a character set other than Shift_JIS is read in it, whatever a meta element says")]
    [InlineData("text/html; charset=utf-8", "<meta charset=\"Shift_JIS\">")]
    [InlineData("text/html", "<meta charset=\"utf-8\">")]
    [InlineData("text/html", "")]
    public void ADocumentNamedAnotherCharacterSetIsReadInIt(string contentType, string meta)
    {
        byte[] document = Encoding.UTF8.GetBytes($"<head>{meta}<script>var a=\"表あ\",b=\"location\";location.href</script></head>");

        string page = Encoding.UTF8.GetString(HybridcastPages.Document(document, At, target => HybridcastPages.Address(Relay, target), Injected, HybridcastPages.DeclaredShiftJis(contentType)));

        Assert.Contains("var a=\"表あ\",b=\"location\";__hcLocation.href</script>", page, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "BR-BA-004: a script that names no character set is read in Shift_JIS only where its bytes are neither UTF-8 nor EUC-JP")]
    [InlineData(new byte[] { 0x95, 0x5C }, true)]
    [InlineData(new byte[] { 0x83, 0x5C, 0x83, 0x74, 0x83, 0x67 }, true)]
    [InlineData(new byte[] { 0xE3, 0x81, 0x82, 0xE8, 0xA1, 0xA8 }, false)]
    [InlineData(new byte[] { 0xA4, 0xA2, 0xC9, 0xBD, 0xE5, 0xA1 }, false)]
    [InlineData(new byte[] { 0x41 }, false)]
    public void AScriptNamingNoCharacterSetIsReadByItsBytes(byte[] text, bool shiftJis)
    {
        byte[] script = [.. "var a=\""u8, .. text, .. "\",b=\"location\";location.href"u8];

        ReadOnlyMemory<byte> handed = HybridcastPages.Script(script, HybridcastPages.DeclaredShiftJis("application/javascript"));

        Assert.Equal([.. "var a=\""u8, .. text, .. "\",b=\"location\";__hcLocation.href"u8], handed.ToArray());
        Assert.Equal(shiftJis, HybridcastPages.LooksShiftJis(script));
    }

    [Theory(DisplayName = "BR-BA-004: the character set a server names is Shift_JIS by any of its names, another, or none")]
    [InlineData("application/javascript; charset=Shift_JIS", true)]
    [InlineData("text/javascript; charset=\"windows-31j\"", true)]
    [InlineData("text/html; charset=x-sjis", true)]
    [InlineData("text/html; charset=utf-8", false)]
    [InlineData("text/html", null)]
    [InlineData(null, null)]
    public void TheNamedCharacterSetIsShiftJisAnotherOrNone(string? contentType, bool? shiftJis)
    {
        Assert.Equal(shiftJis, HybridcastPages.DeclaredShiftJis(contentType));
    }

    [Theory(DisplayName = "BR-BA-004: a script handed over is JavaScript or ECMAScript by any of the types a server names it")]
    [InlineData("application/javascript", true)]
    [InlineData("text/javascript; charset=Shift_JIS", true)]
    [InlineData("application/x-javascript", true)]
    [InlineData("text/ecmascript", true)]
    [InlineData("application/ecmascript", true)]
    [InlineData("application/json", false)]
    [InlineData("text/html", false)]
    [InlineData(null, false)]
    public void AScriptIsJavaScriptOrEcmaScript(string? contentType, bool script)
    {
        Assert.Equal(script, HybridcastPages.IsScript(contentType));
    }

    [Fact(DisplayName = "BR-BA-004: a script handed over is pointed at the stand-in, and passes through byte for byte everywhere else")]
    public void AScriptHandedOverIsPointedAtTheStandIn()
    {
        ReadOnlyMemory<byte> script = Encoding.UTF8.GetBytes("var s = \"天気\"; if (location.hostname.indexOf(\"nhk\") >= 0) { go(); }");

        ReadOnlyMemory<byte> handed = HybridcastPages.Script(script);

        Assert.Equal("var s = \"天気\"; if (__hcLocation.hostname.indexOf(\"nhk\") >= 0) { go(); }", Encoding.UTF8.GetString(handed.Span));
    }

    [Fact(DisplayName = "BR-BA-004: a script with no location to point, and one over 2 MiB, are handed back as the very bytes they came as")]
    public void AScriptWithNothingToPointIsHandedBackAsItCame()
    {
        ReadOnlyMemory<byte> plain = Encoding.UTF8.GetBytes("var a = 'location.href'; // location");
        byte[] large = new byte[HybridcastScripts.Largest + 1];
        Array.Fill(large, (byte)' ');
        Encoding.ASCII.GetBytes("location.href").CopyTo(large, 0);
        ReadOnlyMemory<byte> over = large;

        Assert.True(plain.Equals(HybridcastPages.Script(plain)));
        Assert.True(over.Equals(HybridcastPages.Script(over)));
    }

    [Theory(DisplayName = "BR-BA-004: a broadcast video object has its type kept under data-hybridcast-type, however the attribute is written")]
    [InlineData("<object type=\"video/x-iptvf-broadcast\" id=\"v\"></object>", "<object data-hybridcast-type=\"video/x-iptvf-broadcast\" id=\"v\"></object>")]
    [InlineData("<object id=\"v\" type='video/broadcast'>", "<object id=\"v\" data-hybridcast-type='video/broadcast'>")]
    [InlineData("<object type=video/broadcast width=960>", "<object data-hybridcast-type=video/broadcast width=960>")]
    [InlineData("<OBJECT TYPE = \"Video/X-IPTVF-Broadcast\">", "<OBJECT data-hybridcast-type = \"Video/X-IPTVF-Broadcast\">")]
    [InlineData("<object\ttype=\" video/broadcast \"\nclass=\"tv\">", "<object\tdata-hybridcast-type=\" video/broadcast \"\nclass=\"tv\">")]
    public void ABroadcastVideoObjectKeepsItsTypeUnderAnotherName(string tag, string expected)
    {
        Assert.Equal(Injected + expected, Document(tag));
    }

    [Theory(DisplayName = "BR-BA-004: another object, another element, and an object written in a comment or a script are left as they are")]
    [InlineData("<object type=\"application/pdf\" id=\"d\"></object>")]
    [InlineData("<object type=\"video/broadcast-like\"></object>")]
    [InlineData("<embed type=\"video/broadcast\">")]
    [InlineData("<div type=\"video/broadcast\" data-type=\"video/broadcast\">")]
    [InlineData("<object data-type=\"video/broadcast\" typeof=\"video/broadcast\">")]
    [InlineData("<!-- <object type=\"video/broadcast\"> -->")]
    [InlineData("<script>var s = '<object type=\"video/broadcast\">';</script>")]
    public void AnotherObjectIsLeftAsItIs(string document)
    {
        string page = Document(document);

        Assert.Equal(document, page.Replace(Injected, string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain("data-hybridcast-type", page, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-BA-004: a document in Shift_JIS around a broadcast video object passes through byte for byte except the attribute's name")]
    public void AShiftJisDocumentAroundABroadcastObjectPassesThrough()
    {
        byte[] document = [.. "<p>"u8, 0x83, 0x5C, 0x83, 0x74, .. "</p><object type=\"video/broadcast\"></object>"u8];
        byte[] expected = [.. Encoding.ASCII.GetBytes(Injected), .. "<p>"u8, 0x83, 0x5C, 0x83, 0x74, .. "</p><object data-hybridcast-type=\"video/broadcast\"></object>"u8];

        Assert.Equal(expected, HybridcastPages.Document(document, At, target => HybridcastPages.Address(Relay, target), Injected, shiftJis: true));
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
