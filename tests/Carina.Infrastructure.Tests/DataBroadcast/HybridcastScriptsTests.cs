using System.Diagnostics;
using System.Text;

using Carina.Infrastructure.DataBroadcast;

namespace Carina.Infrastructure.Tests.DataBroadcast;

public sealed class HybridcastScriptsTests
{
    [Theory(DisplayName = "BR-BA-004: the location used as a value is pointed at the frame's stand-in, assignments included")]
    [InlineData("location.href", "__hcLocation.href")]
    [InlineData("var h = location.hostname;", "var h = __hcLocation.hostname;")]
    [InlineData("if(location.hostname.indexOf(\"nhk\")>=0){a=1}", "if(__hcLocation.hostname.indexOf(\"nhk\")>=0){a=1}")]
    [InlineData("!function(){var e=location.protocol+\"//\"+location.host}();", "!function(){var e=__hcLocation.protocol+\"//\"+__hcLocation.host}();")]
    [InlineData("location = \"/next.html\";", "__hcLocation = \"/next.html\";")]
    [InlineData("location.href = 'a.html'; location.reload();", "__hcLocation.href = 'a.html'; __hcLocation.reload();")]
    [InlineData("if (typeof location !== 'undefined') {}", "if (typeof __hcLocation !== 'undefined') {}")]
    [InlineData("var s = ok ? location : other;", "var s = ok ? __hcLocation : other;")]
    [InlineData("send(location, location.search)", "send(__hcLocation, __hcLocation.search)")]
    [InlineData("var n = a / location.pathname.length / 2;", "var n = a / __hcLocation.pathname.length / 2;")]
    [InlineData("x.location = location;", "x.location = __hcLocation;")]
    [InlineData("var f = () => location.href;", "var f = () => __hcLocation.href;")]
    [InlineData("var u = `${location.protocol}//${location.host}/a`;", "var u = `${__hcLocation.protocol}//${__hcLocation.host}/a`;")]
    [InlineData("var u = `a${`b${location.host}`}c${{k: location}.k}`;", "var u = `a${`b${__hcLocation.host}`}c${{k: __hcLocation}.k}`;")]
    [InlineData("post({location});", "post({location: __hcLocation});")]
    [InlineData("post({a, location});", "post({a, location: __hcLocation});")]
    [InlineData("var o = {...location};", "var o = {...__hcLocation};")]
    [InlineData("for (k in location) {}", "for (k in __hcLocation) {}")]
    [InlineData("switch (x) { case location: break; }", "switch (x) { case __hcLocation: break; }")]
    [InlineData("a = b++ / location.port / 2", "a = b++ / __hcLocation.port / 2")]
    [InlineData("if (a) /x/.test(location.href)", "if (a) /x/.test(__hcLocation.href)")]
    [InlineData("function f() {} location.href", "function f() {} __hcLocation.href")]
    [InlineData("var o = {} / 2, h = location.host", "var o = {} / 2, h = __hcLocation.host")]
    [InlineData("var o={class:\"a\"};function f(){location.href=\"x\"}", "var o={class:\"a\"};function f(){__hcLocation.href=\"x\"}")]
    [InlineData("(function($,window,document){var h=window.location.host;})(jQuery,window,document)", "(function($,window,document){var h=window.__hcLocation.host;})(jQuery,window,document)")]
    public void TheLocationUsedAsAValueIsPointedAtTheStandIn(string script, string expected)
        => Assert.Equal(expected, HybridcastScripts.Rewritten(script));

    [Theory(DisplayName = "BR-BA-004: the location of window, self, document or globalThis is the window's stand-in")]
    [InlineData("var h = window.location.hostname;", "var h = window.__hcLocation.hostname;")]
    [InlineData("self.location.href = 'a';", "window.__hcLocation.href = 'a';")]
    [InlineData("document.location.reload()", "window.__hcLocation.reload()")]
    [InlineData("globalThis.location.search", "window.__hcLocation.search")]
    [InlineData("window . location . host", "window.__hcLocation . host")]
    [InlineData("window.location = 'next.html'", "window.__hcLocation = 'next.html'")]
    public void TheLocationOfTheGlobalObjectIsTheWindowsStandIn(string script, string expected)
        => Assert.Equal(expected, HybridcastScripts.Rewritten(script));

