using Carina.Domain.Encodings;
using Carina.Domain.Segments;
using Carina.Domain.Tests.Encodings;

namespace Carina.Domain.Tests.Segments;

public sealed class LearningDataTimelineTests
{
    private const int Block = 170;

    private static readonly TimeSpan BroadcastFrame = TimeSpan.FromTicks(333_666);

    private static readonly TimeSpan FirstFrame = TimeSpan.FromMilliseconds(33);

    private static readonly FrameClock Broadcast = FrameClock.Of(30000, 1001, FirstFrame);

    [Fact(DisplayName = "sound, frames and pictures that come in step give the bytes the collector gives when handed them directly")]
    public void WhatComesInStepGivesWhatTheCollectorGives()
    {
        LearningDataTimeline timeline = new(BroadcastFrame);
        List<LearningDataChunk> placed = Fed(timeline, Stretch(0, 20));
        placed.AddRange(timeline.Finish());

        LearningDataCollector collector = new(Broadcast);
        List<LearningDataChunk> direct = [.. collector.Hear(Tone(0, 20 * SoundReader.SampleRate))];
        for (long frame = 0; Broadcast.At(frame) < TimeSpan.FromSeconds(20); frame++)
        {
            direct.AddRange(collector.See(Lit(128)));
        }

        for (int second = 0; second < 20; second++)
        {
            direct.AddRange(collector.Glimpse(WatermarkPictures.Plain()));
        }

        direct.AddRange(collector.Finish());

        Assert.Equal(Written(direct), Written(placed));
        Assert.Empty(timeline.RemainingGaps());
        Assert.Equal(new ExtractionSound(0, -FirstFrame), timeline.Sound);
    }

    [Fact(DisplayName = "a jump in the sound, the frames and the pictures is filled, kept as one gap, and leaves what follows at its own time")]
    public void AJumpIsFilledKeptAsOneGapAndLeavesWhatFollowsInPlace()
    {
        LearningDataTimeline timeline = new(BroadcastFrame);
        List<LearningDataChunk> chunks = Fed(timeline, [.. Stretch(0, 12), .. Stretch(30, 40, quietFrom: 34)]);
        chunks.AddRange(timeline.Finish());

        LearningDataChunk chunk = Assert.Single(chunks);
        LearningDataGap gap = Assert.Single(timeline.RemainingGaps());

        Assert.Equal(40 * 50, chunk.Loudness.Length);
        Assert.All(chunk.Loudness[(12 * 50)..(30 * 50)].ToArray(), reading => Assert.Equal(SoundLoudness.Silent, reading));
        Assert.NotEqual(SoundLoudness.Silent, chunk.Loudness[(30 * 50) + 2]);
        Assert.Equal(SoundLoudness.Silent, chunk.Loudness[(34 * 50) + 1]);
        Assert.NotEqual(SoundLoudness.Silent, chunk.Loudness[(34 * 50) - 2]);
        Assert.Equal(Broadcast.FirstFrameFrom(TimeSpan.FromSeconds(40)), chunk.Frames.Length);
        Assert.Equal(0, chunk.Frames[(int)Broadcast.FirstFrameFrom(TimeSpan.FromSeconds(34.1))].Brightness);
        Assert.Equal(128, chunk.Frames[(int)Broadcast.FirstFrameFrom(TimeSpan.FromSeconds(33.9))].Brightness);
        Assert.Equal(40, chunk.Outlines);
        Assert.InRange(gap.From.TotalSeconds, 11.96, 12.04);
        Assert.InRange(gap.Until.TotalSeconds, 29.96, 30.04);
    }

    [Fact(DisplayName = "sound that overlaps itself where its shape changes loses the overlap and keeps what follows at its own time")]
    public void SoundThatOverlapsItselfLosesTheOverlap()
    {
        LearningDataTimeline timeline = new(BroadcastFrame);
        List<Handed> handed =
        [
            .. Sound(0, 10),
            .. Sound(9.98, 20, quietFrom: 15),
            .. Frames(0, 20),
            .. Pictures(0, 20),
        ];

        List<LearningDataChunk> chunks = Fed(timeline, handed, inTimeOrder: false);
        chunks.AddRange(timeline.Finish());

        LearningDataChunk chunk = Assert.Single(chunks);

        Assert.Equal(20 * 50, chunk.Loudness.Length);
        Assert.Equal(SoundLoudness.Silent, chunk.Loudness[(15 * 50) + 1]);
        Assert.NotEqual(SoundLoudness.Silent, chunk.Loudness[(15 * 50) - 2]);
        Assert.Empty(timeline.RemainingGaps());
    }

