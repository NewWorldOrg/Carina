using System.Buffers.Binary;

namespace Carina.Driver.Descrambling;

/// <summary>
/// Gathers the sections one PID carries from the payloads of its packets, and hands on each whole one whose CRC holds.
/// </summary>
public sealed class SectionCollector
{
    private const int HeaderLength = 3;

    private const int LongestSection = 4096;

    private const byte Stuffing = 0xFF;

    private readonly byte[] gathered = new byte[LongestSection];

    private int length;

    private bool collecting;

    private int lastCounter = -1;

    public void Take(ReadOnlySpan<byte> payload, bool unitStart, int counter, Action<ReadOnlySpan<byte>> whole)
    {
        ArgumentNullException.ThrowIfNull(whole);

        if (counter == lastCounter)
        {
            return;
        }

        if (lastCounter >= 0 && counter != ((lastCounter + 1) & 15))
        {
            collecting = false;
        }

        lastCounter = counter;

        if (!unitStart)
        {
            Continue(payload, whole);

            return;
        }

        if (payload.IsEmpty)
        {
            collecting = false;

            return;
        }

        int pointer = payload[0];
        if (1 + pointer > payload.Length)
        {
            collecting = false;

            return;
        }

        Continue(payload.Slice(1, pointer), whole);
        StartFrom(payload[(1 + pointer)..], whole);
    }

    private void Continue(ReadOnlySpan<byte> bytes, Action<ReadOnlySpan<byte>> whole)
    {
        if (!collecting)
        {
            return;
        }

        Append(bytes);
        Finish(whole);
    }

    private void StartFrom(ReadOnlySpan<byte> bytes, Action<ReadOnlySpan<byte>> whole)
    {
        ReadOnlySpan<byte> rest = bytes;

        while (!rest.IsEmpty && rest[0] is not Stuffing)
        {
            collecting = true;
            length = 0;

            int taken = Append(rest);
            rest = rest[taken..];

            if (!Finish(whole))
            {
                return;
            }
        }
    }

    private int Append(ReadOnlySpan<byte> bytes)
    {
        int taken = 0;
        int room = Expected() - length;

        while (taken < bytes.Length && room > 0)
        {
            int step = Math.Min(room, bytes.Length - taken);
            bytes.Slice(taken, step).CopyTo(gathered.AsSpan(length));
            length += step;
            taken += step;
            room = Expected() - length;
        }

        return taken;
    }

    private int Expected()
    {
        if (length < HeaderLength)
        {
            return HeaderLength;
        }

        int sectionLength = BinaryPrimitives.ReadUInt16BigEndian(gathered.AsSpan(1)) & 0x0FFF;

        return Math.Min(HeaderLength + sectionLength, LongestSection);
    }

    private bool Finish(Action<ReadOnlySpan<byte>> whole)
    {
        if (!collecting || length < HeaderLength || length < Expected())
        {
            return false;
        }

        collecting = false;
        ReadOnlySpan<byte> section = gathered.AsSpan(0, length);

        if (PsiSection.Holds(section))
        {
            whole(section);
        }

        return true;
    }
}

/// <summary>
/// The long-form sections of ISO/IEC 13818-1: an eight-byte header, a body, and a CRC over all of it.
/// </summary>
public static class PsiSection
{
    public const int HeaderLength = 8;

    public const int CrcLength = 4;

    private static readonly uint[] Table = BuildTable();

    /// <summary>
    /// Whether a section has the syntax indicator set, room for its header and CRC, and a CRC that holds.
    /// </summary>
    public static bool Holds(ReadOnlySpan<byte> section) =>
        section.Length >= HeaderLength + CrcLength && (section[1] & 0x80) is not 0 && Crc(section) is 0;

    public static bool IsCurrent(ReadOnlySpan<byte> section) => (section[5] & 0x01) is not 0;

    public static int Version(ReadOnlySpan<byte> section) => (section[5] >> 1) & 0x1F;

    public static int Extension(ReadOnlySpan<byte> section) => BinaryPrimitives.ReadUInt16BigEndian(section[3..]);

    public static ReadOnlySpan<byte> Body(ReadOnlySpan<byte> section) =>
        section[HeaderLength..^CrcLength];

    public static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in bytes)
        {
            crc = (crc << 8) ^ Table[(crc >> 24) ^ value];
        }

        return crc;
    }

    private static uint[] BuildTable()
    {
        uint[] table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index << 24;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 0x80000000) is not 0 ? (value << 1) ^ 0x04C11DB7 : value << 1;
            }

            table[index] = value;
        }

        return table;
    }
}
