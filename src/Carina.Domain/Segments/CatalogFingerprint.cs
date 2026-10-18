using System.Buffers.Binary;

namespace Carina.Domain.Segments;

/// <summary>
/// The bytes a catalog entry's fingerprint is kept in: one little-endian 32-bit value per frame.
/// </summary>
public static class CatalogFingerprint
{
    public static byte[] Write(ReadOnlySpan<uint> frames)
    {
        byte[] bytes = new byte[frames.Length * sizeof(uint)];

        for (int at = 0; at < frames.Length; at++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at * sizeof(uint)), frames[at]);
        }

        return bytes;
    }

    public static uint[] Read(ReadOnlySpan<byte> bytes)
    {
        uint[] frames = new uint[bytes.Length / sizeof(uint)];

        for (int at = 0; at < frames.Length; at++)
        {
            frames[at] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(at * sizeof(uint))..]);
        }

        return frames;
    }
}
