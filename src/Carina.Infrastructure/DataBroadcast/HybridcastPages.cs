using System.Collections.Frozen;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// Turns a Hybridcast application's document and style sheets into ones that are read through the relay. Every
/// address the markup and the style sheets name — from the root, relative or whole — is resolved against where the
/// document was taken from and pointed at the relay, so it reaches the same server it would have reached had the
/// application been opened there. A script's own text is left alone; what it asks for at run time is the receiver
/// script's to point. The receiver script is put into the head once, as its first element, and where there is no
/// head, before the first thing the document shows. The bytes are read one to one as Latin-1, so a document in any
/// character set built on ASCII passes through unchanged except where an address is rewritten.
/// </summary>
public static partial class HybridcastPages
{
    private const string Bom = "ï»¿";

    private static readonly FrozenSet<string> AddressAttributes = new[]
    {
        "src", "href", "action", "formaction", "poster", "data", "background",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> RawText = new[] { "script", "style" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> DocumentTypes = new[] { "text/html", "application/xhtml+xml" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsDocument(string? contentType) => MediaTypeOf(contentType) is { } type && DocumentTypes.Contains(type);

    public static bool IsStyle(string? contentType) => string.Equals(MediaTypeOf(contentType), "text/css", StringComparison.OrdinalIgnoreCase);

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

    public static byte[] Document(ReadOnlySpan<byte> document, Uri at, Func<Uri, string> relayed, string injected)
    {
        ArgumentNullException.ThrowIfNull(at);
        ArgumentNullException.ThrowIfNull(relayed);
        ArgumentNullException.ThrowIfNull(injected);

        string text = Encoding.Latin1.GetString(document);
        Markup markup = new(text, at, relayed);
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

    [GeneratedRegex("""url\(\s*(?<quote>["']?)(?<value>[^"')]*)\k<quote>\s*\)""", RegexOptions.IgnoreCase)]
    private static partial Regex StyleUrl();

    [GeneratedRegex("""@import\s+(?<quote>["'])(?<value>[^"']*)\k<quote>""", RegexOptions.IgnoreCase)]
    private static partial Regex StyleImport();

    private sealed class Markup(string text, Uri at, Func<Uri, string> relayed)
    {
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
            Written.Append('<').Append(tag.Groups["closing"].Value).Append(name).Append(Attributes(tag.Groups["attributes"].Value)).Append('>');
            Opening(name, closing);

            if (closing || !RawText.Contains(name))
            {
                return after;
            }

            int end = text.IndexOf("</" + name, after, StringComparison.OrdinalIgnoreCase);
            int stops = end < 0 ? text.Length : end;
            string inside = text[after..stops];

            Written.Append(string.Equals(name, "style", StringComparison.OrdinalIgnoreCase) ? StyleText(inside, at, relayed, '"') : inside);

            return stops;
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

        private string Attributes(string attributes)
            => Substituted(Attribute(), attributes, found => Rewritten(found) ?? found.Value);

        private string? Rewritten(Match found)
        {
            string name = found.Groups["name"].Value;
            string written = found.Groups["value"].Value;
            char quote = written[0] is '"' or '\'' ? written[0] : '"';
            string value = written[0] is '"' or '\'' ? written[1..^1] : written;

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
