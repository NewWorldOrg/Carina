using System.Text;

namespace Carina.BroadcastTestSupport;

public static class ModuleDescriptorWriter
{
    public const int TypeTag = 0x01;

    public const int NameTag = 0x02;

    public const int InfoTag = 0x03;

    public const int CompressionTypeTag = 0xC2;

    public const int Zlib = 0x00;

    public static byte[] Type(string mediaType) => DescriptorWriter.Of(TypeTag, Encoding.ASCII.GetBytes(mediaType));

    public static byte[] Name(string name) => DescriptorWriter.Of(NameTag, Encoding.ASCII.GetBytes(name));

    public static byte[] Info(string language, byte[] text)
        => DescriptorWriter.Of(InfoTag, [.. Encoding.ASCII.GetBytes(language), .. text]);

    public static byte[] Compression(long originalSize, int compressionType = Zlib)
        => DescriptorWriter.Of(CompressionTypeTag, new ByteWriter().Byte(compressionType).DoubleWord(originalSize).ToArray());
}
