using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace Carina.Domain.Segments;

/// <summary>
/// The bytes one kind of the learning data of one chunk is kept in: the shape's
/// <see cref="Version"/>, the kind and the chunk's index; for the frame lights alone the frame
/// clock (numerator, denominator, first frame in ticks); then how many readings follow, how many
/// bytes they take, and the readings compressed with Brotli. A reading of two values is kept as all
/// the first values, then all the second. Numbers are little-endian. Bytes in any other shape, or of
/// another kind than the one asked for, are not read.
/// </summary>
public static class LearningDataFormat
{
    public const ushort Version = 1;

    private const int HeaderBytes = sizeof(ushort) + 1 + sizeof(int);

    private const int ClockBytes = (2 * sizeof(int)) + sizeof(long);

    private const int ReadingsHeaderBytes = 2 * sizeof(int);

    private const int Quality = 9;

    private const int WindowBits = 22;

    public static byte[] Write(LearningDataChunk chunk, LearningDataKind kind) => Write(LearningDataPart.Of(chunk, kind));

    public static byte[] Write(LearningDataPart part)
    {
        ArgumentNullException.ThrowIfNull(part);

        ArrayBufferWriter<byte> written = new();
        Span<byte> header = written.GetSpan(HeaderBytes);

        BinaryPrimitives.WriteUInt16LittleEndian(header, Version);
        header[2] = (byte)part.Kind;
        BinaryPrimitives.WriteInt32LittleEndian(header[3..], part.Index);
        written.Advance(HeaderBytes);

        if (part.Clock is FrameClock clock)
        {
            Span<byte> clocked = written.GetSpan(ClockBytes);
            BinaryPrimitives.WriteInt32LittleEndian(clocked, clock.Numerator);
            BinaryPrimitives.WriteInt32LittleEndian(clocked[4..], clock.Denominator);
            BinaryPrimitives.WriteInt64LittleEndian(clocked[8..], clock.FirstFrameAt.Ticks);
            written.Advance(ClockBytes);
        }

        WriteReadings(written, part.Count, Readings(part));

        return written.WrittenSpan.ToArray();
    }

    public static bool TryRead(ReadOnlySpan<byte> bytes, LearningDataKind kind, [NotNullWhen(true)] out LearningDataPart? part)
    {
        part = null;

        if (!Enum.IsDefined(kind)
            || bytes.Length < HeaderBytes
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes) != Version
            || bytes[2] != (byte)kind)
        {
            return false;
        }

        int index = BinaryPrimitives.ReadInt32LittleEndian(bytes[3..]);
        ReadOnlySpan<byte> rest = bytes[HeaderBytes..];
        FrameClock? clock = null;

        if (index is < 0 or > LearningData.LastChunk
            || (kind is LearningDataKind.FrameLights && !TryReadClock(ref rest, index, out clock)))
        {
            return false;
        }

        if (!TryReadReadings(ref rest, Most(kind, clock), BytesEach(kind), out byte[] readings)
            || !rest.IsEmpty
            || (kind is LearningDataKind.CaptionPresence && LearningDataPart.CaptionsFault(index, readings) is not null))
        {
            return false;
        }

        part = LearningDataPart.Read(kind, index, clock, readings);

