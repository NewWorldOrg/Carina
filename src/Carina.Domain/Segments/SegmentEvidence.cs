namespace Carina.Domain.Segments;

/// <summary>
/// The light of a frame and where it falls in the recording's own time.
/// </summary>
public readonly record struct PlacedFrame(TimeSpan At, FrameLight Light);

/// <summary>
/// The readings of one recording a detection reads, all in the recording's own time from zero: the
/// loudness every <see cref="LoudnessStep"/>, how the channels differ every <see cref="ChannelStep"/>,
/// the light of every frame in order, the corner outline of every second, and, when the recording's
/// captions were read, whether each second shows captions. Readings may reach past
/// <see cref="Length"/>; what lies past it is no candidate.
/// </summary>
public sealed class SegmentEvidence
{
    public static readonly TimeSpan LoudnessStep = TimeSpan.FromTicks(TimeSpan.TicksPerSecond * SoundLoudness.Hop / SoundReader.SampleRate);

    public static readonly TimeSpan ChannelStep = TimeSpan.FromTicks(TimeSpan.TicksPerSecond * ChannelDifference.Hop / SoundReader.SampleRate);

    private readonly byte[] loudness;

    private readonly ChannelDifference[] channels;

    private readonly PlacedFrame[] frames;

    private readonly byte[] outlines;

    private readonly byte[]? captions;

    private SegmentEvidence(TimeSpan length, byte[] loudness, ChannelDifference[] channels, PlacedFrame[] frames, byte[] outlines, byte[]? captions)
    {
        Length = length;
        this.loudness = loudness;
        this.channels = channels;
        this.frames = frames;
        this.outlines = outlines;
        this.captions = captions;
    }

    public TimeSpan Length { get; }

    public ReadOnlySpan<byte> Loudness => loudness;

    public ReadOnlySpan<ChannelDifference> Channels => channels;

    public ReadOnlySpan<PlacedFrame> Frames => frames;

    public ReadOnlySpan<byte> Outlines => outlines;

    public bool Captioned => captions is not null;

    public ReadOnlySpan<byte> Captions => captions;

    public static SegmentEvidence Of(
        TimeSpan length,
        ReadOnlySpan<byte> loudness,
        ReadOnlySpan<ChannelDifference> channels,
        IReadOnlyList<PlacedFrame> frames,
        ReadOnlySpan<byte> outlines,
        IReadOnlyList<byte>? captions)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(frames);

        for (int at = 1; at < frames.Count; at++)
        {
            if (frames[at].At <= frames[at - 1].At)
            {
                throw new ArgumentException("Frames follow one another in time.", nameof(frames));
            }
        }

        if (outlines.Length % CornerOutline.Bytes is not 0)
        {
            throw new ArgumentException($"Corner outlines come {CornerOutline.Bytes} bytes a second.", nameof(outlines));
        }

        if (captions is not null && captions.Any(second => second is not (CaptionPresence.Hidden or CaptionPresence.Shown)))
        {
            throw new ArgumentException("A second either shows captions or does not.", nameof(captions));
        }

        return new SegmentEvidence(length, loudness.ToArray(), channels.ToArray(), [.. frames], outlines.ToArray(), captions is null ? null : [.. captions]);
    }

    /// <summary>
    /// The readings <paramref name="parts"/> hold, each part placed at its chunk's own time. A chunk
    /// missing from the loudness is heard as silence, one missing from the channels as unmeasured, and one
    /// missing from the corners as empty corners. The captions are read when any part holds them.
    /// </summary>
    public static SegmentEvidence Of(IEnumerable<LearningDataPart> parts, TimeSpan length)
    {
        ArgumentNullException.ThrowIfNull(parts);

        LearningDataPart[] held = [.. parts.OrderBy(part => part.Index)];
        byte[] loudness = Filled(held, LearningDataKind.Loudness, SoundLoudness.PerChunk, SoundLoudness.Silent, part => part.Loudness.ToArray());
        ChannelDifference[] channels = Filled(
            held,
            LearningDataKind.ChannelDifferences,
            ChannelDifference.PerChunk,
            new ChannelDifference(ChannelDifference.Unmeasured, SoundLoudness.Silent),
            part => part.Channels.ToArray());
        byte[] outlines = Filled(held, LearningDataKind.CornerOutlines, CornerOutline.PerChunk * CornerOutline.Bytes, (byte)0, part => part.CornerOutlines.ToArray());
        PlacedFrame[] frames = [.. held.Where(part => part.Kind is LearningDataKind.FrameLights).SelectMany(Placed)];
        byte[]? captions = held.Any(part => part.Kind is LearningDataKind.CaptionPresence)
            ? Filled(held, LearningDataKind.CaptionPresence, CaptionPresence.PerChunk, CaptionPresence.Hidden, part => part.Captions.ToArray())
            : null;

        return Of(length, loudness, channels, frames, outlines, captions);
    }

    private static T[] Filled<T>(LearningDataPart[] parts, LearningDataKind kind, int perChunk, T missing, Func<LearningDataPart, T[]> readings)
    {
        LearningDataPart[] ofKind = [.. parts.Where(part => part.Kind == kind)];
        long reach = ofKind.Select(part => ((long)part.Index * perChunk) + readings(part).Length).DefaultIfEmpty(0).Max();
        T[] filled = new T[checked((int)reach)];

        Array.Fill(filled, missing);

        foreach (LearningDataPart part in ofKind)
        {
            T[] read = readings(part);

            Array.Copy(read, 0, filled, (long)part.Index * perChunk, read.Length);
        }

        return filled;
    }

    private static IEnumerable<PlacedFrame> Placed(LearningDataPart part)
    {
        FrameClock clock = part.Clock ?? throw new ArgumentException("The light of frames comes with the clock they fall on.", nameof(part));
        FrameLight[] lights = part.Frames.ToArray();

        return lights.Select((light, at) => new PlacedFrame(clock.At(at), light));
    }
}
