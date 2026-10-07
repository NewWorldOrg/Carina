namespace Carina.Domain.Segments;

/// <summary>
/// One kind of the learning data of one chunk: the readings of <see cref="Kind"/> alone, the
/// others empty. Only the frame lights carry the <see cref="Clock"/> their frames fall on. Whether
/// captions are shown is not cut from a chunk, and is made with <see cref="OfCaptions"/>.
/// </summary>
public sealed class LearningDataPart
{
    private readonly uint[] fingerprints;

    private readonly byte[] loudness;

    private readonly ChannelDifference[] channels;

    private readonly FrameLight[] frames;

    private readonly byte[] outlines;

    private readonly byte[] captions;

    private LearningDataPart(
        LearningDataKind kind,
        int index,
        FrameClock? clock,
        uint[] fingerprints,
        byte[] loudness,
        ChannelDifference[] channels,
        FrameLight[] frames,
        byte[] outlines,
        byte[] captions)
    {
        Kind = kind;
        Index = index;
        Clock = clock;
        this.fingerprints = fingerprints;
        this.loudness = loudness;
        this.channels = channels;
        this.frames = frames;
        this.outlines = outlines;
        this.captions = captions;
    }

    public LearningDataKind Kind { get; }

    public int Index { get; }

    public TimeSpan Starts => LearningData.ChunkStarts(Index);

    public FrameClock? Clock { get; }

    public ReadOnlySpan<uint> Fingerprints => fingerprints;

    public ReadOnlySpan<byte> Loudness => loudness;

    public ReadOnlySpan<ChannelDifference> Channels => channels;

    public ReadOnlySpan<FrameLight> Frames => frames;

    public ReadOnlySpan<byte> CornerOutlines => outlines;

    public ReadOnlySpan<byte> Captions => captions;

    public int Count => Kind switch
    {
        LearningDataKind.SoundFingerprints => fingerprints.Length,
        LearningDataKind.Loudness => loudness.Length,
        LearningDataKind.ChannelDifferences => channels.Length,
        LearningDataKind.FrameLights => frames.Length,
        LearningDataKind.CornerOutlines => outlines.Length / CornerOutline.Bytes,
        _ => captions.Length,
    };

    public static LearningDataPart Of(LearningDataChunk chunk, LearningDataKind kind)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        return kind switch
        {
            LearningDataKind.SoundFingerprints => new(kind, chunk.Index, null, chunk.Fingerprints.ToArray(), [], [], [], [], []),
            LearningDataKind.Loudness => new(kind, chunk.Index, null, [], chunk.Loudness.ToArray(), [], [], [], []),
            LearningDataKind.ChannelDifferences => new(kind, chunk.Index, null, [], [], chunk.Channels.ToArray(), [], [], []),
            LearningDataKind.FrameLights => new(kind, chunk.Index, chunk.Clock, [], [], [], chunk.Frames.ToArray(), [], []),
            LearningDataKind.CornerOutlines => new(kind, chunk.Index, null, [], [], [], [], chunk.CornerOutlines.ToArray(), []),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A chunk gathered from a recording holds no data of this kind."),
        };
    }

    /// <summary>
    /// Whether captions are shown in each second of the chunk, from its start: <see cref="CaptionPresence.Shown"/>
    /// or <see cref="CaptionPresence.Hidden"/>.
    /// </summary>
    public static LearningDataPart OfCaptions(int index, ReadOnlySpan<byte> seconds)
    {
        string? fault = CaptionsFault(index, seconds);

        return fault is null
            ? new(LearningDataKind.CaptionPresence, index, null, [], [], [], [], [], seconds.ToArray())
            : throw new ArgumentException(fault);
    }

    internal static string? CaptionsFault(int index, ReadOnlySpan<byte> seconds)
    {
        if (index is < 0 or > LearningData.LastChunk)
        {
            return "A chunk is counted from zero, and no further than a count of seconds reaches.";
        }

        if (seconds.Length > CaptionPresence.PerChunk)
        {
            return $"A chunk holds at most {CaptionPresence.PerChunk} seconds, and this one holds {seconds.Length}.";
        }

        return seconds.ContainsAnyExcept(CaptionPresence.Hidden, CaptionPresence.Shown)
            ? "A second either shows captions or does not."
            : null;
    }

    internal static LearningDataPart Read(LearningDataKind kind, int index, FrameClock? clock, byte[] readings) => kind switch
    {
        LearningDataKind.SoundFingerprints => new(kind, index, null, LearningDataFormat.Fingerprints(readings), [], [], [], [], []),
        LearningDataKind.Loudness => new(kind, index, null, [], readings, [], [], [], []),
        LearningDataKind.ChannelDifferences => new(kind, index, null, [], [], LearningDataFormat.Channels(readings), [], [], []),
        LearningDataKind.FrameLights => new(kind, index, clock, [], [], [], LearningDataFormat.Frames(readings), [], []),
        LearningDataKind.CornerOutlines => new(kind, index, null, [], [], [], [], readings, []),
        _ => new(kind, index, null, [], [], [], [], [], readings),
    };
}
