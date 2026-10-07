using Carina.Domain.Encodings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class LearningDataCollectorTests
{
    private const int TenMinutesAndASecond = LearningData.ChunkSeconds + 1;

    private static readonly FrameClock Broadcast = FrameClock.Of(30000, 1001, TimeSpan.FromMilliseconds(200));

    private static readonly Lazy<SyntheticRecording> Recording = new(() => new SyntheticRecording(TenMinutesAndASecond, Broadcast));

    private static readonly Lazy<List<IReadOnlyList<LearningDataChunk>>> Staged = new(() => HandedOverInStages(Recording.Value));

    private static List<LearningDataChunk> Chunks => [.. Staged.Value.SelectMany(handedBack => handedBack)];

    [Fact(DisplayName = "a chunk of ten minutes holds 37,500 fingerprints, 30,000 loudness readings, 6,000 channel differences, 600 corner outlines and the frames that fall in it")]
    public void AChunkHoldsTenMinutesOfEachKind()
    {
        List<LearningDataChunk> chunks = Chunks;
        LearningDataChunk first = chunks[0];
        long framesInTheFirst = Broadcast.FirstFrameFrom(TimeSpan.FromSeconds(LearningData.ChunkSeconds));

        Assert.Equal(2, chunks.Count);
        Assert.Equal((0, TimeSpan.Zero), (first.Index, first.Starts));
        Assert.Equal(37_500, first.Fingerprints.Length);
        Assert.Equal(30_000, first.Loudness.Length);
        Assert.Equal(6_000, first.Channels.Length);
        Assert.Equal(framesInTheFirst, first.Frames.Length);
        Assert.Equal(600, first.Outlines);
        Assert.Equal(Broadcast, first.Clock);
    }

    [Fact(DisplayName = "the last chunk holds what is left after the last ten minutes, its frames counted from the first one inside it")]
    public void TheLastChunkHoldsWhatIsLeft()
    {
        LearningDataChunk last = Chunks[1];
        long framesBefore = Broadcast.FirstFrameFrom(TimeSpan.FromSeconds(LearningData.ChunkSeconds));

        Assert.Equal((1, TimeSpan.FromSeconds(LearningData.ChunkSeconds)), (last.Index, last.Starts));
        Assert.Equal(63, last.Fingerprints.Length);
        Assert.Equal(50, last.Loudness.Length);
        Assert.Equal(10, last.Channels.Length);
        Assert.Equal(Recording.Value.Frames - framesBefore, last.Frames.Length);
        Assert.Equal(1, last.Outlines);
        Assert.Equal(Broadcast.At(framesBefore), last.Clock.FirstFrameAt);
        Assert.True(last.Clock.FirstFrameAt >= last.Starts);
    }

    [Fact(DisplayName = "the chunks are the same to the bit however the sound, the frames and the pictures are cut into pieces and interleaved")]
    public void TheChunksAreTheSameHoweverThePiecesAreCut()
    {
        List<byte[]> staged = [.. Chunks.Select(LearningDataFormat.Write)];
        List<byte[]> pieced = [.. Collected(Recording.Value, new Cutting(1, 40_001, (3 * FrameLight.Pixels) + 1, (2 * WatermarkFrame.Pixels) + 1)).Select(LearningDataFormat.Write)];

        Assert.Equal(staged, pieced);
    }

    [Fact(DisplayName = "a chunk is handed back only once all three have gone past its end, the last fingerprint waiting for the rest of its window")]
    public void AChunkIsHandedBackOnceAllThreeArePastItsEnd()
    {
        List<IReadOnlyList<LearningDataChunk>> stages = Staged.Value;

        Assert.All(stages.Take(4), Assert.Empty);
        Assert.Equal(0, Assert.Single(stages[4]).Index);
        Assert.Empty(stages[5]);
        Assert.Equal(1, Assert.Single(stages[6]).Index);
    }

    [Fact(DisplayName = "a recording shorter than a chunk is handed back whole when it is finished, and not before")]
    public void AShortRecordingIsHandedBackAtTheFinish()
    {
        SyntheticRecording recording = new(3, Broadcast);
        LearningDataCollector collector = new(Broadcast);

        Assert.Empty(collector.Hear(recording.Sound));
        Assert.Empty(Fed(piece => collector.See(piece), recording.FrameBytes, SyntheticRecording.FrameBytesAt));
        Assert.Empty(Fed(piece => collector.Glimpse(piece), recording.PictureBytes, SyntheticRecording.PictureBytesAt));

        LearningDataChunk chunk = Assert.Single(collector.Finish());

        Assert.Equal(188, chunk.Fingerprints.Length);
        Assert.Equal(150, chunk.Loudness.Length);
        Assert.Equal(30, chunk.Channels.Length);
        Assert.Equal(recording.Frames, chunk.Frames.Length);
        Assert.Equal(3, chunk.Outlines);
    }

    [Fact(DisplayName = "a recording with sound and no pictures is still handed back, its chunks holding no frames")]
    public void SoundAloneIsHandedBack()
    {
        LearningDataCollector collector = new(Broadcast);
        collector.Hear(SyntheticSound.Mono(SyntheticSound.Tones(4, 2 * SoundReader.SampleRate)));

        LearningDataChunk chunk = Assert.Single(collector.Finish());

        Assert.Equal(0, chunk.Frames.Length);
        Assert.Equal(0, chunk.Outlines);
        Assert.Equal(125, chunk.Fingerprints.Length);
    }

    [Fact(DisplayName = "nothing handed over hands back nothing, and nothing more is taken after the finish")]
    public void NothingHandedOverHandsBackNothing()
    {
        LearningDataCollector collector = new(Broadcast);

        Assert.Empty(collector.Finish());
        Assert.Throws<InvalidOperationException>(() => collector.Hear(new short[2]));
        Assert.Throws<InvalidOperationException>(() => collector.See(new byte[1]));
        Assert.Throws<InvalidOperationException>(() => collector.Glimpse(new byte[1]));
        Assert.Throws<InvalidOperationException>(() => collector.Finish());
    }

    private static List<LearningDataChunk> Collected(SyntheticRecording recording, Cutting cutting)
    {
        LearningDataCollector collector = new(recording.Clock);
        List<LearningDataChunk> chunks = [];
        Random random = new(cutting.Seed);
        long sound = 0;
        long frames = 0;
        long pictures = 0;
        byte[] buffer = new byte[Math.Max(cutting.MostFrameBytes, cutting.MostPictureBytes)];

        while (sound < recording.Sound.Length || frames < recording.FrameBytes || pictures < recording.PictureBytes)
        {
            int kind = random.Next(3);

            if (kind is 0 && sound < recording.Sound.Length)
            {
                int length = (int)Math.Min(recording.Sound.Length - sound, Cutting.Length(random, cutting.MostSamples));
                chunks.AddRange(collector.Hear(recording.Sound.AsSpan((int)sound, length)));
                sound += length;
            }

            if (kind is 1 && frames < recording.FrameBytes)
            {
                int length = (int)Math.Min(recording.FrameBytes - frames, Cutting.Length(random, cutting.MostFrameBytes));
                SyntheticRecording.FrameBytesAt(frames, buffer.AsSpan(0, length));
                chunks.AddRange(collector.See(buffer.AsSpan(0, length)));
                frames += length;
            }

            if (kind is 2 && pictures < recording.PictureBytes)
            {
                int length = (int)Math.Min(recording.PictureBytes - pictures, Cutting.Length(random, cutting.MostPictureBytes));
                SyntheticRecording.PictureBytesAt(pictures, buffer.AsSpan(0, length));
                chunks.AddRange(collector.Glimpse(buffer.AsSpan(0, length)));
                pictures += length;
            }
        }

        chunks.AddRange(collector.Finish());

        return chunks;
    }

    private static List<IReadOnlyList<LearningDataChunk>> HandedOverInStages(SyntheticRecording recording)
    {
        LearningDataCollector collector = new(recording.Clock);
        int lastWindowEnds = ((SoundFingerprint.PerChunk - 1) * SoundFingerprint.Hop) + SoundSpectrum.Window;
        long picturesBeforeTheLast = (long)(CornerOutline.PerChunk - 1) * WatermarkFrame.Pixels;
        byte[] lastPicture = new byte[WatermarkFrame.Pixels];
        byte[] rest = new byte[recording.PictureBytes - picturesBeforeTheLast - WatermarkFrame.Pixels];
        SyntheticRecording.PictureBytesAt(picturesBeforeTheLast, lastPicture);
        SyntheticRecording.PictureBytesAt(picturesBeforeTheLast + WatermarkFrame.Pixels, rest);

        return
        [
            Fed(piece => collector.Glimpse(piece), picturesBeforeTheLast, SyntheticRecording.PictureBytesAt),
            Fed(piece => collector.See(piece), recording.FrameBytes, SyntheticRecording.FrameBytesAt),
            collector.Hear(recording.Sound.AsSpan(0, 2 * (lastWindowEnds - 1))),
            collector.Glimpse(lastPicture),
            collector.Hear(recording.Sound.AsSpan(2 * (lastWindowEnds - 1), 2)),
            [.. collector.Hear(recording.Sound.AsSpan(2 * lastWindowEnds)), .. collector.Glimpse(rest)],
            collector.Finish(),
        ];
    }

    private static List<LearningDataChunk> Fed(Func<byte[], IReadOnlyList<LearningDataChunk>> feed, long bytes, Action<long, Span<byte>> fill)
    {
        List<LearningDataChunk> handedBack = [];
        byte[] piece = new byte[WatermarkFrame.Pixels];

        for (long at = 0; at < bytes; at += piece.Length)
        {
            int length = (int)Math.Min(piece.Length, bytes - at);
            byte[] part = length == piece.Length ? piece : new byte[length];
            fill(at, part);
            handedBack.AddRange(feed(part));
        }

        return handedBack;
    }

    private sealed record Cutting(int Seed, int MostSamples, int MostFrameBytes, int MostPictureBytes)
    {
        public static int Length(Random random, int most) => random.Next(2) is 0 ? random.Next(1, 100) : random.Next(1, most + 1);
    }
}
