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

    private ModuleResource(
        string location,
        string mediaType,
        ResourceContent content,
        string? charset,
        ReadOnlyMemory<byte> body,
        int substitutions)
    {
        Location = location;
        MediaType = mediaType;
        Content = content;
        Charset = charset;
        Body = body;
        Substitutions = substitutions;
    }

    public string Location { get; }

    public string MediaType { get; }

    public ResourceContent Content { get; }

    public string? Charset { get; }

    public ReadOnlyMemory<byte> Body { get; }

    public int Substitutions { get; }

    internal static ModuleResource Of(string location, string? contentType, ReadOnlyMemory<byte> body)
    {
        MediaType media = DsmCc.MediaType.Parse(contentType ?? string.Empty);

        if (!TextTypes.Contains(media.Type))
        {
            return new ModuleResource(location, media.Type, ResourceContent.Binary, media.Charset, body, 0);
        }

        if (media.Charset is null || EucJpNames.Contains(media.Charset))
        {
            byte[] decoded = EucJpText.ToUtf8(body.Span, out int substitutions);

            return new ModuleResource(location, media.Type, ResourceContent.Text, media.Charset, decoded, substitutions);
        }

        ResourceContent content = string.Equals(media.Charset, "utf-8", StringComparison.OrdinalIgnoreCase)
            ? ResourceContent.Text
            : ResourceContent.UndecodedText;

        return new ModuleResource(location, media.Type, content, media.Charset, body, 0);
    }
}
