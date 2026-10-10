using System.IO.Compression;
using System.Text;

namespace Carina.BroadcastTestSupport;

public static class EntityWriter
{
    public const string BmlType = "text/X-arib-bml; charset=\"euc-jp\"";

    public const string PngType = "image/X-arib-png";

    public static byte[] Multipart(string boundary, params EntityPart[] parts)
        => [.. Ascii($"Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n\r\n"), .. Parts(boundary, parts)];

    public static byte[] Parts(string boundary, params EntityPart[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var entity = new List<byte>();

        foreach (EntityPart part in parts)
        {
            entity.AddRange(Ascii($"--{boundary}\r\nContent-Location: {part.Location}\r\nContent-Type: {part.ContentType}\r\n\r\n"));
            entity.AddRange(part.Body);
            entity.AddRange(part.EndsOnTheBlankLine ? [] : Ascii("\r\n"));
        }

        entity.AddRange(Ascii($"--{boundary}--\r\n"));

        return entity.ToArray();
    }

    public static byte[] Zlib(byte[] data)
    {
        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return compressed.ToArray();
    }

    public static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
}

public sealed record EntityPart(string Location, string ContentType, byte[] Body)
{
    public bool EndsOnTheBlankLine { get; init; }
}
