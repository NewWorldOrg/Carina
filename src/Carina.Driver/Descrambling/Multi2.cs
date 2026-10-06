using System.Buffers.Binary;
using System.Numerics;

namespace Carina.Driver.Descrambling;

/// <summary>
/// The MULTI2 block cipher of ARIB STD-B25, holding the eight work-key words derived from one system key and one data key.
/// </summary>
public sealed class Multi2
{
    public const int SystemKeyLength = 32;

    public const int DataKeyLength = 8;

    public const int BlockLength = 8;

    public const int BroadcastCycles = 4;

    private readonly uint k1;
    private readonly uint k2;
    private readonly uint k3;
    private readonly uint k4;
    private readonly uint k5;
    private readonly uint k6;
    private readonly uint k7;
    private readonly uint k8;

    private readonly int cycles;

    private Multi2(ReadOnlySpan<uint> work, int cycles)
    {
        k1 = work[0];
        k2 = work[1];
        k3 = work[2];
        k4 = work[3];
        k5 = work[4];
        k6 = work[5];
        k7 = work[6];
        k8 = work[7];
        this.cycles = cycles;
    }

    /// <summary>
    /// Derives the work key from a 32-byte system key and an 8-byte data key; one cycle is the eight-step sequence of the four round functions.
    /// </summary>
    public static Multi2 Keyed(
        ReadOnlySpan<byte> systemKey,
        ReadOnlySpan<byte> dataKey,
        int cycles = BroadcastCycles
    )
    {
        if (systemKey.Length is not SystemKeyLength)
        {
            throw new ArgumentException(
                $"A system key is {SystemKeyLength} bytes, not {systemKey.Length}.",
                nameof(systemKey)
            );
        }

        if (dataKey.Length is not DataKeyLength)
        {
            throw new ArgumentException(
                $"A data key is {DataKeyLength} bytes, not {dataKey.Length}.",
                nameof(dataKey)
            );
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(cycles, 1);

        Span<uint> system = stackalloc uint[8];
        for (int word = 0; word < system.Length; word++)
        {
            system[word] = BinaryPrimitives.ReadUInt32BigEndian(systemKey[(word * 4)..]);
        }

        uint left = BinaryPrimitives.ReadUInt32BigEndian(dataKey);
        uint right = BinaryPrimitives.ReadUInt32BigEndian(dataKey[4..]);

        Span<uint> work = stackalloc uint[8];

        Pi1(left, ref right);
        Pi2(ref left, right, system[0]);
        work[0] = left;
        Pi3(left, ref right, system[1], system[2]);
        work[1] = right;
        Pi4(ref left, right, system[3]);
        work[2] = left;
        Pi1(left, ref right);
        work[3] = right;
        Pi2(ref left, right, system[4]);
        work[4] = left;
        Pi3(left, ref right, system[5], system[6]);
        work[5] = right;
        Pi4(ref left, right, system[7]);
        work[6] = left;
        Pi1(left, ref right);
        work[7] = right;

        return new Multi2(work, cycles);
    }

    public ulong Encrypt(ulong block)
    {
        uint left = (uint)(block >> 32);
        uint right = (uint)block;

        for (int cycle = 0; cycle < cycles; cycle++)
        {
            Pi1(left, ref right);
            Pi2(ref left, right, k1);
            Pi3(left, ref right, k2, k3);
            Pi4(ref left, right, k4);
            Pi1(left, ref right);
            Pi2(ref left, right, k5);
            Pi3(left, ref right, k6, k7);
            Pi4(ref left, right, k8);
        }

        return ((ulong)left << 32) | right;
    }

    public ulong Decrypt(ulong block)
    {
        uint left = (uint)(block >> 32);
        uint right = (uint)block;

        for (int cycle = 0; cycle < cycles; cycle++)
        {
            Pi4(ref left, right, k8);
            Pi3(left, ref right, k6, k7);
            Pi2(ref left, right, k5);
            Pi1(left, ref right);
            Pi4(ref left, right, k4);
            Pi3(left, ref right, k2, k3);
            Pi2(ref left, right, k1);
            Pi1(left, ref right);
        }

        return ((ulong)left << 32) | right;
    }

    /// <summary>
    /// Scrambles a payload in place the way a broadcast does: whole blocks chained from the initial value, and a short tail masked with the encrypted last block.
    /// </summary>
    public void EncryptPayload(Span<byte> payload, ulong initialValue)
    {
        ulong chain = initialValue;
        int whole = payload.Length - (payload.Length % BlockLength);

        for (int offset = 0; offset < whole; offset += BlockLength)
        {
            Span<byte> block = payload.Slice(offset, BlockLength);
            chain = Encrypt(BinaryPrimitives.ReadUInt64BigEndian(block) ^ chain);
            BinaryPrimitives.WriteUInt64BigEndian(block, chain);
        }

        MaskTail(payload[whole..], chain);
    }

    /// <summary>
    /// Unscrambles a payload in place: the inverse of <see cref="EncryptPayload"/>.
    /// </summary>
    public void DecryptPayload(Span<byte> payload, ulong initialValue)
    {
        ulong chain = initialValue;
        int whole = payload.Length - (payload.Length % BlockLength);

        for (int offset = 0; offset < whole; offset += BlockLength)
        {
            Span<byte> block = payload.Slice(offset, BlockLength);
            ulong scrambled = BinaryPrimitives.ReadUInt64BigEndian(block);
            BinaryPrimitives.WriteUInt64BigEndian(block, Decrypt(scrambled) ^ chain);
            chain = scrambled;
        }

        MaskTail(payload[whole..], chain);
    }

    private void MaskTail(Span<byte> tail, ulong chain)
    {
        if (tail.IsEmpty)
        {
            return;
        }

        Span<byte> mask = stackalloc byte[BlockLength];
        BinaryPrimitives.WriteUInt64BigEndian(mask, Encrypt(chain));

        for (int index = 0; index < tail.Length; index++)
        {
            tail[index] ^= mask[index];
        }
    }

    private static void Pi1(uint left, ref uint right) => right ^= left;

    private static void Pi2(ref uint left, uint right, uint key)
    {
        uint x = right + key;
        uint y = BitOperations.RotateLeft(x, 1) + x - 1;
        left ^= BitOperations.RotateLeft(y, 4) ^ y;
    }

    private static void Pi3(uint left, ref uint right, uint first, uint second)
    {
        uint x = left + first;
        uint y = BitOperations.RotateLeft(x, 2) + x + 1;
        uint z = (BitOperations.RotateLeft(y, 8) ^ y) + second;
        uint w = BitOperations.RotateLeft(z, 1) - z;
        right ^= BitOperations.RotateLeft(w, 16) ^ (w | left);
    }

    private static void Pi4(ref uint left, uint right, uint key)
    {
        uint x = right + key;
        left ^= BitOperations.RotateLeft(x, 2) + x + 1;
    }
}
