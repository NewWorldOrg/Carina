namespace Carina.Broadcast.DsmCc;

public sealed class ModuleResource
{
    private static readonly HashSet<string> TextTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/X-arib-bml",
        "text/css",
        "text/X-arib-ecmascript",
        "application/X-arib-ecmascript",
    };

    private static readonly HashSet<string> EucJpNames = new(StringComparer.OrdinalIgnoreCase) { "euc-jp", "x-euc-jp" };

    private ModuleResource(string location, string mediaType, bool isText, ReadOnlyMemory<byte> body, int substitutions = 0)
    {
        Location = location;
        MediaType = mediaType;
        IsText = isText;
        Body = body;
        Substitutions = substitutions;
    }

    public string Location { get; }

    public string MediaType { get; }

    public bool IsText { get; }

    public ReadOnlyMemory<byte> Body { get; }

    public int Substitutions { get; }

    internal static ModuleResource Of(string location, string? contentType, ReadOnlyMemory<byte> body)
    {
        MediaType media = DsmCc.MediaType.Parse(contentType ?? string.Empty);

        if (!TextTypes.Contains(media.Type))
        {
            return new ModuleResource(location, media.Type, false, body);
        }

        if (media.Charset is null || EucJpNames.Contains(media.Charset))
        {
            byte[] decoded = EucJpText.ToUtf8(body.Span, out int substitutions);

            return new ModuleResource(location, media.Type, true, decoded, substitutions);
        }

        bool utf8 = string.Equals(media.Charset, "utf-8", StringComparison.OrdinalIgnoreCase);

        return new ModuleResource(location, media.Type, utf8, body);
    }
}
