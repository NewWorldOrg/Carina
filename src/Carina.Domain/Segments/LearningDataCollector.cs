using System.Runtime.InteropServices;

namespace Carina.Domain.Segments;

public sealed class LearningDataCollector
{
    private readonly SoundReader soundReader = new();

    private readonly PictureReader pictureReader = new();

    private readonly CornerReader cornerReader = new();

    private readonly SoundReadings heard = new();

    private readonly List<FrameLight> lights = [];

    private readonly List<byte> outlines = [];

    private long framesHandedBack;

    private int next;

    private bool finished;

    public LearningDataCollector(FrameClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        Clock = clock;
    }

    public FrameClock Clock { get; }

    public IReadOnlyList<LearningDataChunk> Hear(ReadOnlySpan<short> interleaved)
    {
        StillOpen();
        soundReader.Hear(interleaved, heard);

        return Whole();
    }

    public IReadOnlyList<LearningDataChunk> See(ReadOnlySpan<byte> frames)
    {
        StillOpen();
        pictureReader.See(frames, lights);

        return Whole();
    }

    public IReadOnlyList<LearningDataChunk> Glimpse(ReadOnlySpan<byte> pictures)
    {
        StillOpen();
        cornerReader.Glimpse(pictures, outlines);

        return Whole();
    }

    public IReadOnlyList<LearningDataChunk> Finish()
    {
        StillOpen();
        finished = true;
        soundReader.Finish(heard);

        List<LearningDataChunk> left = [];

        while (heard.Fingerprints.Count > 0
               || heard.Loudness.Count > 0
               || heard.Channels.Count > 0
               || lights.Count > 0
               || outlines.Count > 0)
        {
            left.Add(Cut());
        }

        return left;
    }

    private List<LearningDataChunk> Whole()
    {
        List<LearningDataChunk> whole = [];

        while (heard.Fingerprints.Count >= SoundFingerprint.PerChunk
               && heard.Loudness.Count >= SoundLoudness.PerChunk
               && heard.Channels.Count >= ChannelDifference.PerChunk
               && lights.Count >= FramesOfNext()
               && outlines.Count >= CornerOutline.PerChunk * CornerOutline.Bytes)
        {
            whole.Add(Cut());
        }

        return whole;
    }

    private long FramesOfNext() => Clock.FirstFrameFrom(LearningData.ChunkStarts(next + 1)) - framesHandedBack;

    private LearningDataChunk Cut()
    {
        int frames = (int)Math.Min(lights.Count, FramesOfNext());

        LearningDataChunk chunk = LearningDataChunk.Of(
            next,
            Clock.From(LearningData.ChunkStarts(next)),
            Taken(heard.Fingerprints, SoundFingerprint.PerChunk),
            Taken(heard.Loudness, SoundLoudness.PerChunk),
            Taken(heard.Channels, ChannelDifference.PerChunk),
            Taken(lights, frames),
            Taken(outlines, CornerOutline.PerChunk * CornerOutline.Bytes));

        framesHandedBack += frames;
        next++;

        return chunk;
    }

    private static T[] Taken<T>(List<T> readings, int most)
    {
        int count = Math.Min(readings.Count, most);
        T[] taken = CollectionsMarshal.AsSpan(readings)[..count].ToArray();
        readings.RemoveRange(0, count);

        return taken;
    }

    private void StillOpen()
    {
        if (finished)
        {
            throw new InvalidOperationException("The recording has been finished, and nothing more is gathered after its end.");
        }
    }
}