    [Fact(DisplayName = "sound handed over before the first frame waits for it, is placed from zero, and keeps how far it lies from the first frame")]
    public void SoundBeforeTheFirstFrameWaitsAndIsPlacedFromZero()
    {
        LearningDataTimeline timeline = new(BroadcastFrame);
        TimeSpan late = TimeSpan.FromMilliseconds(500);

        List<LearningDataChunk> chunks = Fed(timeline, Sound(0, 5));

        Assert.Null(timeline.Sound);

        chunks.AddRange(Fed(timeline, [.. Frames(0.5, 5), .. Pictures(0, 5)]));
        chunks.AddRange(timeline.Finish());

        LearningDataChunk chunk = Assert.Single(chunks);

        Assert.Equal(5 * 50, chunk.Loudness.Length);
        Assert.Equal(late, chunk.Clock.FirstFrameAt);
        Assert.Equal(new ExtractionSound(0, -late), timeline.Sound);
    }

    [Fact(DisplayName = "without a frame length the rate is read from the first two frames")]
    public void WithoutALengthTheRateIsReadFromTheFirstTwoFrames()
    {
        LearningDataTimeline timeline = new(null);
        List<Handed> handed = [.. Sound(0, 4), .. Pictures(0, 4)];
        handed.AddRange(Enumerable.Range(0, 100).Select(frame => new Handed(TimeSpan.FromMilliseconds(40 * frame), null, Lit(90), null)));

        List<LearningDataChunk> chunks = Fed(timeline, handed);
        chunks.AddRange(timeline.Finish());

        LearningDataChunk chunk = Assert.Single(chunks);

        Assert.Equal((25, 1, TimeSpan.Zero), (chunk.Clock.Numerator, chunk.Clock.Denominator, chunk.Clock.FirstFrameAt));
        Assert.Equal(100, chunk.Frames.Length);
    }

