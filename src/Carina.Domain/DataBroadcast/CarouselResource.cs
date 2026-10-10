using System.Text;

namespace Carina.Domain.DataBroadcast;

/// <summary>
/// One resource of a module: where the documents find it, its media type, how its bytes are read, and
/// the bytes.
/// </summary>
public sealed record CarouselResource
{
    public const int FramingBytes = sizeof(ushort) + sizeof(byte) + sizeof(uint);

    public CarouselResource(string path, string mediaType, ResourceForm form, ReadOnlyMemory<byte> body)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(mediaType);

        if (!Enum.IsDefined(form))
        {
            throw new ArgumentOutOfRangeException(nameof(form), form, "A resource is binary, text, or text left undecoded.");
        }

        Path = path;
        MediaType = mediaType;
        Form = form;
        Body = body;
    }

    public string Path { get; }

    public string MediaType { get; }

    public ResourceForm Form { get; }

    public ReadOnlyMemory<byte> Body { get; }

    public long Bytes => FramingBytes + Encoding.UTF8.GetByteCount(Path) + (long)Body.Length;
}
