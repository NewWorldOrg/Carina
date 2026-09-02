using System.Buffers.Binary;
using System.Text;

namespace Carina.TestSupport;

public static class Fmp4
{
    public static byte[] Header { get; } = Joined(Box("ftyp", 24), Box("moov", 300));

    public static byte[] Fragment(int mediaLength) => Joined(Box("moof", 100), Box("mdat", mediaLength));

    public static byte[] Box(string kind, int payloadLength)
    {
        ArgumentNullException.ThrowIfNull(kind);

        byte[] box = new byte[8 + payloadLength];

        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(kind).CopyTo(box, 4);
        Array.Fill(box, (byte)payloadLength, 8, payloadLength);

        return box;
    }

    public static byte[] Joined(params byte[][] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        return [.. parts.SelectMany(part => part)];
    }
}
