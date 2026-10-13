using System.Collections.Frozen;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// Turns a Hybridcast application's document, style sheets and scripts into ones that are read through the relay.
/// Every address the markup and the style sheets name — from the root, relative or whole — is resolved against where
/// the document was taken from and pointed at the relay, so it reaches the same server it would have reached had the
/// application been opened there. A script, whether handed over on its own, written into the document or written into
/// an event handler, has its location pointed at the receiver script's stand-in (<see cref="HybridcastScripts"/>)
/// and is otherwise left alone; what it asks for at run time is the receiver script's to point. An object element of
/// the broadcast video type keeps its type under data-hybridcast-type instead, so the browser does not try to load
/// it as a plug-in and the receiver script can find it. The receiver script is put into the head once, as its first
/// element, and where there is no head, before the first thing the document shows. The bytes are read one to one as
/// Latin-1, so a document in any character set built on ASCII passes through unchanged except where it is rewritten.
/// </summary>
public static partial class HybridcastPages
{
    private const string Bom = "ï»¿";

    private static readonly FrozenSet<string> AddressAttributes = new[]
    {
        "src", "href", "action", "formaction", "poster", "data", "background",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> RawText = new[] { "script", "style" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private const string BroadcastObjectType = "data-hybridcast-type";

    private static readonly FrozenSet<string> BroadcastObjectTypes =
        new[] { "video/x-iptvf-broadcast", "video/broadcast" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> ShiftJisNames = new[]
    {
        "shift_jis", "shift-jis", "sjis", "x-sjis", "windows-31j", "cp932", "ms_kanji", "csshiftjis",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> DocumentTypes = new[] { "text/html", "application/xhtml+xml" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> ScriptTypes = new[]
    {
        "application/javascript", "text/javascript", "application/x-javascript", "text/x-javascript",
        "application/ecmascript", "text/ecmascript",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsDocument(string? contentType) => MediaTypeOf(contentType) is { } type && DocumentTypes.Contains(type);

    public static bool IsStyle(string? contentType) => string.Equals(MediaTypeOf(contentType), "text/css", StringComparison.OrdinalIgnoreCase);

    public static bool IsScript(string? contentType) => MediaTypeOf(contentType) is { } type && ScriptTypes.Contains(type);

    public static bool? DeclaredShiftJis(string? contentType)
        => CharsetOf(contentType) is { } charset ? IsShiftJisName(charset) : null;

    public static bool LooksShiftJis(ReadOnlySpan<byte> bytes)
        => bytes.IndexOfAnyInRange((byte)0x80, (byte)0xFF) >= 0 && !Utf8.IsValid(bytes) && !IsEucJp(bytes);

    public static ReadOnlyMemory<byte> Script(ReadOnlyMemory<byte> script, bool? shiftJis = null)
    {
        if (script.Length > HybridcastScripts.Largest)
        {
            return script;
        }

        string? rewritten = HybridcastScripts.Rewritten(Encoding.Latin1.GetString(script.Span), shiftJis ?? LooksShiftJis(script.Span));

        return rewritten is null ? script : Encoding.Latin1.GetBytes(rewritten);
    }

    public static string Address(string relay, Uri target)
    {
        ArgumentException.ThrowIfNullOrEmpty(relay);
        ArgumentNullException.ThrowIfNull(target);

        return $"{relay}?uri={Uri.EscapeDataString(target.GetLeftPart(UriPartial.Query))}{target.Fragment}";
    }

    public static string Injection(string receiver, string relay, string socket, Uri document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return $"<script src=\"{WebUtility.HtmlEncode(receiver)}\" data-hybridcast-relay=\"{WebUtility.HtmlEncode(relay)}\" "
               + $"data-hybridcast-socket=\"{WebUtility.HtmlEncode(socket)}\" "
               + $"data-hybridcast-document=\"{WebUtility.HtmlEncode(document.AbsoluteUri)}\"></script>";
    }

    public static byte[] Document(ReadOnlySpan<byte> document, Uri at, Func<Uri, string> relayed, string injected, bool? shiftJis = null)
    {
        ArgumentNullException.ThrowIfNull(at);
        ArgumentNullException.ThrowIfNull(relayed);
        ArgumentNullException.ThrowIfNull(injected);

        string text = Encoding.Latin1.GetString(document);
        Markup markup = new(text, at, relayed, shiftJis, shiftJis ?? LooksShiftJis(document));
        markup.Walk();

        return Encoding.Latin1.GetBytes(markup.Written.Insert(markup.InjectAt, injected).ToString());
    }

    public static byte[] Style(ReadOnlySpan<byte> sheet, Uri at, Func<Uri, string> relayed)
    {
        ArgumentNullException.ThrowIfNull(at);
        ArgumentNullException.ThrowIfNull(relayed);

        return Encoding.Latin1.GetBytes(StyleText(Encoding.Latin1.GetString(sheet), at, relayed, '"'));
    }

    private static string? MediaTypeOf(string? contentType)
        => contentType is not null && MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? parsed) ? parsed.MediaType : null;

    private static bool IsScriptBlock(string attributes)
    {
        Match? type = Attribute().Matches(attributes).FirstOrDefault(found => string.Equals(found.Groups["name"].Value, "type", StringComparison.OrdinalIgnoreCase));

        if (type is null)
        {
            return true;
        }

        string value = Unquoted(type.Groups["value"].Value).Trim();

        return value.Length == 0 || string.Equals(value, "module", StringComparison.OrdinalIgnoreCase) || IsScript(value);
    }

    private static string Unquoted(string written) => written[0] is '"' or '\'' ? written[1..^1] : written;

    private static bool IsShiftJisName(string charset) => ShiftJisNames.Contains(charset.Trim('"', ' '));

    private static string? CharsetOf(string? contentType)
        => contentType is not null && MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? parsed) ? parsed.CharSet : null;

    private static bool IsEucJp(ReadOnlySpan<byte> bytes)
    {
        int at = 0;

        while (at < bytes.Length && EucJpLength(bytes[at..]) is int length and > 0)
        {
            at += length;
        }

        return at >= bytes.Length;
    }

    private static int EucJpLength(ReadOnlySpan<byte> rest)
    {
        static bool IsEucByte(byte b) => b is >= 0xA1 and <= 0xFE;

        return rest[0] switch
        {
            < 0x80 => 1,
            0x8E => rest.Length > 1 && rest[1] is >= 0xA1 and <= 0xDF ? 2 : 0,
            0x8F => rest.Length > 2 && IsEucByte(rest[1]) && IsEucByte(rest[2]) ? 3 : 0,
            >= 0xA1 and <= 0xFE => rest.Length > 1 && IsEucByte(rest[1]) ? 2 : 0,
            _ => 0,
        };
    }

    private static string? Handler(string raw, bool shiftJis)
    {
        (string decoded, int[] at) = Decoded(raw);
        IReadOnlyList<ScriptEdit> edits = HybridcastScripts.Edits(decoded, shiftJis);

        return edits.Count == 0
            ? null
            : HybridcastScripts.Applied(raw, [.. edits.Select(edit => edit with { Start = at[edit.Start], End = at[edit.End] })]);
    }

    private static (string Decoded, int[] At) Decoded(string raw)
    {
        StringBuilder decoded = new(raw.Length);
        List<int> at = new(raw.Length + 1);
        int from = 0;

        while (from < raw.Length)
        {
            Match entity = raw[from] == '&' ? Entity().Match(raw, from) : Match.Empty;
            string read = entity.Success ? WebUtility.HtmlDecode(entity.Value) : raw[from].ToString();
            bool known = entity.Success && read != entity.Value;
            read = known ? read : raw[from].ToString();

            decoded.Append(read);
            at.AddRange(Enumerable.Repeat(from, read.Length));
            from += known ? entity.Length : 1;
        }

        at.Add(raw.Length);

        return (decoded.ToString(), [.. at]);
    }

    private static string StyleText(string sheet, Uri at, Func<Uri, string> relayed, char quote)
    {
        string urls = Substituted(StyleUrl(), sheet, found => Pointed(found.Groups["value"].Value, at, relayed) is { } pointed
            ? $"url({quote}{pointed}{quote})"
            : found.Value);

        return Substituted(StyleImport(), urls, found => Pointed(found.Groups["value"].Value, at, relayed) is { } pointed
            ? $"@import {quote}{pointed}{quote}"
            : found.Value);
    }

    private static string Substituted(Regex pattern, string input, Func<Match, string> substitute)
    {
        StringBuilder written = new(input.Length);
        int from = 0;

        foreach (Match found in pattern.Matches(input))
        {
            written.Append(input, from, found.Index - from).Append(substitute(found));
            from = found.Index + found.Length;
        }

        return written.Append(input, from, input.Length - from).ToString();
    }

    private static string? Pointed(string written, Uri at, Func<Uri, string> relayed)
    {
        string address = WebUtility.HtmlDecode(written).Trim();

        if (address.Length == 0 || address.StartsWith('#') || !Uri.TryCreate(at, address, out Uri? target))
        {
            return null;
        }

        return target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps ? relayed(target) : null;
    }

    [GeneratedRegex("""\G<(?<closing>/?)(?<name>[A-Za-z][A-Za-z0-9:_-]*)(?<attributes>(?:[^>"']|"[^"]*"|'[^']*')*)>""")]
    private static partial Regex Tag();

    [GeneratedRegex("""(?<lead>\s)(?<name>[^\s"'>/=]+)(?<equals>\s*=\s*)(?<value>"[^"]*"|'[^']*'|[^\s"'=<>`]+)""")]
    private static partial Regex Attribute();

    [GeneratedRegex("""\G&(?:#[0-9]{1,7}|#[xX][0-9A-Fa-f]{1,6}|[A-Za-z][A-Za-z0-9]{1,31});""")]
    private static partial Regex Entity();

    [GeneratedRegex("""url\(\s*(?<quote>["']?)(?<value>[^"')]*)\k<quote>\s*\)""", RegexOptions.IgnoreCase)]
    private static partial Regex StyleUrl();

    [GeneratedRegex("""@import\s+(?<quote>["'])(?<value>[^"']*)\k<quote>""", RegexOptions.IgnoreCase)]
    private static partial Regex StyleImport();

    private sealed class Markup(string text, Uri at, Func<Uri, string> relayed, bool? declared, bool sniffed)
    {
        private bool inShiftJis = sniffed;

        private int? afterHead;

        private int? afterHtml;

        private int? beforeFirstShown;

        public StringBuilder Written { get; } = new(text.Length + 256);

        public int InjectAt => afterHead ?? afterHtml ?? beforeFirstShown ?? Written.Length;

        public void Walk()
        {
            int from = text.StartsWith(Bom, StringComparison.Ordinal) ? Bom.Length : 0;
            Written.Append(text, 0, from);

            while (from < text.Length)
            {
                from = Step(from);
            }
        }

        private int Step(int from)
        {
            int open = text.IndexOf('<', from);

            if (open < 0)
            {
                Shown(from, text.Length);

                return text.Length;
            }

            Shown(from, open);

            if (string.CompareOrdinal(text, open, "<!--", 0, 4) == 0)
            {
                return Copied(open, text.IndexOf("-->", open + 4, StringComparison.Ordinal), 3);
            }

            if (open + 1 < text.Length && text[open + 1] is '!' or '?')
            {
                return Copied(open, text.IndexOf('>', open), 1);
            }

            Match tag = Tag().Match(text, open);

            if (!tag.Success)
            {
                Shown(open, open + 1);

                return open + 1;
            }

            return Opened(tag);
        }

        private int Opened(Match tag)
        {
            string name = tag.Groups["name"].Value;
            bool closing = tag.Groups["closing"].Length > 0;
            int after = tag.Index + tag.Length;

            Placed(name, closing);
            Encoded(name, closing, tag.Groups["attributes"].Value);
            Written.Append('<').Append(tag.Groups["closing"].Value).Append(name).Append(Attributes(name, tag.Groups["attributes"].Value)).Append('>');
            Opening(name, closing);

            if (closing || !RawText.Contains(name))
            {
                return after;
            }

            int end = text.IndexOf("</" + name, after, StringComparison.OrdinalIgnoreCase);
            int stops = end < 0 ? text.Length : end;

            Written.Append(RawTextOf(name, tag.Groups["attributes"].Value, text[after..stops]));

            return stops;
        }

        private string RawTextOf(string name, string attributes, string inside)
        {
            if (string.Equals(name, "style", StringComparison.OrdinalIgnoreCase))
            {
                return StyleText(inside, at, relayed, '"');
            }

            return IsScriptBlock(attributes) ? HybridcastScripts.Rewritten(inside, inShiftJis) ?? inside : inside;
        }

        private void Placed(string name, bool closing)
        {
            if (!closing && !IsFrame(name))
            {
                beforeFirstShown ??= Written.Length;
            }
        }

        private void Opening(string name, bool closing)
        {
            if (closing || beforeFirstShown is not null)
            {
                return;
            }

            if (string.Equals(name, "head", StringComparison.OrdinalIgnoreCase))
            {
                afterHead ??= Written.Length;
            }
            else if (string.Equals(name, "html", StringComparison.OrdinalIgnoreCase))
            {
                afterHtml ??= Written.Length;
            }
        }

        private static bool RunsOn(Match found, string attributes)
        {
            int after = found.Index + found.Length;

            return found.Groups["value"].Value[0] is not ('"' or '\'')
                   && after < attributes.Length
                   && !char.IsWhiteSpace(attributes[after])
                   && attributes[after] != '/';
        }

        private static bool IsBroadcastObjectType(string element, string name, string value)
            => string.Equals(element, "object", StringComparison.OrdinalIgnoreCase)
               && string.Equals(name, "type", StringComparison.OrdinalIgnoreCase)
               && BroadcastObjectTypes.Contains(value.Trim());

        private static bool IsHandler(string name)
            => name.Length > 2 && name.StartsWith("on", StringComparison.OrdinalIgnoreCase) && name.All(char.IsAsciiLetter);

        private static bool IsFrame(string name)
            => string.Equals(name, "head", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "html", StringComparison.OrdinalIgnoreCase);

        private int Copied(int open, int found, int closer)
        {
            int end = found < 0 ? text.Length : found + closer;
            Written.Append(text, open, end - open);

            return end;
        }

        private void Shown(int from, int to)
        {
            if (to > from && !string.IsNullOrWhiteSpace(text[from..to]))
            {
                beforeFirstShown ??= Written.Length;
            }

            Written.Append(text, from, to - from);
        }

        private void Encoded(string name, bool closing, string attributes)
        {
            if (closing || declared is not null || !string.Equals(name, "meta", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (Match found in Attribute().Matches(attributes))
            {
                inShiftJis = CharsetNamedBy(found) is { } charset ? IsShiftJisName(charset) : inShiftJis;
            }
        }

        private static string? CharsetNamedBy(Match found)
        {
            string attribute = found.Groups["name"].Value;
            string value = Unquoted(found.Groups["value"].Value);

            if (string.Equals(attribute, "charset", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            return string.Equals(attribute, "content", StringComparison.OrdinalIgnoreCase) ? CharsetOf(value) : null;
        }

        private string Attributes(string element, string attributes)
            => Substituted(Attribute(), attributes, found => Rewritten(element, found, attributes) ?? found.Value);

        private string? Rewritten(string element, Match found, string attributes)
        {
            string name = found.Groups["name"].Value;
            string written = found.Groups["value"].Value;
            char quote = written[0] is '"' or '\'' ? written[0] : '"';
            string value = Unquoted(written);

            if (IsBroadcastObjectType(element, name, value))
            {
                return $"{found.Groups["lead"].Value}{BroadcastObjectType}{found.Groups["equals"].Value}{written}";
            }

            if (IsHandler(name))
            {
                return RunsOn(found, attributes) || Handler(value, inShiftJis) is not { } handled
                    ? null
                    : $"{found.Groups["lead"].Value}{name}{found.Groups["equals"].Value}{quote}{handled}{quote}";
            }

            if (string.Equals(name, "style", StringComparison.OrdinalIgnoreCase))
            {
                return $"{found.Groups["lead"].Value}{name}{found.Groups["equals"].Value}{quote}{StyleText(value, at, relayed, quote == '"' ? '\'' : '"')}{quote}";
            }

            if (!AddressAttributes.Contains(name) || Pointed(value, at, relayed) is not { } pointed)
            {
                return null;
            }

            return $"{found.Groups["lead"].Value}{name}{found.Groups["equals"].Value}\"{pointed}\"";
        }
    }
}
