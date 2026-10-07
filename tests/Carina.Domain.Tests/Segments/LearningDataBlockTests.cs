using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class LearningDataBlockTests
{
    private const int Chunk = 2;

    private static readonly DateTime Noon = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    public static TheoryData<LearningDataKind> Kinds => [.. LearningDataChunk.Kinds];

    [Theory(DisplayName = "a block keeps one kind of one chunk as the bytes that read back to it")]
    [MemberData(nameof(Kinds))]
    public void ABlockKeepsOneKindOfOneChunk(LearningDataKind kind)
    {
        RecordingId recording = RecordingId.New();
        LearningDataPart part = LearningDataPart.Of(Sample(), kind);

        LearningDataBlock block = LearningDataBlock.Of(recording, part, ExtractionVersion.Current, Noon);

        Assert.Equal(recording, block.RecordingId);
        Assert.Equal(kind, block.Kind);
        Assert.Equal(Chunk, block.Chunk);
        Assert.Equal(LearningDataFormat.Write(part), block.Bytes);
        Assert.Equal(ExtractionVersion.Current, block.Version);
        Assert.Equal(Noon, block.WrittenAt);
        Assert.True(block.TryRead(out LearningDataPart? read));
        Assert.Equal(part.Count, read.Count);
        Assert.Equal(kind, read.Kind);
    }

    [Fact(DisplayName = "bytes written for another kind or another chunk do not read as the block")]
    public void BytesForAnotherKindOrChunkDoNotRead()
    {
        byte[] loudness = LearningDataFormat.Write(Sample(), LearningDataKind.Loudness);

        LearningDataBlock otherKind = Block(LearningDataKind.SoundFingerprints, Chunk, loudness);
        LearningDataBlock otherChunk = Block(LearningDataKind.Loudness, Chunk + 1, loudness);

        Assert.False(otherKind.TryRead(out LearningDataPart? kindRead));
        Assert.Null(kindRead);
        Assert.False(otherChunk.TryRead(out LearningDataPart? chunkRead));
        Assert.Null(chunkRead);
    }

    [Fact(DisplayName = "a block with nothing in it, of a kind not kept, or of a chunk out of reach is refused")]
    public void ABlockOutOfShapeIsRefused()
    {
        byte[] loudness = LearningDataFormat.Write(Sample(), LearningDataKind.Loudness);

        Assert.Throws<ArgumentException>(() => Block(LearningDataKind.Loudness, Chunk, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => Block((LearningDataKind)99, Chunk, loudness));
        Assert.Throws<ArgumentOutOfRangeException>(() => Block(LearningDataKind.Loudness, -1, loudness));
        Assert.Throws<ArgumentOutOfRangeException>(() => Block(LearningDataKind.Loudness, LearningData.LastChunk + 1, loudness));
    }

    private static LearningDataBlock Block(LearningDataKind kind, int chunk, byte[] bytes)
        => LearningDataBlock.Rehydrate(RecordingId.New(), kind, chunk, bytes, ExtractionVersion.Current, Noon);

    private static LearningDataChunk Sample()
        => LearningDataChunk.Of(
            Chunk,
            FrameClock.Of(30000, 1001, LearningData.ChunkStarts(Chunk)),
            [0x0F0F_0F0Fu, 0xF0F0_F0F0u, 0x1234_5678u],
            [10, 20, 30, 40],
            [new ChannelDifference(200, 3)],
            [new FrameLight(16, 0), new FrameLight(120, 80)],
            new byte[CornerOutline.Bytes]);
}
