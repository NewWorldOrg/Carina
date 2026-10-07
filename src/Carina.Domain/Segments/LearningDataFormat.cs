using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace Carina.Domain.Segments;

/// <summary>
/// The bytes a <see cref="LearningDataChunk"/> is kept in: a header of the shape's
/// <see cref="Version"/>, the chunk's index and its frame clock (numerator, denominator, first
/// frame in ticks), then one section for each kind of data in a fixed order — fingerprints,
/// loudness, channel differences, frame lights, corner outlines — each naming its kind, how many
/// readings it holds and how many bytes follow, the readings compressed with Brotli. A reading of
/// two values is kept as all the first values, then all the second. Numbers are little-endian.
/// Bytes in any other shape are not read.
/// </summary>
public static class LearningDataFormat
{
    public const ushort Version = 1;

    private const int HeaderBytes = sizeof(ushort) + (3 * sizeof(int)) + sizeof(long);

    private const int SectionHeaderBytes = 1 + (2 * sizeof(int));

    private const int Quality = 9;

    private const int WindowBits = 22;

    private enum Kind : byte
    {
        Fingerprints = 1,
        Loudness = 2,
        Channels = 3,
        Frames = 4,
        CornerOutlines = 5,
    }

    public static byte[] Write(LearningDataChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        ArrayBufferWriter<byte> written = new();
        Span<byte> header = written.GetSpan(HeaderBytes);

        BinaryPrimitives.WriteUInt16LittleEndian(header, Version);
        BinaryPrimitives.WriteInt32LittleEndian(header[2..], chunk.Index);
        BinaryPrimitives.WriteInt32LittleEndian(header[6..], chunk.Clock.Numerator);
        BinaryPrimitives.WriteInt32LittleEndian(header[10..], chunk.Clock.Denominator);
        BinaryPrimitives.WriteInt64LittleEndian(header[14..], chunk.Clock.FirstFrameAt.Ticks);
        written.Advance(HeaderBytes);

        Section(written, Kind.Fingerprints, chunk.Fingerprints.Length, FingerprintBytes(chunk.Fingerprints));
        Section(written, Kind.Loudness, chunk.Loudness.Length, chunk.Loudness);
        Section(written, Kind.Channels, chunk.Channels.Length, ChannelBytes(chunk.Channels));
        Section(written, Kind.Frames, chunk.Frames.Length, FrameBytes(chunk.Frames));
        Section(written, Kind.CornerOutlines, chunk.Outlines, chunk.CornerOutlines);

        return written.WrittenSpan.ToArray();
    }

    public static bool TryRead(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out LearningDataChunk? chunk)
    {
        chunk = null;

        if (bytes.Length < HeaderBytes || BinaryPrimitives.ReadUInt16LittleEndian(bytes) != Version)
        {
            return false;
        }

        int index = BinaryPrimitives.ReadInt32LittleEndian(bytes[2..]);
        int numerator = BinaryPrimitives.ReadInt32LittleEndian(bytes[6..]);
        int denominator = BinaryPrimitives.ReadInt32LittleEndian(bytes[10..]);
        long firstFrameTicks = BinaryPrimitives.ReadInt64LittleEndian(bytes[14..]);

        if (FrameClock.Fault(numerator, denominator, TimeSpan.FromTicks(firstFrameTicks)) is not null)
        {
            return false;
        }

        FrameClock clock = FrameClock.Of(numerator, denominator, TimeSpan.FromTicks(firstFrameTicks));
        ReadOnlySpan<byte> rest = bytes[HeaderBytes..];

        if (!TrySection(ref rest, Kind.Fingerprints, SoundFingerprint.PerChunk, sizeof(uint), out byte[] fingerprints)
            || !TrySection(ref rest, Kind.Loudness, SoundLoudness.PerChunk, 1, out byte[] loudness)
            || !TrySection(ref rest, Kind.Channels, ChannelDifference.PerChunk, 2, out byte[] channels)
            || !TrySection(ref rest, Kind.Frames, clock.MostFramesIn(LearningData.ChunkSeconds), 2, out byte[] frames)
            || !TrySection(ref rest, Kind.CornerOutlines, CornerOutline.PerChunk, CornerOutline.Bytes, out byte[] outlines)
            || !rest.IsEmpty)
        {
            return false;
        }

        string? fault = LearningDataChunk.Fault(
            index,
            clock,
            fingerprints.Length / sizeof(uint),
            loudness.Length,
            channels.Length / 2,
            frames.Length / 2,
            outlines.Length);

        if (fault is not null)
        {
            return false;
        }

        chunk = LearningDataChunk.Of(
            index,
            clock,
            Fingerprints(fingerprints),
            loudness,
            Channels(channels),
            Frames(frames),
            outlines);

        return true;
    }

    private static void Section(ArrayBufferWriter<byte> written, Kind kind, int count, ReadOnlySpan<byte> readings)
    {
        byte[] compressed = new byte[BrotliEncoder.GetMaxCompressedLength(readings.Length)];

        if (!BrotliEncoder.TryCompress(readings, compressed, out int length, Quality, WindowBits))
        {
            throw new InvalidOperationException($"The {kind} of a chunk could not be compressed.");
        }

        Span<byte> header = written.GetSpan(SectionHeaderBytes);
        header[0] = (byte)kind;
        BinaryPrimitives.WriteInt32LittleEndian(header[1..], count);
        BinaryPrimitives.WriteInt32LittleEndian(header[5..], length);
        written.Advance(SectionHeaderBytes);
        written.Write(compressed.AsSpan(0, length));
    }

    private static bool TrySection(ref ReadOnlySpan<byte> rest, Kind kind, int most, int bytesEach, out byte[] readings)
    {
        readings = [];

        if (rest.Length < SectionHeaderBytes || rest[0] != (byte)kind)
        {
            return false;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(rest[1..]);
        int length = BinaryPrimitives.ReadInt32LittleEndian(rest[5..]);

        if (count < 0 || count > most || length < 0 || length > rest.Length - SectionHeaderBytes)
        {
            return false;
        }

        byte[] expanded = new byte[count * bytesEach];

        if (!BrotliDecoder.TryDecompress(rest.Slice(SectionHeaderBytes, length), expanded, out int expandedLength)
            || expandedLength != expanded.Length)
        {
            return false;
        }

        rest = rest[(SectionHeaderBytes + length)..];
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

    private static uint[] Fingerprints(byte[] bytes)
    {
        uint[] fingerprints = new uint[bytes.Length / sizeof(uint)];

        for (int at = 0; at < fingerprints.Length; at++)
        {
            fingerprints[at] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at * sizeof(uint)));
        }

        return fingerprints;
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

    private static ChannelDifference[] Channels(byte[] bytes)
    {
        int count = bytes.Length / 2;
        ChannelDifference[] channels = new ChannelDifference[count];

        for (int at = 0; at < count; at++)
        {
            channels[at] = new ChannelDifference(bytes[at], bytes[count + at]);
        }

        return channels;
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

    private static FrameLight[] Frames(byte[] bytes)
    {
        int count = bytes.Length / 2;
        FrameLight[] frames = new FrameLight[count];

        for (int at = 0; at < count; at++)
        {
            frames[at] = new FrameLight(bytes[at], bytes[count + at]);
        }

        return frames;
    }
}
