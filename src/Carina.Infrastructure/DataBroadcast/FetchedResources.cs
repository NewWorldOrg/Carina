using System.Collections.Frozen;
using System.Net.Http.Headers;

using Carina.Broadcast.DsmCc;
using Carina.Domain.DataBroadcast;

namespace Carina.Infrastructure.DataBroadcast;

public static class FetchedResources
{
    private const string Bml = "text/X-arib-bml";

    private const string Css = "text/css";

    private const string EcmaScript = "text/X-arib-ecmascript";

    private const string Jpeg = "image/jpeg";

    private const string Png = "image/X-arib-png";

    private static readonly FrozenSet<string> Carried = new[]
    {
        Bml, Css, EcmaScript, "application/X-arib-ecmascript", Jpeg, Png, "image/png",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> Texts = new[]
    {
        Bml, Css, EcmaScript, "application/X-arib-ecmascript",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> ByExtension = new Dictionary<string, string>
    {
        [".bml"] = Bml,
        [".css"] = Css,
        [".ecm"] = EcmaScript,
        [".js"] = EcmaScript,
        [".jpg"] = Jpeg,
        [".jpeg"] = Jpeg,
        [".png"] = Png,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> EucJpNames =
        new[] { "euc-jp", "x-euc-jp" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static CarouselResource Of(Uri at, MediaTypeHeaderValue? type, byte[] body)
    {
        ArgumentNullException.ThrowIfNull(at);
        ArgumentNullException.ThrowIfNull(body);

        string media = MediaTypeOf(at, type?.MediaType);

        if (!Texts.Contains(media))
        {
            return new CarouselResource(at.AbsoluteUri, media, ResourceForm.Binary, body);
        }

        string? charset = type?.CharSet?.Trim('"');

        if (charset is null || EucJpNames.Contains(charset))
        {
            return new CarouselResource(at.AbsoluteUri, media, ResourceForm.Text, EucJpText.ToUtf8(body));
        }

        ResourceForm form = string.Equals(charset, "utf-8", StringComparison.OrdinalIgnoreCase)
            ? ResourceForm.Text
            : ResourceForm.UndecodedText;

        return new CarouselResource(at.AbsoluteUri, media, form, body);
    }

    private static string MediaTypeOf(Uri at, string? said)
    {
        if (said is not null && Carried.Contains(said))
        {
            return said;
        }

        return ByExtension.TryGetValue(Path.GetExtension(at.AbsolutePath), out string? guessed)
            ? guessed
            : CarouselReader.UnnamedMediaType;
    }
}
