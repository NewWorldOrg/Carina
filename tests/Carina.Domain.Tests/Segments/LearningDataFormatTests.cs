using System.Buffers.Binary;

using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class LearningDataFormatTests
{
    private const int KindAt = 2;

    private const int IndexAt = 3;

    private static readonly FrameClock Broadcast = FrameClock.Of(30000, 1001, TimeSpan.FromMilliseconds(200));

    private static readonly Lazy<LearningDataChunk> ThreeSeconds = new(() => Sample(3));

    public static TheoryData<LearningDataKind> Kinds => [.. LearningDataChunk.Kinds];

    public static TheoryData<LearningDataKind, LearningDataKind> MistakenKinds
    {
        get
        {
            TheoryData<LearningDataKind, LearningDataKind> pairs = [];

            foreach (LearningDataKind written in LearningDataChunk.Kinds)
            {
                foreach (LearningDataKind asked in LearningDataChunk.Kinds.Where(asked => asked != written))
                {
                    pairs.Add(written, asked);
                }
            }

            return pairs;
        }
    }

    [Fact(DisplayName = "the kinds of data keep their numbers")]
    public void TheKindsKeepTheirNumbers()
    {
        Assert.Equal(1, (byte)LearningDataKind.SoundFingerprints);
        Assert.Equal(2, (byte)LearningDataKind.Loudness);
        Assert.Equal(3, (byte)LearningDataKind.ChannelDifferences);
        Assert.Equal(4, (byte)LearningDataKind.FrameLights);
        Assert.Equal(5, (byte)LearningDataKind.CornerOutlines);
        Assert.Equal(Enum.GetValues<LearningDataKind>(), LearningDataChunk.Kinds);
    }

    [Theory(DisplayName = "one kind of a chunk written and read back holds that kind of the chunk and nothing else")]
    [MemberData(nameof(Kinds))]
    public void AKindReadsBackAsWritten(LearningDataKind kind)
    {
        LearningDataChunk chunk = ThreeSeconds.Value;

        Assert.True(LearningDataFormat.TryRead(LearningDataFormat.Write(chunk, kind), kind, out LearningDataPart? read));
        AssertHolds(LearningDataPart.Of(chunk, kind), read);
        Assert.True(read.Count > 0);
        Assert.Equal(kind is LearningDataKind.FrameLights ? chunk.Clock : null, read.Clock);
    }

    [Theory(DisplayName = "one kind of a chunk with nothing in it is written and read back too")]
    [MemberData(nameof(Kinds))]
    public void AnEmptyKindReadsBack(LearningDataKind kind)
    {
        LearningDataChunk empty = LearningDataChunk.Of(4, Broadcast.From(LearningData.ChunkStarts(4)), [], [], [], [], []);

        Assert.True(LearningDataFormat.TryRead(LearningDataFormat.Write(empty, kind), kind, out LearningDataPart? read));
        AssertHolds(LearningDataPart.Of(empty, kind), read);
    }

    [Theory(DisplayName = "the bytes begin with the version of their shape, the kind and the chunk's index")]
    [MemberData(nameof(Kinds))]
    public void TheBytesBeginWithVersionKindAndIndex(LearningDataKind kind)
    {
        byte[] bytes = LearningDataFormat.Write(LearningDataChunk.Of(7, Broadcast.From(LearningData.ChunkStarts(7)), [], [], [], [], []), kind);

        Assert.Equal(LearningDataFormat.Version, BinaryPrimitives.ReadUInt16LittleEndian(bytes));
        Assert.Equal((byte)kind, bytes[KindAt]);
        Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(IndexAt)));
    }

    [Theory(DisplayName = "bytes of one kind read as another kind are not read")]
    [MemberData(nameof(MistakenKinds))]
    public void AnotherKindIsNotRead(LearningDataKind written, LearningDataKind asked)
    {
        Assert.False(LearningDataFormat.TryRead(LearningDataFormat.Write(ThreeSeconds.Value, written), asked, out LearningDataPart? read));
        Assert.Null(read);
    }

    [Fact(DisplayName = "bytes that say one kind while holding another are not read")]
    public void AKindRelabelledIsNotRead()
    {
        byte[] bytes = LearningDataFormat.Write(ThreeSeconds.Value, LearningDataKind.Loudness);
        bytes[KindAt] = (byte)LearningDataKind.FrameLights;

        Assert.False(LearningDataFormat.TryRead(bytes, LearningDataKind.FrameLights, out _));
    }

    [Theory(DisplayName = "bytes of a version this shape does not know are not read")]
    [MemberData(nameof(Kinds))]
    public void AnUnknownVersionIsNotRead(LearningDataKind kind)
    {
        byte[] bytes = LearningDataFormat.Write(ThreeSeconds.Value, kind);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, LearningDataFormat.Version + 1);

        Assert.False(LearningDataFormat.TryRead(bytes, kind, out _));
    }

    [Theory(DisplayName = "bytes cut short anywhere, or with anything after the end, are not read")]
    [MemberData(nameof(Kinds))]
    public void CutOrOverlongBytesAreNotRead(LearningDataKind kind)
    {
        byte[] bytes = LearningDataFormat.Write(ThreeSeconds.Value, kind);

        Assert.All(Enumerable.Range(0, bytes.Length), length => Assert.False(LearningDataFormat.TryRead(bytes.AsSpan(0, length), kind, out _)));
        Assert.False(LearningDataFormat.TryRead([.. bytes, 0], kind, out _));
    }

    [Theory(DisplayName = "readings claiming more than a chunk holds are not read")]
    [MemberData(nameof(Kinds))]
    public void AnOverfullKindIsNotRead(LearningDataKind kind)
    {
        byte[] bytes = LearningDataFormat.Write(ThreeSeconds.Value, kind);
        int countAt = kind is LearningDataKind.FrameLights ? 23 : 7;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(countAt), SoundFingerprint.PerChunk + 1);

        Assert.False(LearningDataFormat.TryRead(bytes, kind, out _));
    }

    [Fact(DisplayName = "frame lights whose first frame comes before their chunk are not read")]
    public void FramesBeforeTheirChunkAreNotRead()
    {
        byte[] bytes = LearningDataFormat.Write(ThreeSeconds.Value, LearningDataKind.FrameLights);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(IndexAt), 1);

        Assert.False(LearningDataFormat.TryRead(bytes, LearningDataKind.FrameLights, out _));
    }

    [Theory(DisplayName = "bytes that are not data at all are not read, and damage anywhere never throws")]
    [MemberData(nameof(Kinds))]
    public void DamagedBytesNeverThrow(LearningDataKind kind)
    {
        Random random = new(17);
        byte[] noise = new byte[4096];
        random.NextBytes(noise);
        byte[] bytes = LearningDataFormat.Write(ThreeSeconds.Value, kind);

        Assert.False(LearningDataFormat.TryRead(noise, kind, out _));

        for (int at = 0; at < bytes.Length; at++)
        {
            byte[] damaged = [.. bytes];
            damaged[at] ^= 0x5A;
            LearningDataFormat.TryRead(damaged, kind, out _);
        }
    }

    [Fact(DisplayName = "a kind no chunk holds is neither cut from a chunk nor read")]
    public void AnUnknownKindIsRefused()
    {
        LearningDataKind unknown = (LearningDataKind)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => LearningDataPart.Of(ThreeSeconds.Value, unknown));
        Assert.False(LearningDataFormat.TryRead(LearningDataFormat.Write(ThreeSeconds.Value, LearningDataKind.Loudness), unknown, out _));
    }

    [Fact(DisplayName = "a quiet, dark, plain chunk is kept in a small share of the bytes its readings take")]
    public void AQuietChunkIsKeptSmall()
    {
        LearningDataChunk quiet = LearningDataChunk.Of(
            0,
            Broadcast,
            new uint[SoundFingerprint.PerChunk],
            Enumerable.Repeat(SoundLoudness.Silent, SoundLoudness.PerChunk).ToArray(),
            Enumerable.Repeat(new ChannelDifference(ChannelDifference.Unmeasured, SoundLoudness.Silent), ChannelDifference.PerChunk).ToArray(),
            Enumerable.Repeat(new FrameLight(16, 0), Broadcast.MostFramesIn(LearningData.ChunkSeconds) - 1).ToArray(),
            new byte[CornerOutline.PerChunk * CornerOutline.Bytes]);
        int readings = (SoundFingerprint.PerChunk * 4) + SoundLoudness.PerChunk + (ChannelDifference.PerChunk * 2) + (quiet.Frames.Length * 2) + quiet.CornerOutlines.Length;

        Assert.True(LearningDataChunk.Kinds.Sum(kind => LearningDataFormat.Write(quiet, kind).Length) < readings / 100);
    }

    [Fact(DisplayName = "a chunk holding more than its length takes, frames before its start or a torn corner outline is refused")]
    public void AnImpossibleChunkIsRefused()
    {
        FrameClock clock = Broadcast.From(LearningData.ChunkStarts(1));

        Assert.Throws<ArgumentException>(() => LearningDataChunk.Of(1, Broadcast, [], [], [], [], []));
        Assert.Throws<ArgumentException>(() => LearningDataChunk.Of(-1, Broadcast, [], [], [], [], []));
        Assert.Throws<ArgumentException>(() => LearningDataChunk.Of(1, clock, new uint[SoundFingerprint.PerChunk + 1], [], [], [], []));
        Assert.Throws<ArgumentException>(() => LearningDataChunk.Of(1, clock, [], new byte[SoundLoudness.PerChunk + 1], [], [], []));
        Assert.Throws<ArgumentException>(() => LearningDataChunk.Of(1, clock, [], [], new ChannelDifference[ChannelDifference.PerChunk + 1], [], []));
        Assert.Throws<ArgumentException>(() => LearningDataChunk.Of(1, clock, [], [], [], new FrameLight[clock.MostFramesIn(LearningData.ChunkSeconds) + 1], []));
        Assert.Throws<ArgumentException>(() => LearningDataChunk.Of(1, clock, [], [], [], [], new byte[(CornerOutline.PerChunk + 1) * CornerOutline.Bytes]));
        Assert.Throws<ArgumentException>(() => LearningDataChunk.Of(1, clock, [], [], [], [], new byte[CornerOutline.Bytes + 1]));
    }

    private static LearningDataChunk Sample(int seconds)
    {
        SyntheticRecording recording = new(seconds, Broadcast);
        LearningDataCollector collector = new(Broadcast);
        byte[] frames = new byte[recording.FrameBytes];
        byte[] pictures = new byte[recording.PictureBytes];
        SyntheticRecording.FrameBytesAt(0, frames);
        SyntheticRecording.PictureBytesAt(0, pictures);

        collector.Hear(recording.Sound);
        collector.See(frames);
        collector.Glimpse(pictures);

        return Assert.Single(collector.Finish());
    }

    private static void AssertHolds(LearningDataPart expected, LearningDataPart? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Index, actual.Index);
        Assert.Equal(expected.Clock, actual.Clock);
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.Fingerprints.ToArray(), actual.Fingerprints.ToArray());
        Assert.Equal(expected.Loudness.ToArray(), actual.Loudness.ToArray());
        Assert.Equal(expected.Channels.ToArray(), actual.Channels.ToArray());
        Assert.Equal(expected.Frames.ToArray(), actual.Frames.ToArray());
        Assert.Equal(expected.CornerOutlines.ToArray(), actual.CornerOutlines.ToArray());
    }
}
