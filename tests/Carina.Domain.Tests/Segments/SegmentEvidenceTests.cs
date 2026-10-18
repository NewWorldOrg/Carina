using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentEvidenceTests
{
    [Fact(DisplayName = "the readings of the parts are placed at their chunks' own time, a missing chunk heard as silence, unmeasured channels and empty corners")]
    public void ThePartsArePlacedAtTheirChunksTime()
    {
        LearningDataChunk first = Chunk(0, 600, loudness: 40, correlation: 190);
        LearningDataChunk third = Chunk(2, 10, loudness: 60, correlation: 120);
        LearningDataPart[] parts =
        [
            .. new[] { third, first }.SelectMany(chunk => new[] { LearningDataKind.Loudness, LearningDataKind.ChannelDifferences, LearningDataKind.FrameLights, LearningDataKind.CornerOutlines }
                .Select(kind => LearningDataPart.Of(chunk, kind))),
        ];

        SegmentEvidence evidence = SegmentEvidence.Of(parts, TimeSpan.FromSeconds(1210));

        Assert.Equal((2 * SoundLoudness.PerChunk) + 500, evidence.Loudness.Length);
        Assert.Equal(40, evidence.Loudness[0]);
        Assert.Equal(SoundLoudness.Silent, evidence.Loudness[SoundLoudness.PerChunk]);
        Assert.Equal(60, evidence.Loudness[2 * SoundLoudness.PerChunk]);
        Assert.Equal(ChannelDifference.Unmeasured, evidence.Channels[ChannelDifference.PerChunk].Correlation);
        Assert.Equal(120, evidence.Channels[2 * ChannelDifference.PerChunk].Correlation);
        Assert.Equal(6100, evidence.Frames.Length);
        Assert.Equal(TimeSpan.FromSeconds(1200), evidence.Frames[6000].At);
        Assert.Equal(TimeSpan.FromSeconds(599.9), evidence.Frames[5999].At);
        Assert.Equal(1210 * CornerOutline.Bytes, evidence.Outlines.Length);
        Assert.False(evidence.Captioned);
    }

    [Fact(DisplayName = "captions are read when a part holds them, and a chunk missing from them shows none")]
    public void CaptionsAreReadWhenAPartHoldsThem()
    {
        byte[] shown = [.. Enumerable.Repeat(CaptionPresence.Shown, 600)];

        SegmentEvidence evidence = SegmentEvidence.Of([LearningDataPart.OfCaptions(1, shown)], TimeSpan.FromSeconds(1200));

        Assert.True(evidence.Captioned);
        Assert.Equal(1200, evidence.Captions.Length);
        Assert.Equal(CaptionPresence.Hidden, evidence.Captions[0]);
        Assert.Equal(CaptionPresence.Shown, evidence.Captions[600]);
    }

    [Fact(DisplayName = "readings of no length, frames out of order, broken corners and captions that are neither shown nor hidden are refused")]
    public void BrokenReadingsAreRefused()
    {
        PlacedFrame frame = new(TimeSpan.FromSeconds(1), new FrameLight(0, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => SegmentEvidence.Of(TimeSpan.Zero, [], [], [], [], null));
        Assert.Throws<ArgumentException>(() => SegmentEvidence.Of(TimeSpan.FromSeconds(10), [], [], [frame, frame], [], null));
        Assert.Throws<ArgumentException>(() => SegmentEvidence.Of(TimeSpan.FromSeconds(10), [], [], [], new byte[3], null));
        Assert.Throws<ArgumentException>(() => SegmentEvidence.Of(TimeSpan.FromSeconds(10), [], [], [], [], [2]));
    }

    private static LearningDataChunk Chunk(int index, int seconds, byte loudness, byte correlation)
    {
        FrameClock clock = FrameClock.Of(10, 1, LearningData.ChunkStarts(index));

        return LearningDataChunk.Of(
            index,
            clock,
            [],
            [.. Enumerable.Repeat(loudness, seconds * SoundReader.SampleRate / SoundLoudness.Hop)],
            [.. Enumerable.Repeat(new ChannelDifference(correlation, 100), seconds * SoundReader.SampleRate / ChannelDifference.Hop)],
            [.. Enumerable.Repeat(new FrameLight(128, 2), seconds * 10)],
            new byte[seconds * CornerOutline.Bytes]);
    }
}
