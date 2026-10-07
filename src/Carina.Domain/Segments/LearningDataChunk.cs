namespace Carina.Domain.Segments;

/// <summary>
/// The learning data of the chunk of a recording that covers its own time from <see cref="Starts"/>
/// for <see cref="LearningData.ChunkSeconds"/>: fingerprints, loudness and channel differences at
/// their fixed steps from <see cref="Starts"/>, the light of every frame from the one
/// <see cref="Clock"/> starts at, and the corner outline of every second. The last chunk of a
/// recording may hold fewer of each, and no chunk holds more than its length takes. Each of
/// <see cref="Kinds"/> is kept on its own as a <see cref="LearningDataPart"/>.
/// </summary>
public sealed class LearningDataChunk
{
    public static readonly IReadOnlyList<LearningDataKind> Kinds =
    [
        LearningDataKind.SoundFingerprints,
        LearningDataKind.Loudness,
        LearningDataKind.ChannelDifferences,
        LearningDataKind.FrameLights,
        LearningDataKind.CornerOutlines,
    ];

    private readonly uint[] fingerprints;

    private readonly byte[] loudness;

    private readonly ChannelDifference[] channels;

    private readonly FrameLight[] frames;

    private readonly byte[] outlines;

    private LearningDataChunk(
        int index,
        FrameClock clock,
        uint[] fingerprints,
        byte[] loudness,
        ChannelDifference[] channels,
        FrameLight[] frames,
        byte[] outlines)
    {
        Index = index;
        Clock = clock;
        this.fingerprints = fingerprints;
        this.loudness = loudness;
        this.channels = channels;
        this.frames = frames;
        this.outlines = outlines;
    }

    public int Index { get; }

    public TimeSpan Starts => LearningData.ChunkStarts(Index);

    public FrameClock Clock { get; }

    public ReadOnlySpan<uint> Fingerprints => fingerprints;

    public ReadOnlySpan<byte> Loudness => loudness;

    public ReadOnlySpan<ChannelDifference> Channels => channels;

    public ReadOnlySpan<FrameLight> Frames => frames;

    public ReadOnlySpan<byte> CornerOutlines => outlines;

    public int Outlines => outlines.Length / CornerOutline.Bytes;

    public static LearningDataChunk Of(
        int index,
        FrameClock clock,
        ReadOnlySpan<uint> fingerprints,
        ReadOnlySpan<byte> loudness,
        ReadOnlySpan<ChannelDifference> channels,
        ReadOnlySpan<FrameLight> frames,
        ReadOnlySpan<byte> cornerOutlines)
    {
        ArgumentNullException.ThrowIfNull(clock);

        string? fault = Fault(index, clock, fingerprints.Length, loudness.Length, channels.Length, frames.Length, cornerOutlines.Length);

        return fault is null
            ? new LearningDataChunk(
                index,
                clock,
                fingerprints.ToArray(),
                loudness.ToArray(),
                channels.ToArray(),
                frames.ToArray(),
                cornerOutlines.ToArray())
            : throw new ArgumentException(fault);
    }

    public ReadOnlySpan<byte> OutlineAt(int second)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(second);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(second, Outlines);

        return outlines.AsSpan(second * CornerOutline.Bytes, CornerOutline.Bytes);
    }

    internal static string? Fault(int index, FrameClock clock, int fingerprints, int loudness, int channels, int frames, int outlineBytes)
    {
        if (index is < 0 or > LearningData.LastChunk)
        {
            return "A chunk is counted from zero, and no further than a count of seconds reaches.";
        }

        if (clock.FirstFrameAt < LearningData.ChunkStarts(index))
        {
            return "A chunk's frames start no earlier than the chunk does.";
        }

        if (outlineBytes % CornerOutline.Bytes is not 0)
        {
            return $"A corner outline is kept in {CornerOutline.Bytes} bytes.";
        }

        return Overfull("fingerprints", fingerprints, SoundFingerprint.PerChunk)
            ?? Overfull("loudness readings", loudness, SoundLoudness.PerChunk)
            ?? Overfull("channel differences", channels, ChannelDifference.PerChunk)
            ?? Overfull("frames", frames, clock.MostFramesIn(LearningData.ChunkSeconds))
            ?? Overfull("corner outlines", outlineBytes / CornerOutline.Bytes, CornerOutline.PerChunk);
    }

    private static string? Overfull(string readings, int count, int most)
        => count > most ? $"A chunk holds at most {most} {readings}, and this one holds {count}." : null;
}
