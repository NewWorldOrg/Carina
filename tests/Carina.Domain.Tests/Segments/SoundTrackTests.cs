using System.Buffers.Binary;
using System.IO.Compression;

using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SoundTrackTests
{
    [Fact(DisplayName = "the fingerprints and loudness of each chunk are placed at the chunk's own time, a missing chunk heard as silence")]
    public void ChunksArePlacedAtTheirOwnTime()
    {
        LearningDataPart first = Read(LearningDataKind.SoundFingerprints, 0, Fingerprints(SoundFingerprint.PerChunk, 1));
        LearningDataPart third = Read(LearningDataKind.SoundFingerprints, 2, Fingerprints(10, 3));
        LearningDataPart loudness = Read(LearningDataKind.Loudness, 2, [.. Enumerable.Repeat((byte)40, 5)]);

        SoundTrack track = SoundTrack.Of([third, loudness, first]);

        Assert.Equal((2 * SoundFingerprint.PerChunk) + 10, track.Fingerprints.Length);
        Assert.Equal(1u, track.Fingerprints[0]);
        Assert.Equal(0u, track.Fingerprints[SoundFingerprint.PerChunk]);
        Assert.Equal(3u, track.Fingerprints[2 * SoundFingerprint.PerChunk]);
        Assert.Equal((2 * SoundLoudness.PerChunk) + 5, track.Loudness.Length);
        Assert.Equal(SoundLoudness.Silent, track.Loudness[0]);
        Assert.Equal(40, track.Loudness[2 * SoundLoudness.PerChunk]);
    }

    [Fact(DisplayName = "the kinds of learning data other than fingerprints and loudness are passed over")]
    public void OtherKindsArePassedOver()
    {
        LearningDataPart captions = LearningDataPart.OfCaptions(3, [CaptionPresence.Shown]);

        SoundTrack track = SoundTrack.Of([captions]);

        Assert.Equal(0, track.Fingerprints.Length);
        Assert.Equal(TimeSpan.Zero, track.Length);
    }

    [Fact(DisplayName = "a frame of fingerprint is 16 ms, and a stretch is cut out on the frames it covers")]
    public void AStretchIsCutOutOnItsFrames()
    {
        SoundTrack track = SoundTrack.Of([.. Enumerable.Range(1, 200).Select(value => (uint)value)], []);

        uint[] cut = track.Slice(new SoundSpan(TimeSpan.FromMilliseconds(160), TimeSpan.FromMilliseconds(320)));

        Assert.Equal(TimeSpan.FromMilliseconds(16), SegmentMatcher.Frame);
        Assert.Equal(TimeSpan.FromMilliseconds(3200), track.Length);
        Assert.Equal([11u, 12u, 13u, 14u, 15u, 16u, 17u, 18u, 19u, 20u], cut);
    }

    private static uint[] Fingerprints(int count, uint value) => [.. Enumerable.Repeat(value, count)];

    private static LearningDataPart Read(LearningDataKind kind, int index, uint[] fingerprints)
    {
        byte[] readings = new byte[fingerprints.Length * sizeof(uint)];

        for (int at = 0; at < fingerprints.Length; at++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(readings.AsSpan(at * sizeof(uint)), fingerprints[at]);
        }

        return Read(kind, index, fingerprints.Length, readings);
    }

    private static LearningDataPart Read(LearningDataKind kind, int index, byte[] readings) => Read(kind, index, readings.Length, readings);

    private static LearningDataPart Read(LearningDataKind kind, int index, int count, byte[] readings)
    {
        byte[] compressed = new byte[BrotliEncoder.GetMaxCompressedLength(readings.Length)];

        Assert.True(BrotliEncoder.TryCompress(readings, compressed, out int length));

        byte[] bytes = new byte[15 + length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, LearningDataFormat.Version);
        bytes[2] = (byte)kind;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(3), index);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(7), count);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(11), length);
        compressed.AsSpan(0, length).CopyTo(bytes.AsSpan(15));

        Assert.True(LearningDataFormat.TryRead(bytes, kind, out LearningDataPart? part));

        return part;
    }
}