    [Theory(DisplayName = "BR-BA-004: strings, template text, comments and regular expressions are left as they are, and nothing is rewritten where nothing was found")]
    [InlineData("var a = \"location.hostname\";")]
    [InlineData("var a = 'location';")]
    [InlineData("var a = \"say \\\"location\\\" twice\";")]
    [InlineData("var a = 'it\\'s location';")]
    [InlineData("var a = \"line \\\nlocation\";")]
    [InlineData("var a = \"line \\\r\nlocation\";")]
    [InlineData("// location.href = 'x'\nvar a = 1;")]
    [InlineData("/* location.href */ var a = 1;")]
    [InlineData("/** a\n * location\n */ var a = 1;")]
    [InlineData("var t = `location.host is ${host}`;")]
    [InlineData("var t = `a\\`location\\`${x}`;")]
    [InlineData("var r = /location/.test(s);")]
    [InlineData("var r = s.replace(/\\/location\\//g, '');")]
    [InlineData("var r = s.match(/location[/]x/);")]
    [InlineData("return /location/i.test(s);")]
    [InlineData("var r = [/location/, /a\\/location/];")]
    [InlineData("x = a ? /location/ : /b/;")]
    [InlineData("<!--\nvar a = 1;\n-->")]
    [InlineData("var a = 1;")]
    [InlineData("")]
    public void TextThatIsNotCodeIsLeftAsItIs(string script)
        => Assert.Null(HybridcastScripts.Rewritten(script));

    [Theory(DisplayName = "BR-BA-004: a property name, a member of another object and a name that only contains the location are left as they are")]
    [InlineData("var o = {location: 1};")]
    [InlineData("var o = {\"location\": 1};")]
    [InlineData("var o = {a: 1, location: 2};")]
    [InlineData("var o = {location() { return 1; }};")]
    [InlineData("var o = {get location() { return 1; }, set location(v) {}};")]
    [InlineData("class A { location() {} static location = 1; }")]
    [InlineData("class A extends B { get location() { return 1; } }")]
    [InlineData("a.location.href")]
    [InlineData("this.location = x;")]
    [InlineData("frame.window.location.href")]
    [InlineData("a?.location")]
    [InlineData("this.#location = 1;")]
    [InlineData("var mylocation = location2 + $location + location_ + _location;")]
    [InlineData("location: for (;;) { break location; }")]
    [InlineData("parent.location.href")]
    public void ANameThatIsNotTheGlobalLocationIsLeftAsItIs(string script)
        => Assert.Null(HybridcastScripts.Rewritten(script));

    [Theory(DisplayName = "BR-BA-004: a scope that declares a location of its own is left as it is, and only that scope")]
    [InlineData("function f(location) { return location.href; }", "function f(location) { return location.href; }")]
    [InlineData("function f(a, location) { return location.href; } location.href", "function f(a, location) { return location.href; } __hcLocation.href")]
    [InlineData("var g = function (x, {location}) { return location; }; location.host", "var g = function (x, {location}) { return location; }; __hcLocation.host")]
    [InlineData("var g = (location) => location.href; location.host", "var g = (location) => location.href; __hcLocation.host")]
    [InlineData("var g = location => location.href, h = location.host", "var g = location => location.href, h = __hcLocation.host")]
    [InlineData("function f() { var location = {}; return location.x; } location.href", "function f() { var location = {}; return location.x; } __hcLocation.href")]
    [InlineData("function f() { if (a) { var location = 1; } return location; } location.host", "function f() { if (a) { var location = 1; } return location; } __hcLocation.host")]
    [InlineData("if (a) { let location = 1; use(location); } use(location);", "if (a) { let location = 1; use(location); } use(__hcLocation);")]
    [InlineData("try { a(); } catch (location) { use(location); } use(location);", "try { a(); } catch (location) { use(location); } use(__hcLocation);")]
    [InlineData("function f() { const {location} = obj; return location; } location.href", "function f() { const {location} = obj; return location; } __hcLocation.href")]
    [InlineData("function f() { function location() {} return location; } location.href", "function f() { function location() {} return location; } __hcLocation.href")]
    [InlineData("function f() { var self = this; self.location = 1; } self.location.href", "function f() { var self = this; self.location = 1; } window.__hcLocation.href")]
    [InlineData("function f(a = location.href) { return a; }", "function f(a = __hcLocation.href) { return a; }")]
    [InlineData("function f(location){var h=location.host;return g(location)} location.href", "function f(location){var h=location.host;return g(location)} __hcLocation.href")]
    [InlineData("function f(){var self=this;x=self.location;g(self)} self.location.href", "function f(){var self=this;x=self.location;g(self)} window.__hcLocation.href")]
    [InlineData("function f(){var a=1,location={};return location.href} location.href", "function f(){var a=1,location={};return location.href} __hcLocation.href")]
    [InlineData("if (a) { let b = 1, location = {}; use(location); } use(location);", "if (a) { let b = 1, location = {}; use(location); } use(__hcLocation);")]
    [InlineData("function f(){var a = g(1, 2), self = this; return self.location} use(location)", "function f(){var a = g(1, 2), self = this; return self.location} use(__hcLocation)")]
    [InlineData("function f(){var a = [b, location]; return a} use(location)", "function f(){var a = [b, __hcLocation]; return a} use(__hcLocation)")]
    public void AScopeThatDeclaresItsOwnLocationIsLeftAsItIs(string script, string expected)
        => Assert.Equal(expected, HybridcastScripts.Rewritten(script) ?? script);