    [Fact(DisplayName = "a gap is handed out only once both the sound and the frames have gone past it")]
    public void AGapIsHandedOutOnceBothHaveGonePastIt()
    {
        LearningDataTimeline timeline = new(BroadcastFrame);
        Fed(timeline, [.. Frames(0, 1.5), .. Pictures(0, 2), .. Sound(0, 1), .. Sound(2, 3)]);

        Assert.Empty(timeline.SettledGaps());

        Fed(timeline, [.. Frames(1.5, 4), .. Sound(3, 4)]);

        Assert.Equal([new LearningDataGap(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2))], timeline.SettledGaps());
        Assert.Empty(timeline.SettledGaps());
    }

    [Fact(DisplayName = "a recording that hands over no frames, or no sound, cannot be placed for want of a stream")]
    public void NoFramesOrNoSoundIsAStreamMissing()
    {
        LearningDataTimeline soundAlone = new(BroadcastFrame);
        Fed(soundAlone, Sound(0, 2));
        LearningDataTimeline framesAlone = new(BroadcastFrame);
        Fed(framesAlone, Frames(0, 2));

        Assert.Equal(ExtractionFailure.StreamMissing, Assert.Throws<LearningDataUnplaceableException>(() => soundAlone.Finish()).Failure);
        Assert.Equal(ExtractionFailure.StreamMissing, Assert.Throws<LearningDataUnplaceableException>(() => framesAlone.Finish()).Failure);
    }

    [Fact(DisplayName = "sound that waits more than a minute for a frame cannot be placed for want of a stream")]
    public void SoundWaitingTooLongForAFrameIsAStreamMissing()
    {
        LearningDataTimeline timeline = new(BroadcastFrame);

        LearningDataUnplaceableException refused = Assert.Throws<LearningDataUnplaceableException>(() => Fed(timeline, Sound(0, 61)));

        Assert.Equal(ExtractionFailure.StreamMissing, refused.Failure);
    }

    [Fact(DisplayName = "a frame, a picture or a block of sound of the wrong shape is refused")]
    public void WhatIsOfTheWrongShapeIsRefused()
    {
        LearningDataTimeline timeline = new(BroadcastFrame);

        Assert.Equal(ExtractionFailure.Other, Assert.Throws<LearningDataUnplaceableException>(() => timeline.See(TimeSpan.Zero, new byte[FrameLight.Pixels - 1])).Failure);
        Assert.Equal(ExtractionFailure.Other, Assert.Throws<LearningDataUnplaceableException>(() => timeline.Glimpse(TimeSpan.Zero, new byte[10])).Failure);
        Assert.Equal(ExtractionFailure.Other, Assert.Throws<LearningDataUnplaceableException>(() => timeline.Hear(TimeSpan.Zero, new short[3])).Failure);
    }

    [Fact(DisplayName = "the first two frames too far apart to read a rate from are a timing that does not add up")]
    public void TheFirstTwoFramesTooFarApartAreATimingMismatch()
    {
        LearningDataTimeline timeline = new(null);
        timeline.See(TimeSpan.Zero, Lit(10));

        Assert.Equal(
            ExtractionFailure.TimingMismatch,
            Assert.Throws<LearningDataUnplaceableException>(() => timeline.See(TimeSpan.FromSeconds(3), Lit(10))).Failure);
    }

    private static List<Handed> Stretch(double from, double until, double? quietFrom = null)
        => [.. Sound(from, until, quietFrom), .. Frames(from, until, quietFrom), .. Pictures(from, until)];

    private static IEnumerable<Handed> Sound(double from, double until, double? quietFrom = null)
    {
        long first = (long)Math.Round(from * SoundReader.SampleRate);
        long last = (long)Math.Round(until * SoundReader.SampleRate);

        for (long pair = first; pair < last; pair += Block)
        {
            int pairs = (int)Math.Min(Block, last - pair);
            short[] samples = Tone(pair, pairs);

            Quieted(samples, pair, quietFrom);

            yield return new Handed(Millisecond((double)pair / SoundReader.SampleRate), samples, null, null);
        }
    }

    private static IEnumerable<Handed> Frames(double from, double until, double? darkFrom = null)
    {
        for (long frame = Broadcast.FirstFrameFrom(TimeSpan.FromSeconds(from)); Broadcast.At(frame) < TimeSpan.FromSeconds(until); frame++)
        {
            double at = Broadcast.At(frame).TotalSeconds;
            bool dark = darkFrom is { } start && at >= start && at < start + 0.4;

            yield return new Handed(Millisecond(at), null, Lit(dark ? (byte)0 : (byte)128), null);
        }
    }

    private static IEnumerable<Handed> Pictures(double from, double until)
    {
        for (int second = (int)Math.Ceiling(from); second < until; second++)
        {
            yield return new Handed(Millisecond(second + FirstFrame.TotalSeconds), null, null, WatermarkPictures.Plain());
        }
    }

    private static List<LearningDataChunk> Fed(LearningDataTimeline timeline, IEnumerable<Handed> handed, bool inTimeOrder = true)
    {
        List<LearningDataChunk> chunks = [];

        foreach (Handed piece in inTimeOrder ? handed.OrderBy(piece => piece.At) : handed)
        {
            chunks.AddRange(Handover(timeline, piece));
        }

        return chunks;
    }

    private static IReadOnlyList<LearningDataChunk> Handover(LearningDataTimeline timeline, Handed piece)
        => piece switch
        {
            { Sound: { } sound } => timeline.Hear(piece.At, sound),
            { Frame: { } frame } => timeline.See(piece.At, frame),
            { Picture: { } picture } => timeline.Glimpse(piece.At, picture),
            _ => [],
        };

    private static short[] Tone(long firstPair, int pairs)
    {
        short[] samples = new short[pairs * SoundReader.Channels];

        for (int pair = 0; pair < pairs; pair++)
        {
            short value = (short)(8000 * Math.Sin(2 * Math.PI * 440 * (firstPair + pair) / SoundReader.SampleRate));
            samples[2 * pair] = value;
            samples[(2 * pair) + 1] = value;
        }

        return samples;
    }

    private static void Quieted(short[] samples, long firstPair, double? quietFrom)
    {
        if (quietFrom is not { } start)
        {
            return;
        }

        for (int pair = 0; pair < samples.Length / SoundReader.Channels; pair++)
        {
            double at = (double)(firstPair + pair) / SoundReader.SampleRate;

            if (at >= start && at < start + 0.4)
            {
                samples[2 * pair] = 0;
                samples[(2 * pair) + 1] = 0;
            }
        }
    }

    private static byte[] Lit(byte brightness)
    {
        byte[] frame = new byte[FrameLight.Pixels];
        Array.Fill(frame, brightness);

        return frame;
    }

    private static TimeSpan Millisecond(double seconds) => TimeSpan.FromMilliseconds(Math.Round(seconds * 1000));

    private static List<byte[]> Written(List<LearningDataChunk> chunks)
        => [.. chunks.SelectMany(chunk => LearningDataChunk.Kinds.Select(kind => LearningDataFormat.Write(chunk, kind)))];

    private sealed record Handed(TimeSpan At, short[]? Sound, byte[]? Frame, byte[]? Picture);
}