        return true;
    }

    internal static uint[] Fingerprints(byte[] bytes)
    {
        uint[] fingerprints = new uint[bytes.Length / sizeof(uint)];

        for (int at = 0; at < fingerprints.Length; at++)
        {
            fingerprints[at] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at * sizeof(uint)));
        }

        return fingerprints;
    }

    internal static ChannelDifference[] Channels(byte[] bytes)
    {
        int count = bytes.Length / 2;
        ChannelDifference[] channels = new ChannelDifference[count];

        for (int at = 0; at < count; at++)
        {
            channels[at] = new ChannelDifference(bytes[at], bytes[count + at]);
        }

        return channels;
    }

    internal static FrameLight[] Frames(byte[] bytes)
    {
        int count = bytes.Length / 2;
        FrameLight[] frames = new FrameLight[count];

        for (int at = 0; at < count; at++)
        {
            frames[at] = new FrameLight(bytes[at], bytes[count + at]);
        }

        return frames;
    }

    private static byte[] Readings(LearningDataPart part) => part.Kind switch
    {
        LearningDataKind.SoundFingerprints => FingerprintBytes(part.Fingerprints),
        LearningDataKind.Loudness => part.Loudness.ToArray(),
        LearningDataKind.ChannelDifferences => ChannelBytes(part.Channels),
        LearningDataKind.FrameLights => FrameBytes(part.Frames),
        LearningDataKind.CornerOutlines => part.CornerOutlines.ToArray(),
        _ => part.Captions.ToArray(),
    };

    private static int Most(LearningDataKind kind, FrameClock? clock) => kind switch
    {
        LearningDataKind.SoundFingerprints => SoundFingerprint.PerChunk,
        LearningDataKind.Loudness => SoundLoudness.PerChunk,
        LearningDataKind.ChannelDifferences => ChannelDifference.PerChunk,
        LearningDataKind.FrameLights => clock?.MostFramesIn(LearningData.ChunkSeconds) ?? 0,
        LearningDataKind.CornerOutlines => CornerOutline.PerChunk,
        _ => CaptionPresence.PerChunk,
    };

    private static int BytesEach(LearningDataKind kind) => kind switch
    {
        LearningDataKind.SoundFingerprints => sizeof(uint),
        LearningDataKind.Loudness or LearningDataKind.CaptionPresence => 1,
        LearningDataKind.ChannelDifferences or LearningDataKind.FrameLights => 2,
        _ => CornerOutline.Bytes,
    };

    private static void WriteReadings(ArrayBufferWriter<byte> written, int count, ReadOnlySpan<byte> readings)
    {
        byte[] compressed = new byte[BrotliEncoder.GetMaxCompressedLength(readings.Length)];

        if (!BrotliEncoder.TryCompress(readings, compressed, out int length, Quality, WindowBits))
        {
            throw new InvalidOperationException("The readings of a chunk could not be compressed.");
        }

        Span<byte> header = written.GetSpan(ReadingsHeaderBytes);
        BinaryPrimitives.WriteInt32LittleEndian(header, count);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], length);
        written.Advance(ReadingsHeaderBytes);
        written.Write(compressed.AsSpan(0, length));
    }

    private static bool TryReadClock(ref ReadOnlySpan<byte> rest, int index, [NotNullWhen(true)] out FrameClock? clock)
    {
        clock = null;

        if (rest.Length < ClockBytes)
        {
            return false;
        }

        int numerator = BinaryPrimitives.ReadInt32LittleEndian(rest);
        int denominator = BinaryPrimitives.ReadInt32LittleEndian(rest[4..]);
        TimeSpan firstFrameAt = TimeSpan.FromTicks(BinaryPrimitives.ReadInt64LittleEndian(rest[8..]));

        if (FrameClock.Fault(numerator, denominator, firstFrameAt) is not null || firstFrameAt < LearningData.ChunkStarts(index))
        {
            return false;
        }

        clock = FrameClock.Of(numerator, denominator, firstFrameAt);
        rest = rest[ClockBytes..];

        return true;
    }

    private static bool TryReadReadings(ref ReadOnlySpan<byte> rest, int most, int bytesEach, out byte[] readings)
    {
        readings = [];

        if (rest.Length < ReadingsHeaderBytes)
        {
            return false;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(rest);
        int length = BinaryPrimitives.ReadInt32LittleEndian(rest[4..]);

        if (count < 0 || count > most || length < 0 || length > rest.Length - ReadingsHeaderBytes)
        {
            return false;
        }

        byte[] expanded = new byte[count * bytesEach];

        if (!BrotliDecoder.TryDecompress(rest.Slice(ReadingsHeaderBytes, length), expanded, out int expandedLength)
            || expandedLength != expanded.Length)
        {
            return false;
        }

        rest = rest[(ReadingsHeaderBytes + length)..];
        readings = expanded;

        return true;
    }

    private static byte[] FingerprintBytes(ReadOnlySpan<uint> fingerprints)
    {
        byte[] bytes = new byte[fingerprints.Length * sizeof(uint)];

        for (int at = 0; at < fingerprints.Length; at++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at * sizeof(uint)), fingerprints[at]);
        }

        return bytes;
    }

    private static byte[] ChannelBytes(ReadOnlySpan<ChannelDifference> channels)
    {
        byte[] bytes = new byte[channels.Length * 2];

        for (int at = 0; at < channels.Length; at++)
        {
            bytes[at] = channels[at].Correlation;
            bytes[channels.Length + at] = channels[at].Difference;
        }

        return bytes;
    }

    private static byte[] FrameBytes(ReadOnlySpan<FrameLight> frames)
    {
        byte[] bytes = new byte[frames.Length * 2];

        for (int at = 0; at < frames.Length; at++)
        {
            bytes[at] = frames[at].Brightness;
            bytes[frames.Length + at] = frames[at].Change;
        }

        return bytes;
    }
}