    [Theory(DisplayName = "BR-BA-004: a script that declares the location where it begins is left as it is")]
    [InlineData("var location = 1; location.href")]
    [InlineData("let location = 1; use(location);")]
    [InlineData("function location() {} location();")]
    [InlineData("var a=1,location={};location.href")]
    [InlineData("const {location} = window; location.href")]
    [InlineData("var {location: l} = self; l.href")]
    [InlineData("let {a, location} = obj; location.href")]
    [InlineData("import {location} from \"./a.js\"; location.href")]
    [InlineData("import {a as location} from \"./a.js\"; location.href")]
    [InlineData("import location from \"./a.js\"; location.href")]
    [InlineData("import * as location from \"./a.js\"; location.href")]
    [InlineData("export {location};")]
    public void AScriptThatDeclaresTheLocationIsLeftAsItIs(string script)
        => Assert.Null(HybridcastScripts.Rewritten(script));

    [Fact(DisplayName = "BR-BA-004: in Shift_JIS, a second byte that looks like a backslash or a quote is part of its character, not of the code")]
    public void AShiftJisSecondByteIsPartOfItsCharacter()
    {
        string misleading = Encoding.Latin1.GetString([.. "var a=\""u8, 0x95, 0x5C, .. "\",b=\"location\";"u8]);
        string pointed = Encoding.Latin1.GetString([.. "var a=\""u8, 0x83, 0x5C, .. "\";var t=`"u8, 0x83, 0x60, .. "`;location.href"u8]);

        Assert.Null(HybridcastScripts.Rewritten(misleading, shiftJis: true));
        Assert.EndsWith(";__hcLocation.href", HybridcastScripts.Rewritten(pointed, shiftJis: true), StringComparison.Ordinal);
    }

    [Theory(DisplayName = "BR-BA-004: a script written to make the reading slow is read, or given up on, within a bounded time")]
    [InlineData("regular expressions that never close")]
    [InlineData("declarations deep inside groups")]
    [InlineData("many scopes that declare the location")]
    [InlineData("arrows nested inside one another")]
    [InlineData("patterns nested deep")]
    [InlineData("arrows around a template of many substitutions")]
    public void AScriptWrittenToBeSlowIsReadInBoundedTime(string shape)
    {
        string script = shape switch
        {
            "regular expressions that never close" => string.Concat(Enumerable.Repeat("/[", 1_000_000)),
            "declarations deep inside groups" => new string('(', 1_000_000) + string.Concat(Enumerable.Repeat("var self;", 110_000)),
            "many scopes that declare the location" => string.Concat(Enumerable.Repeat("function f(location){}", 47_000)) + string.Concat(Enumerable.Repeat("location;", 117_000)),
            "arrows nested inside one another" => string.Concat(Enumerable.Repeat("location=>", 200_000)) + "location",
            "patterns nested deep" => new string('[', 1_000_000) + string.Concat(Enumerable.Repeat(",self", 200_000)),
            _ => string.Concat(Enumerable.Repeat("location=>", 2000)) + "`" + string.Concat(Enumerable.Repeat("${}", 650_000)) + "`",
        };
        Stopwatch timed = Stopwatch.StartNew();

        Exception? failure = Record.Exception(() => HybridcastScripts.Rewritten(script));

        Assert.Null(failure);
        Assert.True(script.Length <= HybridcastScripts.Largest);
        Assert.True(timed.Elapsed < TimeSpan.FromSeconds(10), $"reading {shape} took {timed.Elapsed}");
    }

