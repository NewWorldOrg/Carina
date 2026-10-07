using System.Buffers.Binary;

using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class LearningDataFormatTests
{
    private static readonly FrameClock Broadcast = FrameClock.Of(30000, 1001, TimeSpan.FromMilliseconds(200));

    [Fact(DisplayName = "a chunk written and read back is the chunk that was written")]
    public void AChunkReadsBackAsWritten()
    {
        LearningDataChunk written = Sample(3);

        Assert.True(LearningDataFormat.TryRead(LearningDataFormat.Write(written), out LearningDataChunk? read));
        AssertSame(written, read);
    }

    [Fact(DisplayName = "a chunk with nothing in it is written and read back too")]
    public void AnEmptyChunkReadsBack()
    {
        LearningDataChunk empty = LearningDataChunk.Of(4, Broadcast.From(LearningData.ChunkStarts(4)), [], [], [], [], []);

        Assert.True(LearningDataFormat.TryRead(LearningDataFormat.Write(empty), out LearningDataChunk? read));
        AssertSame(empty, read);
    }

    [Fact(DisplayName = "the bytes begin with the version of their shape")]
    public void TheBytesBeginWithTheirVersion()
        => Assert.Equal(LearningDataFormat.Version, BinaryPrimitives.ReadUInt16LittleEndian(LearningDataFormat.Write(Sample(1))));

    [Fact(DisplayName = "bytes of a version this shape does not know are not read")]
    public void AnUnknownVersionIsNotRead()
    {
        byte[] bytes = LearningDataFormat.Write(Sample(1));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, LearningDataFormat.Version + 1);

        Assert.False(LearningDataFormat.TryRead(bytes, out LearningDataChunk? chunk));
        Assert.Null(chunk);
    }

    [Fact(DisplayName = "bytes cut short anywhere, or with anything after the end, are not read")]
    public void CutOrOverlongBytesAreNotRead()
    {
        byte[] bytes = LearningDataFormat.Write(Sample(1));

        Assert.All(Enumerable.Range(0, bytes.Length), length => Assert.False(LearningDataFormat.TryRead(bytes.AsSpan(0, length), out _)));
        Assert.False(LearningDataFormat.TryRead([.. bytes, 0], out _));
    }

    [Fact(DisplayName = "a section of another kind in the place of the first is not read")]
    public void ASectionOutOfPlaceIsNotRead()
    {
        byte[] bytes = LearningDataFormat.Write(Sample(1));
        bytes[22] = 2;

        Assert.False(LearningDataFormat.TryRead(bytes, out _));
    }

    [Fact(DisplayName = "a section claiming more readings than a chunk holds is not read")]
    public void AnOverfullSectionIsNotRead()
    {
        byte[] bytes = LearningDataFormat.Write(Sample(1));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(23), SoundFingerprint.PerChunk + 1);

        Assert.False(LearningDataFormat.TryRead(bytes, out _));
    }

    [Fact(DisplayName = "bytes that are not a chunk at all are not read, and damage anywhere never throws")]
    public void DamagedBytesNeverThrow()
    {
        Random random = new(17);
        byte[] noise = new byte[4096];
        random.NextBytes(noise);
        byte[] bytes = LearningDataFormat.Write(Sample(1));

        Assert.False(LearningDataFormat.TryRead(noise, out _));

        for (int at = 0; at < bytes.Length; at++)
        {
            byte[] damaged = [.. bytes];
            damaged[at] ^= 0x5A;
            LearningDataFormat.TryRead(damaged, out _);
        }
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

        Assert.True(LearningDataFormat.Write(quiet).Length < readings / 100);
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

    private static void AssertSame(LearningDataChunk expected, LearningDataChunk? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Index, actual.Index);
        Assert.Equal(expected.Clock, actual.Clock);
        Assert.Equal(expected.Fingerprints.ToArray(), actual.Fingerprints.ToArray());
        Assert.Equal(expected.Loudness.ToArray(), actual.Loudness.ToArray());
        Assert.Equal(expected.Channels.ToArray(), actual.Channels.ToArray());
        Assert.Equal(expected.Frames.ToArray(), actual.Frames.ToArray());
        Assert.Equal(expected.CornerOutlines.ToArray(), actual.CornerOutlines.ToArray());
    }
}
