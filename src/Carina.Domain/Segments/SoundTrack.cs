namespace Carina.Domain.Segments;

/// <summary>
/// The sound of a recording as matching hears it: a fingerprint every <see cref="SegmentMatcher.Frame"/>
/// and a loudness every <see cref="SoundLoudness.Hop"/> samples, both from the recording's own time
/// zero. A chunk missing from the learning data is heard as silence.
/// </summary>
public sealed class SoundTrack
{
    private readonly uint[] fingerprints;

    private readonly byte[] loudness;

    private SoundTrack(uint[] fingerprints, byte[] loudness)
    {
        this.fingerprints = fingerprints;
        this.loudness = loudness;
    }

    public ReadOnlySpan<uint> Fingerprints => fingerprints;

    public ReadOnlySpan<byte> Loudness => loudness;

    public TimeSpan Length => SegmentMatcher.Frame * fingerprints.Length;

    public static SoundTrack Of(ReadOnlySpan<uint> fingerprints, ReadOnlySpan<byte> loudness)
        => new(fingerprints.ToArray(), loudness.ToArray());

    /// <summary>
    /// The sound the fingerprints and loudness among <paramref name="parts"/> make, each part placed at
    /// its chunk's own time. Parts of the other kinds are passed over.
    /// </summary>
    public static SoundTrack Of(IEnumerable<LearningDataPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        LearningDataPart[] fingerprinted = [.. parts.Where(part => part.Kind is LearningDataKind.SoundFingerprints or LearningDataKind.Loudness)];
        uint[] fingerprints = new uint[Reach(fingerprinted, LearningDataKind.SoundFingerprints, SoundFingerprint.PerChunk)];
        byte[] loudness = new byte[Reach(fingerprinted, LearningDataKind.Loudness, SoundLoudness.PerChunk)];

        Array.Fill(loudness, SoundLoudness.Silent);

        foreach (LearningDataPart part in fingerprinted)
        {
            if (part.Kind is LearningDataKind.SoundFingerprints)
            {
                part.Fingerprints.CopyTo(fingerprints.AsSpan(part.Index * SoundFingerprint.PerChunk));
            }
            else
            {
                part.Loudness.CopyTo(loudness.AsSpan(part.Index * SoundLoudness.PerChunk));
            }
        }

        return new SoundTrack(fingerprints, loudness);
    }

    /// <summary>
    /// The fingerprints of the frames <paramref name="span"/> covers, as far as the sound reaches.
    /// </summary>
    public uint[] Slice(SoundSpan span)
    {
        ArgumentNullException.ThrowIfNull(span);

        int from = Math.Clamp(SegmentMatcher.FrameAt(span.Starts), 0, fingerprints.Length);
        int to = Math.Clamp(SegmentMatcher.FrameAt(span.Ends), from, fingerprints.Length);

        return fingerprints[from..to];
    }

    /// <summary>
    /// Whether the loudness at the middle of the window a frame's fingerprint was taken over is
    /// silence; a frame past the loudness kept is not taken for silence.
    /// </summary>
    internal bool IsSilentAt(int frame)
    {
        long sample = ((long)frame * SoundFingerprint.Hop) + (SoundSpectrum.Window / 2);
        long reading = sample / SoundLoudness.Hop;

        return reading < loudness.Length && loudness[reading] == SoundLoudness.Silent;
    }

    private static int Reach(IEnumerable<LearningDataPart> parts, LearningDataKind kind, int perChunk)
        => parts
            .Where(part => part.Kind == kind)
            .Select(part => checked((part.Index * (long)perChunk) + part.Count))
            .Select(reach => checked((int)reach))
            .DefaultIfEmpty(0)
            .Max();
}