    [Fact(DisplayName = "BR-BA-004: characters outside ASCII, read one byte to one character, pass through unchanged")]
    public void CharactersOutsideAsciiPassThrough()
    {
        string script = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes("var s = \"天気\"; var 位置 = location.href; /* 場所 */"));

        string rewritten = HybridcastScripts.Rewritten(script)!;

        Assert.Equal("var s = \"天気\"; var 位置 = __hcLocation.href; /* 場所 */", Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(rewritten)));
    }

    [Theory(DisplayName = "BR-BA-004: a script that ends part way through a string, a template, a comment, a regular expression or a group is read to its end without failing")]
    [InlineData("var a = \"location")]
    [InlineData("var a = `${location")]
    [InlineData("/* location")]
    [InlineData("f(location")]
    [InlineData("var r = /location")]
    [InlineData("}}}) location ((([")]
    public void AScriptThatEndsPartWayIsReadToItsEnd(string script)
    {
        Exception? failure = Record.Exception(() => HybridcastScripts.Rewritten(script));

        Assert.Null(failure);
    }

    [Fact(DisplayName = "BR-BA-004: no arrangement of the pieces a script is made of makes the reading fail")]
    public void NoArrangementMakesTheReadingFail()
    {
        string[] pieces =
        [
            "location", "window", "self", ".", "?.", "(", ")", "{", "}", "[", "]", "`", "${", "/", "'", "\"", "\\",
            "=>", ":", "?", ",", ";", "var ", "let ", "function ", "catch", "class ", "if", "return ", "=", "+", "++",
            "//", "/*", "*/", "\n", "x", "1", "#", "<!--", "-->", "...",
        ];
        Random random = new(20261014);

        for (int round = 0; round < 3000; round++)
        {
            string script = string.Concat(Enumerable.Range(0, random.Next(1, 40)).Select(_ => pieces[random.Next(pieces.Length)]));

            Exception? failure = Record.Exception(() => HybridcastScripts.Rewritten(script));

            Assert.True(failure is null, $"reading {script} failed: {failure}");
        }
    }

    [Fact(DisplayName = "BR-BA-004: a script over 2 MiB is left as it is, and one of 2 MiB is rewritten within a bounded time")]
    public void AScriptOverTheLimitIsLeftAsItIs()
    {
        string unit = "if(location.hostname.indexOf(\"nhk\")>=0){a=[/x\\//g,`${b}`,'c'];}\n";
        StringBuilder built = new(HybridcastScripts.Largest + unit.Length);

        while (built.Length + unit.Length <= HybridcastScripts.Largest)
        {
            built.Append(unit);
        }

        string most = built.ToString().PadRight(HybridcastScripts.Largest, ' ');
        Stopwatch timed = Stopwatch.StartNew();
        string? rewritten = HybridcastScripts.Rewritten(most);
        timed.Stop();

        Assert.NotNull(rewritten);
        Assert.DoesNotContain("(location.", rewritten, StringComparison.Ordinal);
        Assert.True(timed.Elapsed < TimeSpan.FromSeconds(10), $"rewriting 2 MiB took {timed.Elapsed}");
        Assert.Null(HybridcastScripts.Rewritten(most + " "));
    }

    [Fact(DisplayName = "BR-BA-004: groups nested far deeper than any script nests them are read without running out of stack")]
    public void DeepNestingIsReadWithoutRunningOutOfStack()
    {
        string script = new string('(', 200_000) + "location" + new string(')', 200_000);

        Assert.StartsWith("((((", HybridcastScripts.Rewritten(script), StringComparison.Ordinal);
    }
}
